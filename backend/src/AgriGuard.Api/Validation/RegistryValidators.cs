using System.Linq.Expressions;
using AgriGuard.Application.Registry;
using FluentValidation;

namespace AgriGuard.Api.Validation;

// Shape and range checks only. Rules needing the database (does this district exist, is the plot
// code taken, is this stage transition legal) belong to the services, where the data is.

public sealed class CreateFarmRequestValidator : AbstractValidator<CreateFarmRequest>
{
    public CreateFarmRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Give the farm a name.").MaximumLength(150);
        RuleFor(x => x.Village).MaximumLength(150);
        RuleFor(x => x.DistrictId).NotEmpty().WithMessage("Choose a district.");
    }
}

public sealed class UpdateFarmRequestValidator : AbstractValidator<UpdateFarmRequest>
{
    public UpdateFarmRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Give the farm a name.").MaximumLength(150);
        RuleFor(x => x.Village).MaximumLength(150);
        RuleFor(x => x.DistrictId).NotEmpty().WithMessage("Choose a district.");
    }
}

public sealed class CreatePlotRequestValidator : AbstractValidator<CreatePlotRequest>
{
    public CreatePlotRequestValidator()
    {
        RuleFor(x => x.FarmId).NotEmpty();
        RuleFor(x => x.PlotCode).NotEmpty().WithMessage("Give the plot a short code, e.g. P-07.").MaximumLength(20);
        RuleFor(x => x.Name).MaximumLength(100);
        Include(new PlotGeometryValidator<CreatePlotRequest>(x => x.AreaHectares, x => x.Latitude, x => x.Longitude));
    }
}

public sealed class UpdatePlotRequestValidator : AbstractValidator<UpdatePlotRequest>
{
    public UpdatePlotRequestValidator()
    {
        RuleFor(x => x.PlotCode).NotEmpty().WithMessage("Give the plot a short code, e.g. P-07.").MaximumLength(20);
        RuleFor(x => x.Name).MaximumLength(100);
        RuleFor(x => x.Status).IsInEnum();
        Include(new PlotGeometryValidator<UpdatePlotRequest>(x => x.AreaHectares, x => x.Latitude, x => x.Longitude));
    }
}

/// <summary>
/// Shared area/coordinate rules, so create and update cannot drift apart. The bounds match the
/// CHECK constraints on the plots table — the database is the backstop, this is the message.
/// </summary>
public sealed class PlotGeometryValidator<T> : AbstractValidator<T>
{
    // Expressions, not Funcs: FluentValidation reads the member name out of the expression tree.
    public PlotGeometryValidator(
        Expression<Func<T, decimal>> area,
        Expression<Func<T, decimal>> latitude,
        Expression<Func<T, decimal>> longitude)
    {
        RuleFor(area).GreaterThan(0).WithName("areaHectares")
            .WithMessage("Area must be greater than 0 hectares.")
            .LessThanOrEqualTo(10_000).WithMessage("That area looks wrong — enter hectares, not square metres.");

        RuleFor(latitude).InclusiveBetween(-90, 90).WithName("latitude")
            .WithMessage("Latitude must be between -90 and 90.");

        RuleFor(longitude).InclusiveBetween(-180, 180).WithName("longitude")
            .WithMessage("Longitude must be between -180 and 180.");
    }
}

public sealed class CreateCropCycleRequestValidator : AbstractValidator<CreateCropCycleRequest>
{
    public CreateCropCycleRequestValidator()
    {
        RuleFor(x => x.PlotId).NotEmpty();
        RuleFor(x => x.CropId).NotEmpty().WithMessage("Choose the crop being sown.");
        RuleFor(x => x.SownDate).NotEqual(default(DateOnly)).WithMessage("Enter the sowing date.");
    }
}

public sealed class AdvanceStageRequestValidator : AbstractValidator<AdvanceStageRequest>
{
    public AdvanceStageRequestValidator()
    {
        RuleFor(x => x.ToStage).IsInEnum().WithMessage("Choose the stage the crop has reached.");
        RuleFor(x => x.Note).MaximumLength(500);
    }
}
