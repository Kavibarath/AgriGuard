using AgriGuard.Application.Common.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriGuard.IntegrationTests.Infrastructure;

/// <summary>
/// Throws each exception type through the real middleware pipeline so the ProblemDetails
/// mapping is tested end to end. Only registered by <see cref="AgriGuardApiFactory"/>.
/// </summary>
[ApiController]
[Route("_test/faults")]
// Exercises the exception pipeline, not authorization; the fallback policy would otherwise 401 these.
[AllowAnonymous]
public sealed class FaultsController : ControllerBase
{
    [HttpGet("not-found")]
    public IActionResult NotFound_() => throw new NotFoundException("Plot", 42);

    [HttpGet("forbidden")]
    public IActionResult Forbidden_() => throw new ForbiddenAccessException();

    [HttpGet("conflict")]
    public IActionResult Conflict_() => throw new ConflictException("Case already has a pending run.");

    [HttpGet("validation")]
    public IActionResult Validation() => throw new RequestValidationException("areaHectares", "Must be greater than 0.");

    [HttpGet("business-rule")]
    public IActionResult BusinessRule() =>
        throw new BusinessRuleException("ILLEGAL_STAGE_TRANSITION", "Cannot move from Sown to Harvested.");

    [HttpGet("unhandled")]
    public IActionResult Unhandled() => throw new InvalidOperationException("secret connection detail");
}
