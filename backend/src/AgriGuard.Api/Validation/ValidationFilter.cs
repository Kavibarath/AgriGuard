using AgriGuard.Application.Common.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AgriGuard.Api.Validation;

/// <summary>
/// Runs any registered FluentValidation validator against each action argument before the action.
///
/// Why a filter rather than MVC's built-in model validation: the rules live in one testable class
/// per request type instead of scattered across attributes, and failures are raised as
/// <see cref="RequestValidationException"/> so they take the same RFC 7807 path as every other
/// error — one error shape for clients, whatever the source.
/// </summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        Dictionary<string, string[]>? errors = null;

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;

            var validator = services.GetService(typeof(IValidator<>).MakeGenericType(argument.GetType())) as IValidator;
            if (validator is null) continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (result.IsValid) continue;

            errors ??= [];
            foreach (var group in result.Errors.GroupBy(e => Camelise(e.PropertyName)))
                errors[group.Key] = [.. group.Select(e => e.ErrorMessage)];
        }

        if (errors is not null) throw new RequestValidationException(errors);

        await next();
    }

    /// <summary>Property names reach the client as camelCase, matching the JSON they sent.</summary>
    private static string Camelise(string name) =>
        string.IsNullOrEmpty(name) || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
