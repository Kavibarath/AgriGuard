using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Persistence;

internal static class Sequences
{
    /// <summary>"BK-2026-000001" from a PostgreSQL sequence, so concurrent requests never collide.</summary>
    public static async Task<string> NextAsync(AgriGuardDbContext db, string sequence, string prefix, int year, CancellationToken ct)
    {
        // The sequence name goes in as a parameter, cast to regclass, not spliced into the SQL.
        var next = await db.Database.SqlQuery<long>($"SELECT nextval({sequence}::regclass) AS \"Value\"").SingleAsync(ct);
        return $"{prefix}-{year}-{next:D6}";
    }
}
