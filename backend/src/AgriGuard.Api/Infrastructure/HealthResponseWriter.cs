using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgriGuard.Api.Infrastructure;

/// <summary>
/// Compact JSON body for /health. Reports each check's name, status and duration only —
/// exception messages are deliberately omitted because they can leak hostnames or connection details.
/// </summary>
public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1)
            })
        });
    }
}
