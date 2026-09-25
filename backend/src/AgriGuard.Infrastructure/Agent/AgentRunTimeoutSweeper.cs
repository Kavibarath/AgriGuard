using AgriGuard.Domain.Cases;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgriGuard.Infrastructure.Agent;

/// <summary>
/// Marks runs TimedOut when the agent never reports back. The agent enforces its own five-minute
/// limit and always posts a result, so this only fires when the agent process itself died or lost
/// the network mid-run. Without it the case would sit in AgentProcessing forever, and no new run
/// could start, because a case may have only one run in flight.
/// </summary>
public sealed class AgentRunTimeoutSweeper(
    IServiceScopeFactory scopes,
    IOptions<AgentServiceOptions> options,
    TimeProvider timeProvider,
    ILogger<AgentRunTimeoutSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.SweepInterval, timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed sweep (database briefly unreachable) must not stop the next one.
                logger.LogError(ex, "Agent run timeout sweep failed");
            }
        }
    }

    public async Task<int> SweepAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgriGuardDbContext>();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var timeout = options.Value.RunTimeout;
        var cutoff = now - timeout;

        // Only runs the agent itself is still working on. PendingApproval waits on a person and
        // may legitimately wait for days.
        var stale = await db.AgentRuns
            .Include(r => r.Case)
            .Include(r => r.Steps)
            .Where(r => r.StartedAt < cutoff
                        && (r.Status == AgentRunStatus.Planning || r.Status == AgentRunStatus.Diagnosing
                            || r.Status == AgentRunStatus.Drafting || r.Status == AgentRunStatus.Validating
                            || r.Status == AgentRunStatus.RevisionRequested))
            .ToListAsync(ct);

        foreach (var run in stale)
        {
            var from = run.Status;
            AgentRunLifecycle.End(run, AgentRunStatus.TimedOut,
                $"The agent service did not report a result within {timeout.TotalMinutes:0} minutes.", now);
            db.AgentRunEvents.Add(new AgentRunEvent
            {
                AgentRunId = run.Id,
                EventType = AgentEventType.RunFailed,
                PayloadJson = AgentPayloads.ToStorable(new { reason = run.FailureReason }),
                OccurredAt = now
            });
            db.AgentRunEvents.Add(AgentRunLifecycle.StatusChanged(run.Id, from, AgentRunStatus.TimedOut, now));
        }

        if (stale.Count == 0)
            return 0;

        try
        {
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Timed out {Count} agent run(s) with no result after {Timeout}", stale.Count, timeout);
            return stale.Count;
        }
        catch (DbUpdateConcurrencyException)
        {
            // A result arrived at the same moment; it wins, and the next sweep re-checks.
            return 0;
        }
    }
}
