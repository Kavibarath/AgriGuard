using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgriGuard.Infrastructure.Persistence;

/// <summary>Recognises the database errors that the serializable flows (approval, reservations) recover from.</summary>
internal static class PostgresErrors
{
    /// <summary>
    /// Lost a race: a serialization failure (40001), a deadlock (40P01), or a stale row version.
    /// Starting again from fresh data is the right answer to all three.
    /// </summary>
    public static bool IsRaceLost(Exception ex) =>
        ex is DbUpdateConcurrencyException
        || Find(ex)?.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected;

    /// <summary>A unique index was violated; <paramref name="constraintPart"/> narrows it to one index.</summary>
    public static bool IsUniqueViolation(Exception ex, string? constraintPart = null) =>
        Find(ex) is { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && (constraintPart is null || (pg.ConstraintName?.Contains(constraintPart, StringComparison.Ordinal) ?? false));

    /// <summary>A row is still referenced by another table's foreign key.</summary>
    public static bool IsForeignKeyViolation(Exception ex) =>
        Find(ex)?.SqlState == PostgresErrorCodes.ForeignKeyViolation;

    private static PostgresException? Find(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is PostgresException pg) return pg;
        return null;
    }
}
