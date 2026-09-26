using AgriGuard.Application.Cases;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Cases;

/// <summary>The prescription and order an approved run produced, as the console and the decision result show them.</summary>
internal static class IssuedPrescriptions
{
    public static Task<IssuedPrescriptionDto?> ForRunAsync(AgriGuardDbContext db, Guid runId, CancellationToken ct) =>
        db.InputOrders.AsNoTracking()
            .Where(o => o.Prescription!.AgentRunId == runId)
            .Select(o => new IssuedPrescriptionDto(
                o.Prescription!.Id,
                o.Prescription.PrescriptionNo,
                o.Prescription.Product.Name,
                o.Prescription.DosePerHectare,
                o.Prescription.TotalQuantity,
                o.Prescription.SprayDate,
                o.Prescription.EarliestSafeHarvestDate,
                o.Prescription.Instructions,
                o.Id,
                o.OrderNo,
                o.Dealer.ShopName,
                o.Lines.Sum(l => l.Packs),
                o.TotalAmount))
            .FirstOrDefaultAsync(ct);
}
