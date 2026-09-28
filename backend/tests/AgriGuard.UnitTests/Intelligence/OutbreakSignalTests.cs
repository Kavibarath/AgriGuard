using AgriGuard.Domain.Cases;
using AgriGuard.Domain.Intelligence;

namespace AgriGuard.UnitTests.Intelligence;

public sealed class OutbreakSignalTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);
    private static readonly Guid Nuwara = Guid.NewGuid();
    private static readonly Guid Anuradhapura = Guid.NewGuid();

    private static ObservedCase Blight(int daysAgo, CaseSeverity severity = CaseSeverity.Medium, Guid? district = null) =>
        new(district ?? Nuwara, Today.AddDays(-daysAgo), severity, 6.95m, 80.79m, "LATE_BLIGHT", "Late blight");

    private static ObservedCase Mildew(int daysAgo, CaseSeverity severity = CaseSeverity.Medium, Guid? district = null) =>
        new(district ?? Nuwara, Today.AddDays(-daysAgo), severity, 6.95m, 80.79m, "POWDERY_MILDEW", "Powdery mildew");

    private static ObservedCase Unconfirmed(int daysAgo) =>
        new(Nuwara, Today.AddDays(-daysAgo), CaseSeverity.Critical, 6.95m, 80.79m);

    [Fact]
    public void No_cases_is_no_pressure()
    {
        var result = OutbreakSignal.Assess([], Today, 14);

        Assert.Equal(0, result.Index);
        Assert.Equal(PressureLevel.None, result.Level);
        Assert.Equal(PressureTrend.Steady, result.Trend);
        Assert.Empty(result.Pathogens);
    }

    [Fact]
    public void A_case_counts_half_after_a_week_and_a_quarter_after_two()
    {
        Assert.Equal(1m, OutbreakSignal.Weight(Blight(0), Today));
        Assert.Equal(0.5m, OutbreakSignal.Weight(Blight(7), Today));
        Assert.Equal(0.25m, OutbreakSignal.Weight(Blight(14), Today));
    }

    [Fact]
    public void Severity_scales_the_weight()
    {
        Assert.Equal(0.5m, OutbreakSignal.Weight(Blight(0, CaseSeverity.Low), Today));
        Assert.Equal(2m, OutbreakSignal.Weight(Blight(0, CaseSeverity.Critical), Today));
    }

    [Theory]
    [InlineData(0, 0, PressureLevel.None)]
    [InlineData(1, 22, PressureLevel.Low)]
    [InlineData(2, 39, PressureLevel.Moderate)]
    [InlineData(4.5, 68, PressureLevel.High)]
    [InlineData(8, 86, PressureLevel.Severe)]
    public void The_index_saturates_and_is_banded_into_levels(double score, int index, PressureLevel level)
    {
        Assert.Equal(index, OutbreakSignal.ToIndex((decimal)score));
        Assert.Equal(level, OutbreakSignal.LevelFor(index));
    }

    [Fact]
    public void Unconfirmed_reports_are_counted_but_never_scored()
    {
        // Ten critical suspicions and no confirmation: something to look at, not an outbreak.
        var result = OutbreakSignal.Assess(Enumerable.Range(0, 10).Select(_ => Unconfirmed(1)), Today, 14);

        Assert.Equal(10, result.ReportedCases);
        Assert.Equal(0, result.ConfirmedCases);
        Assert.Equal(PressureLevel.None, result.Level);
    }

    [Fact]
    public void Cases_outside_the_window_are_ignored()
    {
        var result = OutbreakSignal.Assess([Blight(14), Blight(13), Blight(-1)], Today, 14);

        // The window is the 14 days ending today: 13 days ago is in, 14 days ago and tomorrow are not.
        Assert.Equal(1, result.ConfirmedCases);
        Assert.Equal(Today.AddDays(-13), result.From);
    }

    [Fact]
    public void Pathogens_are_ranked_by_weighted_score_not_raw_count()
    {
        // Three old mild mildew cases against two fresh severe blight cases.
        var result = OutbreakSignal.Assess(
            [Mildew(12, CaseSeverity.Low), Mildew(12, CaseSeverity.Low), Mildew(11, CaseSeverity.Low), Blight(0, CaseSeverity.High), Blight(1, CaseSeverity.High)],
            Today, 14);

        Assert.Equal(["LATE_BLIGHT", "POWDERY_MILDEW"], result.Pathogens.Select(p => p.Code));
        Assert.Equal(3, result.Pathogens[1].ConfirmedCases);
        Assert.Equal(100, result.Pathogens.Sum(p => p.SharePercent));
        Assert.Equal(Today, result.Pathogens[0].LastReportedOn);
    }

    [Fact]
    public void More_recent_confirmations_mean_a_rising_trend()
    {
        var rising = OutbreakSignal.Assess([Blight(10), Blight(2), Blight(1), Blight(0)], Today, 14);
        var falling = OutbreakSignal.Assess([Blight(12), Blight(11), Blight(9), Blight(2)], Today, 14);
        var steady = OutbreakSignal.Assess([Blight(10), Blight(9), Blight(2), Blight(1)], Today, 14);

        Assert.Equal(PressureTrend.Rising, rising.Trend);
        Assert.Equal(PressureTrend.Falling, falling.Trend);
        Assert.Equal(PressureTrend.Steady, steady.Trend);
    }

    [Fact]
    public void One_new_case_after_a_quiet_week_is_not_yet_a_trend() =>
        Assert.Equal(PressureTrend.Steady, OutbreakSignal.TrendOf(olderHalf: 0, newerHalf: 1));

    [Fact]
    public void Every_day_of_the_window_has_a_row_even_when_empty()
    {
        var result = OutbreakSignal.Assess([Blight(0), Unconfirmed(0), Blight(3)], Today, 7);

        Assert.Equal(7, result.Daily.Count);
        Assert.Equal(Today.AddDays(-6), result.Daily[0].Date);
        Assert.Equal(new DailyCaseCount(Today, 2, 1), result.Daily[^1]);
        Assert.Equal(0, result.Daily[0].ReportedCases);
    }

    [Fact]
    public void Districts_are_scored_separately_and_placed_only_roughly()
    {
        var anu = new ObservedCase(Anuradhapura, Today, CaseSeverity.High, 8.3514m, 80.5041m, "LATE_BLIGHT", "Late blight");
        var suspected = new ObservedCase(Anuradhapura, Today, CaseSeverity.Low, 8.3710m, 80.4960m);
        var result = OutbreakSignal.Assess([Blight(0), Blight(1), Mildew(0), anu, suspected], Today, 14);

        Assert.Equal([Nuwara, Anuradhapura], result.Districts.Select(d => d.DistrictId));
        var anuradhapura = result.Districts[1];
        Assert.Equal((2, 1), (anuradhapura.ReportedCases, anuradhapura.ConfirmedCases));
        Assert.Equal("LATE_BLIGHT", anuradhapura.TopPathogenCode);
        // Two reports averaged, then rounded to a tenth of a degree.
        Assert.Equal((8.4m, 80.5m), (anuradhapura.Latitude, anuradhapura.Longitude));
    }

    [Fact]
    public void Days_are_clamped_to_a_sensible_window()
    {
        Assert.Equal(1, OutbreakSignal.Assess([], Today, 0).WindowDays);
        Assert.Equal(OutbreakSignal.MaxDays, OutbreakSignal.Assess([], Today, 400).WindowDays);
    }
}
