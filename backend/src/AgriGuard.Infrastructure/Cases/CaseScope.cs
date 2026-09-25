using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Domain.Cases;
using AgriGuard.Domain.Identity;

namespace AgriGuard.Infrastructure.Cases;

/// <summary>
/// Row-level access for cases and their runs — the same rule as the registry (see RegistryScope):
/// farmer → own cases, agronomist → their district's, administrator → all, anyone else → none.
/// Applied inside the query so paging counts only rows the caller may see.
/// </summary>
internal static class CaseScope
{
    public static IQueryable<CropCase> ScopedTo(this IQueryable<CropCase> cases, ICurrentUserAccessor user) => user.Role switch
    {
        UserRole.Farmer when user.UserId is { } farmerId => cases.Where(c => c.FarmerId == farmerId),
        UserRole.FieldAgronomist when user.DistrictId is { } districtId => cases.Where(c => c.DistrictId == districtId),
        UserRole.CoopAdministrator => cases,
        _ => cases.Where(_ => false)
    };

    public static IQueryable<AgentRun> ScopedTo(this IQueryable<AgentRun> runs, ICurrentUserAccessor user) => user.Role switch
    {
        UserRole.Farmer when user.UserId is { } farmerId => runs.Where(r => r.Case.FarmerId == farmerId),
        UserRole.FieldAgronomist when user.DistrictId is { } districtId => runs.Where(r => r.Case.DistrictId == districtId),
        UserRole.CoopAdministrator => runs,
        _ => runs.Where(_ => false)
    };

    /// <summary>404 when the row does not exist, 403 when it exists but is not the caller's.</summary>
    public static void EnsureVisible<T>(T? scoped, bool existsUnscoped, string resource, Guid id)
    {
        if (scoped is not null) return;
        if (existsUnscoped) throw new ForbiddenAccessException($"This {resource.ToLowerInvariant()} is outside your farms or district.");
        throw new NotFoundException(resource, id);
    }

    /// <summary>
    /// The terminal run statuses as a query predicate. Spelled out rather than using a set's
    /// Contains, because the statuses are stored as strings through a value converter.
    /// </summary>
    public static IQueryable<AgentRun> WhereNotTerminal(this IQueryable<AgentRun> runs) => runs.Where(r =>
        r.Status != AgentRunStatus.Completed && r.Status != AgentRunStatus.Rejected
        && r.Status != AgentRunStatus.Failed && r.Status != AgentRunStatus.TimedOut);
}
