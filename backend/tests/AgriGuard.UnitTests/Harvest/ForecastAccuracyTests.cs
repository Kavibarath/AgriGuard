using AgriGuard.Domain.Harvest;

namespace AgriGuard.UnitTests.Harvest;

public sealed class ForecastAccuracyTests
{
    [Fact]
    public void Over_and_under_estimates_cancel_in_the_variance_but_not_in_the_error()
    {
        // One 10% short, one 10% over.
        var summary = ForecastAccuracy.Summarise([(1000m, 900m), (1000m, 1100m)]);

        Assert.Equal(0m, summary.VariancePercent);
        Assert.Equal(10m, summary.MeanAbsolutePercentError);
        Assert.Equal(2, summary.WithinTolerance);
    }

    [Fact]
    public void A_harvest_that_fell_short_has_a_negative_variance()
    {
        var summary = ForecastAccuracy.Summarise([(2400m, 2150m)]);

        Assert.Equal(-10.4m, summary.VariancePercent);
        Assert.Equal(10.4m, summary.MeanAbsolutePercentError);
        // Just outside ±10%.
        Assert.Equal(0, summary.WithinTolerance);
        Assert.Equal((2400m, 2150m), (summary.ForecastKg, summary.ActualKg));
    }

    [Fact]
    public void Nothing_to_compare_gives_no_percentages_rather_than_a_division_by_zero()
    {
        var summary = ForecastAccuracy.Summarise([(0m, 500m)]);

        Assert.Equal(0, summary.Forecasts);
        Assert.Null(summary.VariancePercent);
        Assert.Null(summary.MeanAbsolutePercentError);
    }
}
