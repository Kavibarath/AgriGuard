namespace AgriGuard.Domain.Common;

/// <summary>
/// Base for every persisted entity. Ids are UUIDv7: time-ordered (cheap B-tree inserts)
/// yet non-sequential, so resource ids cannot be enumerated through the public API.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}

/// <summary>
/// Entities carrying audit fields. Values are stamped centrally in
/// AgriGuardDbContext.SaveChangesAsync — never set them manually.
/// </summary>
public abstract class AuditableEntity : Entity
{
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
