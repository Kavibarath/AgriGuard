using AgriGuard.Domain.Harvest;

namespace AgriGuard.Application.Weather;

/// <summary>A forecast for one ~11 km grid cell, hourly, in the farm's local time.</summary>
public sealed record WeatherForecast(decimal LatitudeRounded, decimal LongitudeRounded, DateTime FetchedAt, IReadOnlyList<HourlyWeather> Hours);

/// <summary>Fetches a forecast from the provider (Open-Meteo). Throws when it cannot; callers degrade.</summary>
public interface IWeatherProvider
{
    Task<IReadOnlyList<HourlyWeather>> FetchAsync(decimal latitude, decimal longitude, CancellationToken ct = default);
}

/// <summary>
/// Forecasts through a 3-hour cache. Returns null when no forecast can be had (provider down,
/// circuit open): weather is advisory input, so its absence degrades a check rather than failing it.
/// </summary>
public interface IWeatherService
{
    Task<WeatherForecast?> ForecastAsync(decimal latitude, decimal longitude, CancellationToken ct = default);
}

public sealed record SprayDayDto(
    DateOnly Date,
    bool Suitable,
    int RainProbabilityPercent,
    decimal WindSpeedKph,
    decimal TemperatureC,
    decimal PrecipitationMm,
    IReadOnlyList<string> Problems);

/// <summary>Which of the coming days suit spraying on a plot, judged exactly as rule V8 judges them.</summary>
public sealed record SprayWindowDto(
    Guid PlotId,
    string PlotCode,
    // False when the forecast could not be fetched; Days is then empty and nothing is guessed.
    bool ForecastAvailable,
    string Source,
    DateTime? FetchedAt,
    // The product's rainfast hours when one was named, otherwise a typical 4 hours.
    int RainfastHours,
    string? ProductName,
    // "Suitable on 29 Sep, 30 Sep; rain likely on 1 Oct": one line for a person or the agent.
    string Summary,
    // The last 48 hours at the plot: wet, humid weather favours fungal disease (the Diagnosis agent reads this).
    decimal? RecentRainMm,
    int? RecentHumidityPercent,
    IReadOnlyList<SprayDayDto> Days,
    SprayThresholdsDto Thresholds);

public sealed record SprayThresholdsDto(int MaxRainProbabilityPercent, decimal MaxWindSpeedKph, decimal MaxTemperatureC);

public interface ISprayWindowService
{
    /// <summary>For a signed-in user: the plot must be one they may see (their own, or their district's).</summary>
    Task<SprayWindowDto> ForPlotAsync(Guid plotId, int days, Guid? productId, CancellationToken ct = default);

    /// <summary>For the agent's tool: not user-scoped, the run already belongs to this plot's case.</summary>
    Task<SprayWindowDto> ComputeAsync(Guid plotId, int days, Guid? productId, CancellationToken ct = default);
}
