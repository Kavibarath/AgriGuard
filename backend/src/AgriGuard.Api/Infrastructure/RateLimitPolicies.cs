using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgriGuard.Api.Infrastructure;

public static class RateLimitPolicies
{
    /// <summary>Credential endpoints: /api/auth/*.</summary>
    public const string Auth = "auth";

    /// <summary>Case submission, which starts an agent run and therefore costs real LLM time.</summary>
    public const string Cases = "cases";

    public static IServiceCollection AddAgriGuardRateLimiting(this IServiceCollection services, IConfiguration configuration) =>
        services.AddRateLimiter(options =>
        {
            // Configurable so the integration tests can tighten or loosen a bucket without
            // waiting out a real one-minute window.
            options.AddPolicy(Auth, PerClient(
                configuration.GetValue("RateLimiting:Auth:PermitLimit", 10),
                configuration.GetValue("RateLimiting:Auth:Window", TimeSpan.FromMinutes(1))));
            options.AddPolicy(Cases, PerClient(
                configuration.GetValue("RateLimiting:Cases:PermitLimit", 30),
                configuration.GetValue("RateLimiting:Cases:Window", TimeSpan.FromMinutes(1))));

            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

                // Same RFC 7807 shape as every other error response.
                var problemDetailsService = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = "Rate limit exceeded. Wait a moment and try again."
                    }
                });
            };
        });

    /// <summary>
    /// Keyed by authenticated user when there is one, otherwise by remote IP — so one noisy
    /// client cannot lock every other user out of logging in.
    /// </summary>
    private static Func<HttpContext, RateLimitPartition<string>> PerClient(int limit, TimeSpan window) =>
        context => RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = window });
}
