using System.Linq.Expressions;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Models;
using AgriGuard.Application.Inventory;
using AgriGuard.Domain.Inventory;
using AgriGuard.Infrastructure.Persistence;
using AgriGuard.Infrastructure.Registry;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Inventory;

/// <summary>The agro-input catalogue. Reads are open to every signed-in role; the controller limits writes to administrators.</summary>
public sealed class ProductService(AgriGuardDbContext db) : IProductService
{
    private static readonly Dictionary<string, Expression<Func<Product, object>>> Sortable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = p => p.Name,
        ["activeIngredient"] = p => p.ActiveIngredient.Name,
        ["unitPrice"] = p => p.UnitPrice,
        ["manufacturer"] = p => p.Manufacturer!
    };

    public async Task<PagedResult<ProductDto>> ListAsync(ProductQuery query, CancellationToken ct = default)
    {
        var products = db.Products.AsNoTracking();

        // Withdrawn products are hidden unless asked for: nobody should pick one by accident.
        if (!query.IncludeInactive)
            products = products.Where(p => p.IsActive);
        if (query.ActiveIngredientId is { } ingredientId)
            products = products.Where(p => p.ActiveIngredientId == ingredientId);
        if (query.CropId is { } cropId)
            products = products.Where(p => p.CropApprovals.Any(a => a.CropId == cropId && a.IsActive));
        if (query.Search is { Length: > 0 } search)
        {
            var pattern = $"%{search.Trim()}%";
            products = products.Where(p => EF.Functions.ILike(p.Name, pattern)
                                        || EF.Functions.ILike(p.ActiveIngredient.Name, pattern)
                                        || (p.Manufacturer != null && EF.Functions.ILike(p.Manufacturer, pattern)));
        }

        return await products
            .OrderByAllowed(query, Sortable, p => p.Name, p => p.Id)
            .Select(Projection)
            .ToPagedResultAsync(query, ct);
    }

    public async Task<ProductDto> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.Products.AsNoTracking().Where(p => p.Id == id).Select(Projection).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Product", id);

    public async Task<ProductDto> CreateAsync(ProductRequest request, CancellationToken ct = default)
    {
        var product = new Product();
        await ApplyAsync(product, request, ct);
        db.Products.Add(product);
        await SaveAsync(product.Name, ct);
        return await GetAsync(product.Id, ct);
    }

    public async Task<ProductDto> UpdateAsync(Guid id, ProductRequest request, CancellationToken ct = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Product", id);
        await ApplyAsync(product, request, ct);
        await SaveAsync(product.Name, ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Only a product nothing refers to can be deleted. Once it has a rule, stock or a prescription
    /// it is history: withdraw it (IsActive = false) instead, which stops new use but keeps the record.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Product", id);

        var inUse = await db.ProductCropApprovals.AnyAsync(a => a.ProductId == id, ct)
                    || await db.InventoryBatches.AnyAsync(b => b.ProductId == id, ct)
                    || await db.Prescriptions.AnyAsync(p => p.ProductId == id, ct)
                    || await db.ChemicalApplications.AnyAsync(a => a.ProductId == id, ct);
        if (inUse)
            throw new ConflictException($"{product.Name} has rules, stock or treatment history. Withdraw it (set it inactive) instead of deleting it.");

        db.Products.Remove(product);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PostgresErrors.IsForeignKeyViolation(ex))
        {
            // Referenced from somewhere the check above does not look (an order line, a reservation).
            throw new ConflictException($"{product.Name} is still referenced by orders or holds. Withdraw it instead of deleting it.");
        }
    }

    public async Task<IReadOnlyList<ActiveIngredientDto>> ListActiveIngredientsAsync(CancellationToken ct = default) =>
        await db.ActiveIngredients.AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new ActiveIngredientDto(a.Id, a.Name, a.ChemicalClass, a.ResistanceGroup))
            .ToListAsync(ct);

    private async Task ApplyAsync(Product product, ProductRequest request, CancellationToken ct)
    {
        if (!await db.ActiveIngredients.AnyAsync(a => a.Id == request.ActiveIngredientId, ct))
            throw new NotFoundException("Active ingredient", request.ActiveIngredientId);

        var name = request.Name.Trim();
        if (await db.Products.AnyAsync(p => p.Name == name && p.Id != product.Id, ct))
            throw new ConflictException($"A product called '{name}' already exists.");

        product.Name = name;
        product.Manufacturer = string.IsNullOrWhiteSpace(request.Manufacturer) ? null : request.Manufacturer.Trim();
        product.ActiveIngredientId = request.ActiveIngredientId;
        product.Formulation = request.Formulation;
        product.Unit = request.Unit;
        product.PackSize = request.PackSize;
        product.UnitPrice = request.UnitPrice;
        product.IsActive = request.IsActive;
    }

    private async Task SaveAsync(string name, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex))
        {
            throw new ConflictException($"A product called '{name}' already exists.");
        }
    }

    private static Expression<Func<Product, ProductDto>> Projection => p => new ProductDto(
        p.Id,
        p.Name,
        p.Manufacturer,
        p.ActiveIngredientId,
        p.ActiveIngredient.Name,
        p.ActiveIngredient.ResistanceGroup,
        p.Formulation,
        p.Unit,
        p.PackSize,
        p.UnitPrice,
        p.IsActive,
        p.CropApprovals.Count(a => a.IsActive));
}
