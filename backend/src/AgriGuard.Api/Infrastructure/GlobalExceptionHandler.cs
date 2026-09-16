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
        RequestValidationException e => new HttpValidationProblemDetails(e.Errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
            Detail = e.Message
        },
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

    private static ProblemDetails Problem(int status, string title, string? detail) =>
        new() { Status = status, Title = title, Detail = detail };

    private static ProblemDetails WithCode(ProblemDetails problem, string code)
    {
        problem.Extensions["code"] = code;
        return problem;
    }
}
