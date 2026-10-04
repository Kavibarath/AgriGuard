using System.Data;
using AgriGuard.Application.Common.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Persistence;

/// <summary>
/// Runs a unit of work in one serializable transaction and retries it when it loses a race to
/// another transaction touching the same rows. Each retry starts from fresh data, so it sees what
/// the winner did — for example, that the hold it wanted to commit was released a moment ago.
/// </summary>
internal static class SerializableTransaction
{
    public const int MaxAttempts = 3;

    /// <param name="conflictMessage">What the caller is told if every attempt loses (409).</param>
    public static async Task<T> RunAsync<T>(AgriGuardDbContext db, Func<Task<T>> work, string conflictMessage, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var result = await work();
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return result;
            }
            catch (Exception ex) when (PostgresErrors.IsRaceLost(ex))
            {
                db.ChangeTracker.Clear();
                if (attempt >= MaxAttempts)
                    throw new ConflictException(conflictMessage);
            }
        }
    }
}
