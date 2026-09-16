using System.Text.RegularExpressions;
using Serilog.Context;

namespace AgriGuard.Api.Infrastructure;

/// <summary>
/// Gives every request a correlation ID: reuses a well-formed inbound <c>X-Correlation-Id</c>
/// (so a Flutter/React request can be traced through the API and into the agent service),
/// otherwise generates one. The ID is echoed on the response and attached to every log line.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    private const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var inbound = context.Request.Headers[HeaderName].ToString();

        // Client-supplied values end up in logs, so only accept short, plain tokens —
        // never raw text that could forge log lines.
        var correlationId = SafeId().IsMatch(inbound) ? inbound : Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(ItemKey, correlationId))
        {
            await next(context);
        }
    }

    public static string? Get(HttpContext context) => context.Items[ItemKey] as string;

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex SafeId();
}
