using AgriGuard.Domain.Common;
using AgriGuard.Domain.Reference;

namespace AgriGuard.Domain.Identity;

public enum UserRole
{
    Farmer,
    FieldAgronomist,
    AgroDealer,
    CoopAdministrator
}

public class User : AuditableEntity
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }

    /// <summary>Password hash only — plaintext passwords are never stored (§6).</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    /// <summary>Farmers' home district; agronomists' assigned district (scopes case access).</summary>
    public Guid? DistrictId { get; set; }
    public District? District { get; set; }

    /// <summary>Farmers only: maximum input-order value (rule V11).</summary>
    public decimal? CreditLimit { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}

public class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>SHA-256 of the token; the raw token only ever lives on the client.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public bool IsActive(DateTime utcNow) => RevokedAt is null && ExpiresAt > utcNow;
}
