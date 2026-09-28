using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Application.Reports;
using AgriGuard.Domain.Registry;
using AgriGuard.Infrastructure.Persistence;
using AgriGuard.Infrastructure.Registry;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Reports;

/// <summary>
/// GET /api/reports/plot-treatment-history (Component A): every spray on a plot across its crop
/// cycles, with the pre-harvest interval each one set, and how often each active ingredient was used
/// (the resistance-management view behind rules V6 and V7). Scoped like the plot itself.
/// </summary>
public sealed class RegistryReportService(AgriGuardDbContext db, ICurrentUserAccessor currentUser) : IRegistryReportService
{
    public async Task<PlotTreatmentHistoryReport> PlotTreatmentHistoryAsync(PlotTreatmentQuery query, CancellationToken ct = default)
    {
        if (query.From is { } start && query.To is { } end && start > end)
            throw new RequestValidationException(nameof(query.From), "The start date must be on or before the end date.");

        var plot = await db.Plots.AsNoTracking().ScopedTo(currentUser)
            .Where(p => p.Id == query.PlotId)
            .Select(p => new { p.Id, p.PlotCode, FarmName = p.Farm.Name, p.AreaHectares })
            .FirstOrDefaultAsync(ct);
        RegistryScope.EnsureVisible(plot, await db.Plots.AnyAsync(p => p.Id == query.PlotId, ct), "Plot", query.PlotId);

        var applications = db.ChemicalApplications.AsNoTracking().Where(a => a.CropCycle.PlotId == query.PlotId);
        if (query.From is { } from)
            applications = applications.Where(a => a.ApplicationDate >= from);
        if (query.To is { } to)
            applications = applications.Where(a => a.ApplicationDate <= to);

        var rows = await applications
            .OrderByDescending(a => a.ApplicationDate).ThenByDescending(a => a.CreatedAt)
            .Select(a => new
            {
                a.Id,
                a.ApplicationDate,
                CropName = a.CropCycle.Crop.Name,
                a.CropCycle.SownDate,
                ProductName = a.Product.Name,
                ActiveIngredient = a.Product.ActiveIngredient.Name,
                a.Product.ActiveIngredient.ResistanceGroup,
                a.Product.Unit,
                a.DosePerHectare,
                a.TotalQuantity,
                a.Status,
                PrescriptionNo = a.Prescription == null ? null : a.Prescription.PrescriptionNo,
                Phi = db.ProductCropApprovals
                    .Where(r => r.ProductId == a.ProductId && r.CropId == a.CropCycle.CropId)
                    .Select(r => (int?)r.PreHarvestIntervalDays)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var counted = rows.Where(r => r.Status != ApplicationStatus.Cancelled).ToList();

        return new PlotTreatmentHistoryReport(
            plot!.Id, plot.PlotCode, plot.FarmName, plot.AreaHectares, query.From, query.To,
            counted.Count,
            [.. counted.GroupBy(r => (r.ActiveIngredient, r.ResistanceGroup))
                .Select(g => new ActiveIngredientUseDto(g.Key.ActiveIngredient, g.Key.ResistanceGroup, g.Count(), g.Max(r => r.ApplicationDate)))
                .OrderByDescending(u => u.Applications).ThenBy(u => u.ActiveIngredient, StringComparer.Ordinal)],
            [.. rows.Select(r => new TreatmentRowDto(
                r.Id, r.ApplicationDate, r.CropName, r.SownDate, r.ProductName, r.ActiveIngredient, r.ResistanceGroup,
                r.Unit.ToString(), r.DosePerHectare, r.TotalQuantity, r.Status, r.PrescriptionNo,
                r.Phi, r.Phi is { } days && r.Status != ApplicationStatus.Cancelled ? r.ApplicationDate.AddDays(days) : null))]);
    }
}
