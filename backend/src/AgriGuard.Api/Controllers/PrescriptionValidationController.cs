using AgriGuard.Application.Auth;
using AgriGuard.Application.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriGuard.Api.Controllers;

/// <summary>
/// The deterministic safety gate, exposed for review and for the agent service.
///
/// Read-only: validating changes nothing. Issuing a prescription and committing stock are
/// separate operations that ASP.NET Core performs only after a human approves (§9.5), which is
/// why the agent can call this freely and still cannot cause harm.
/// </summary>
[ApiController]
[Route("api/prescriptions")]
[Authorize(Policy = AuthPolicies.OwnsFarm)]
public sealed class PrescriptionValidationController(IPrescriptionValidationService validation) : ControllerBase
{
    /// <summary>
    /// Checks a proposed prescription against rules V1–V11 and returns every rule's finding —
    /// passed, failed or not evaluated — with the evidence it was decided on.
    ///
    /// Always 200: a rejection is a successful validation with a negative answer, not an error.
    /// The agent service will reach the same logic through /internal/tools/validate-prescription
    /// once the tool surface and its service key exist.
    /// </summary>
    [HttpPost("validate")]
    [ProducesResponseType<ValidationVerdictDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<ValidationVerdictDto> Validate(ValidatePrescriptionRequest request, CancellationToken ct) =>
        validation.ValidateAsync(request, ct);
}
