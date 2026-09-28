using AgriGuard.Domain.Validation;

namespace AgriGuard.Domain.Harvest;

/// <summary>One hour of forecast at a plot, in the farm's local time.</summary>
public sealed record HourlyWeather(
    DateTime LocalTime,
    int RainProbabilityPercent,
    decimal WindSpeedKph,
    decimal TemperatureC,
    decimal PrecipitationMm,
    int RelativeHumidityPercent);

/// <summary>Whether a day suits spraying, with the figures it was judged on.</summary>
public sealed record SprayDayAssessment(
    DateOnly Date,
    int RainProbabilityPercent,
    decimal WindSpeedKph,
    decimal TemperatureC,
    decimal PrecipitationMm,
    IReadOnlyList<string> Problems)
{
    public bool Suitable => Problems.Count == 0;

    /// <summary>The same figures as rule V8's input, so the rule and this assessment always agree.</summary>
    public WeatherAssessment ToRuleInput() => new(RainProbabilityPercent, WindSpeedKph, TemperatureC, PrecipitationMm);
}

/// <summary>
/// Turns an hourly forecast into a spray verdict for a day (rule V8, the spray-window screen and the
/// agent's weather tool all use this).
///
/// Spraying is assumed to happen in the morning application window, 06:00–10:00, as extension
/// advice recommends: calm air, no midday heat. Wind and temperature are judged over that window.
/// Rain is judged from the start of spraying until the product is rainfast — the application
/// window plus the product's rainfast hours — because rain before then washes the product off.
/// Every figure is the worst hour in its window, except rain, which is also totalled: a likely
/// shower counts only if it brings at least 0.5 mm (V8's MinWashOffRainMm). The thresholds are V8's own.
///
/// Pure: no clock, no network. Returns null when the forecast does not cover the application
/// window (e.g. beyond the 16-day horizon), so V8 reports "not evaluated" instead of guessing.
/// </summary>
public static class SprayWeather
{
    public static readonly TimeOnly ApplicationStart = new(6, 0);
    public static readonly TimeOnly ApplicationEnd = new(10, 0);

    public static SprayDayAssessment? Assess(IReadOnlyList<HourlyWeather> hours, DateOnly date, int rainfastHours)
    {
        var start = date.ToDateTime(ApplicationStart);
        var applicationEnd = date.ToDateTime(ApplicationEnd);
        var rainfastEnd = applicationEnd.AddHours(Math.Max(0, rainfastHours));

        var application = hours.Where(h => h.LocalTime >= start && h.LocalTime < applicationEnd).ToList();
        var expectedHours = (int)(applicationEnd - start).TotalHours;
        if (application.Count < expectedHours)
            return null;

        var untilRainfast = hours.Where(h => h.LocalTime >= start && h.LocalTime < rainfastEnd).ToList();

        var rain = untilRainfast.Max(h => h.RainProbabilityPercent);
        var wind = application.Max(h => h.WindSpeedKph);
        var temperature = application.Max(h => h.TemperatureC);
        var precipitation = untilRainfast.Sum(h => h.PrecipitationMm);

        List<string> problems = [];
        // Likely AND enough to wash the product off, exactly as V8 judges it.
        if (rain >= PrescriptionSafetyValidator.MaxRainProbabilityPercent && precipitation >= PrescriptionSafetyValidator.MinWashOffRainMm)
            problems.Add($"{rain}% chance of {precipitation:0.#} mm of rain before the spray is rainfast");
        if (wind >= PrescriptionSafetyValidator.MaxWindSpeedKph)
            problems.Add($"wind up to {wind:0.#} km/h would cause drift");
        if (temperature > PrescriptionSafetyValidator.MaxTemperatureC)
            problems.Add($"{temperature:0.#} °C is too hot to spray");

        return new SprayDayAssessment(date, rain, wind, temperature, precipitation, problems);
    }
}
