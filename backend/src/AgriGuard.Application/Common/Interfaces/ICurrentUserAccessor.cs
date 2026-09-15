namespace AgriGuard.Application.Common.Interfaces;

/// <summary>
/// The authenticated caller, resolved from JWT claims by the API layer.
/// Null values mean "no user" (background jobs, seeding, agent callbacks).
/// </summary>
public interface ICurrentUserAccessor
{
    Guid? UserId { get; }
}
