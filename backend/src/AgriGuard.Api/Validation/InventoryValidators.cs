using AgriGuard.Application.Inventory;
using AgriGuard.Domain.Inventory;
using FluentValidation;

namespace AgriGuard.Api.Validation;

// Shape and range checks only. Whether a product exists, a batch number is taken, or there is
// enough stock to hold is checked in the services, where the data is. The bounds on the rules
// match the table's CHECK constraints — the database is the backstop, this is the message.

public sealed class ProductRequestValidator : AbstractValidator<ProductRequest>
{
    public ProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Give the product its label name.").MaximumLength(200);
        RuleFor(x => x.Manufacturer).MaximumLength(150);
        RuleFor(x => x.ActiveIngredientId).NotEmpty().WithMessage("Choose the active ingredient.");
        RuleFor(x => x.Formulation).IsInEnum();
        RuleFor(x => x.Unit).IsInEnum();
        RuleFor(x => x.PackSize).GreaterThan(0).WithMessage("Pack size must be more than zero.").LessThanOrEqualTo(1000);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).WithMessage("Price cannot be negative.").LessThanOrEqualTo(10_000_000);
    }
}

/// <summary>Every limit of a rule, for both create and update.</summary>
public sealed class ApprovalLimitsValidator<T> : AbstractValidator<T> where T : IApprovalLimits
{
    public ApprovalLimitsValidator()
    {
        RuleFor(x => x.MinDosePerHectare).GreaterThan(0).WithMessage("The minimum dose must be more than zero.");
        RuleFor(x => x.MaxDosePerHectare)
            .GreaterThanOrEqualTo(x => x.MinDosePerHectare).WithMessage("The maximum dose cannot be below the minimum.")
            .LessThanOrEqualTo(1000);
        RuleFor(x => x.PreHarvestIntervalDays).InclusiveBetween(0, 365).WithMessage("The pre-harvest interval must be 0–365 days.");
        RuleFor(x => x.ReEntryIntervalHours).InclusiveBetween(0, 720).WithMessage("The re-entry interval must be 0–720 hours.");
        RuleFor(x => x.MaxApplicationsPerCycle).InclusiveBetween(1, 20).WithMessage("Allow 1–20 applications per crop cycle.");
        RuleFor(x => x.MinDaysBetweenApplications).InclusiveBetween(0, 180).WithMessage("The gap between applications must be 0–180 days.");
        RuleFor(x => x.RainfastHours).InclusiveBetween(0, 72).WithMessage("Rainfast time must be 0–72 hours.");
    }
}

public sealed class ApprovalLimitsRequestValidator : AbstractValidator<ApprovalLimitsRequest>
{
    public ApprovalLimitsRequestValidator() => Include(new ApprovalLimitsValidator<ApprovalLimitsRequest>());
}

public sealed class CreateApprovalRequestValidator : AbstractValidator<CreateApprovalRequest>
{
    public CreateApprovalRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Choose the product.");
        RuleFor(x => x.CropId).NotEmpty().WithMessage("Choose the crop.");
        Include(new ApprovalLimitsValidator<CreateApprovalRequest>());
    }
}

public sealed class CreateBatchRequestValidator : AbstractValidator<CreateBatchRequest>
{
    public CreateBatchRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Choose the product.");
        RuleFor(x => x.BatchNo).NotEmpty().WithMessage("Enter the batch number printed on the pack.").MaximumLength(50);
        RuleFor(x => x.QuantityOnHand).GreaterThan(0).WithMessage("Enter how much arrived.").LessThanOrEqualTo(100_000);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).WithMessage("Price cannot be negative.").LessThanOrEqualTo(10_000_000);
    }
}

public sealed class UpdateBatchRequestValidator : AbstractValidator<UpdateBatchRequest>
{
    public UpdateBatchRequestValidator()
    {
        // Zero is a valid count: the batch sold out or was written off.
        RuleFor(x => x.QuantityOnHand).GreaterThanOrEqualTo(0).WithMessage("Stock cannot be negative.").LessThanOrEqualTo(100_000);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).WithMessage("Price cannot be negative.").LessThanOrEqualTo(10_000_000);
    }
}

public sealed class CreateReservationRequestValidator : AbstractValidator<CreateReservationRequest>
{
    public CreateReservationRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Choose the product.");
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Enter how much to hold.").LessThanOrEqualTo(100_000);
        RuleFor(x => x.Note).MaximumLength(ReservationLimits.MaxNoteLength);
    }
}

public sealed class FulfilOrderRequestValidator : AbstractValidator<FulfilOrderRequest>
{
    public FulfilOrderRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum()
            .Must(s => s is OrderStatus.Packed or OrderStatus.Collected)
            .WithMessage("An order is fulfilled by marking it Packed, then Collected.");
    }
}
