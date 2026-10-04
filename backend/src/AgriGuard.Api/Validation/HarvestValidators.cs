using AgriGuard.Application.Harvest;
using FluentValidation;

namespace AgriGuard.Api.Validation;

// Shape and range checks only. Whether the crop is safe to harvest, a slot has room, or a centre
// can take another slot is decided in the services, where the data is.

public sealed class CreateHarvestForecastRequestValidator : AbstractValidator<CreateHarvestForecastRequest>
{
    public CreateHarvestForecastRequestValidator()
    {
        RuleFor(x => x.CropCycleId).NotEmpty().WithMessage("Choose the crop.");
        RuleFor(x => x.EstimatedYieldKg).GreaterThan(0).WithMessage("Enter the expected yield in kg.").LessThanOrEqualTo(1_000_000);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class RecordActualYieldRequestValidator : AbstractValidator<RecordActualYieldRequest>
{
    public RecordActualYieldRequestValidator() =>
        RuleFor(x => x.ActualYieldKg).GreaterThanOrEqualTo(0).WithMessage("The actual yield cannot be negative.").LessThanOrEqualTo(1_000_000);
}

public sealed class CreateCollectionSlotRequestValidator : AbstractValidator<CreateCollectionSlotRequest>
{
    public CreateCollectionSlotRequestValidator()
    {
        RuleFor(x => x.CentreId).NotEmpty().WithMessage("Choose the centre.");
        RuleFor(x => x.SlotIndex).InclusiveBetween(1, 24);
        RuleFor(x => x.EndTime).GreaterThan(x => x.StartTime).WithMessage("The slot must end after it starts.");
        RuleFor(x => x.CapacityKg).GreaterThan(0).WithMessage("Enter the slot's capacity in kg.").LessThanOrEqualTo(100_000);
    }
}

public sealed class AllocateBookingRequestValidator : AbstractValidator<AllocateBookingRequest>
{
    public AllocateBookingRequestValidator()
    {
        RuleFor(x => x.CropCycleId).NotEmpty().WithMessage("Choose the crop to deliver.");
        RuleFor(x => x.QuantityKg).GreaterThan(0).WithMessage("Enter how many kg you will deliver.").LessThanOrEqualTo(20_000);
    }
}
