using Microsoft.EntityFrameworkCore;

namespace AgriGuard.IntegrationTests.Infrastructure;

public static class WeatherFixtures
{
    /// <summary>
    /// Runs <paramref name="test"/> with the fake forecast changed by <paramref name="change"/>,
    /// then puts the calm default back. The cache is cleared on both sides, so neither this test
    /// nor the next one reads a forecast cached under different weather.
    /// </summary>
    public static async Task ChangeWeatherAsync(this AgriGuardApiFactory factory, Action<FakeWeatherProvider> change, Func<Task> test)
    {
        await factory.ClearWeatherCacheAsync();
        change(factory.Weather);
        try
        {
            await test();
        }
        finally
        {
            factory.Weather.Reset();
            await factory.ClearWeatherCacheAsync();
        }
    }

    public static Task<int> ClearWeatherCacheAsync(this AgriGuardApiFactory factory) =>
        factory.QueryAsync(db => db.WeatherSnapshots.ExecuteDeleteAsync());
}
