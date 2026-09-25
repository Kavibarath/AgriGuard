using AgriGuard.Application.Cases;
using AgriGuard.Domain.Cases;
using AgriGuard.Domain.Reference;
using FluentValidation;

namespace AgriGuard.Api.Validation;

// Shape checks only. Whether the plot is the caller's, and whether the cycle is on it and active,
// are checked in CaseService where the data is.

public sealed class CreateCaseRequestValidator : AbstractValidator<CreateCaseRequest>
{
    public const int MaxSymptoms = 10;

    /// <summary>Matches the column length: the first line of defence on the untrusted field.</summary>
    public const int MaxFarmerNoteLength = 1000;

    public CreateCaseRequestValidator()
    {
        RuleFor(x => x.PlotId).NotEmpty().WithMessage("Choose the plot the problem is on.");
        RuleFor(x => x.CropCycleId).NotEmpty().WithMessage("Choose the crop the problem is on.");

        RuleFor(x => x.SymptomCodes)
            .NotEmpty().WithMessage("Tick at least one symptom.")
            .Must(codes => codes is null || codes.Count <= MaxSymptoms)
            .WithMessage($"Tick at most {MaxSymptoms} symptoms.");

        // A closed list, so the agent gets structured symptoms it can reason about, not free text.
        RuleForEach(x => x.SymptomCodes)
            .Must(code => code is not null && SymptomCatalogue.IsValid(code.Trim().ToLowerInvariant()))
            .WithMessage((_, code) => $"'{code}' is not a known symptom code.");

        RuleFor(x => x.FarmerNote).MaximumLength(MaxFarmerNoteLength);

        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).WithMessage("Latitude must be between -90 and 90.");
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).WithMessage("Longitude must be between -180 and 180.");

        RuleFor(x => x.Severity).IsInEnum();
    }
}

public sealed class DecideRequestValidator : AbstractValidator<DecideRequest>
{
    public DecideRequestValidator()
    {
        RuleFor(x => x.Decision).IsInEnum().WithMessage("Decision must be Approve, Reject or Revise.");

        // A rejection is audited; a revision's reason is what the agent is told to fix. Neither is
        // useful as "no" or "fix it".
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Give a reason when rejecting or requesting a revision.")
            .MinimumLength(10).WithMessage("Say what is wrong in a sentence, so the reason is useful later.")
            .When(x => x.Decision is ApprovalDecisionType.Reject or ApprovalDecisionType.Revise);

        RuleFor(x => x.Reason).MaximumLength(1000);

        // The agent accepts a reviewer note of at most this length.
        RuleFor(x => x.Reason)
            .MaximumLength(ApprovalLimits.MaxReviewerNoteLength)
            .When(x => x.Decision == ApprovalDecisionType.Revise)
            .WithMessage($"Keep revision guidance under {ApprovalLimits.MaxReviewerNoteLength} characters; it is passed to the agent.");
    }
}

public sealed class UpdateCaseStatusRequestValidator : AbstractValidator<UpdateCaseStatusRequest>
{
    public UpdateCaseStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
    }
}
