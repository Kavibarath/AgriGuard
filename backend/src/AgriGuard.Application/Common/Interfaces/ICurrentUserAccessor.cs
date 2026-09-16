using AgriGuard.Domain.Identity;

namespace AgriGuard.Application.Common.Interfaces;

/// <summary>
/// The authenticated caller, resolved from JWT claims by the API layer.
/// Null values mean "no user" (background jobs, seeding, agent callbacks).
/// </summary>
public interface ICurrentUserAccessor
{
    Guid? UserId { get; }

    UserRole? Role { get; }

    /// <summary>District from the token; scopes an agronomist's view of cases.</summary>
    Guid? DistrictId { get; }

    string? Email { get; }
}
