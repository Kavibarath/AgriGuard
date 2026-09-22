using AgriGuard.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Api.Infrastructure;

/// <summary>
/// Last line of defence: turns any exception escaping a controller into an RFC 7807 response.
/// Expected <see cref="AppException"/>s keep their message; unexpected exceptions are logged
/// and returned as a generic 500 so stack traces and SQL never reach the client.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        // Client disconnected mid-request: nothing to send and nothing worth an error log.
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            return true;
        }

        var problem = Map(exception);

        if (problem.Status >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        else
            logger.LogInformation("Request rejected with {StatusCode}: {Reason}", problem.Status, exception.Message);

        context.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private ProblemDetails Map(Exception exception) => exception switch
    {
        // Keys are camelised centrally so a client sees the same field name whether the rule
        // came from a FluentValidation validator or was thrown by a service.
        RequestValidationException e => new HttpValidationProblemDetails(Camelise(e.Errors))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
            Detail = e.Message
        },
        AuthenticationFailedException e => Problem(StatusCodes.Status401Unauthorized, "Authentication failed", e.Message),
        NotFoundException e => Problem(StatusCodes.Status404NotFound, "Resource not found", e.Message),
        ForbiddenAccessException e => Problem(StatusCodes.Status403Forbidden, "Forbidden", e.Message),
        ConflictException e => Problem(StatusCodes.Status409Conflict, "Conflict", e.Message),
        // Optimistic-concurrency token mismatch: someone else changed the row first.
        DbUpdateConcurrencyException => Problem(StatusCodes.Status409Conflict, "Conflict",
            "The resource was modified by another request. Reload and try again."),
        BusinessRuleException e => WithCode(
            Problem(StatusCodes.Status422UnprocessableEntity, "Business rule violated", e.Message), e.Code),
        _ => Problem(StatusCodes.Status500InternalServerError, "An unexpected error occurred",
            environment.IsDevelopment() ? exception.ToString() : null)
    };

    /// <summary>Field names reach the client as camelCase, matching the JSON they sent.</summary>
    private static Dictionary<string, string[]> Camelise(IDictionary<string, string[]> errors) =>
        errors.ToDictionary(
            pair => string.IsNullOrEmpty(pair.Key) || char.IsLower(pair.Key[0])
                ? pair.Key
                : char.ToLowerInvariant(pair.Key[0]) + pair.Key[1..],
            pair => pair.Value);

    private static ProblemDetails Problem(int status, string title, string? detail) =>
        new() { Status = status, Title = title, Detail = detail };

    private static ProblemDetails WithCode(ProblemDetails problem, string code)
    {
        problem.Extensions["code"] = code;
        return problem;
    }
}
