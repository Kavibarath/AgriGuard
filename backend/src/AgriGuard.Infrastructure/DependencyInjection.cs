using AgriGuard.Application.Agent;
using AgriGuard.Application.Auth;
using AgriGuard.Application.Cases;
using AgriGuard.Application.Common;
using AgriGuard.Application.Inventory;
using AgriGuard.Application.Registry;
using AgriGuard.Application.Validation;
using AgriGuard.Application.Weather;
using AgriGuard.Infrastructure.Agent;
using AgriGuard.Infrastructure.Cases;
using AgriGuard.Infrastructure.Identity;
using AgriGuard.Infrastructure.Inventory;
using AgriGuard.Infrastructure.Persistence;
using AgriGuard.Infrastructure.Persistence.Seed;
using AgriGuard.Infrastructure.Registry;
using AgriGuard.Infrastructure.Validation;
using AgriGuard.Infrastructure.Weather;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace AgriGuard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Connection string comes from user-secrets locally and environment variables in the cloud —
        // never from a committed appsettings file.
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' is not configured. Locally run: " +
                "dotnet user-secrets set \"ConnectionStrings:Default\" \"<connection string>\" --project backend/src/AgriGuard.Api");

        // Demo accounts are seeded only where they are wanted (local demo, viva database),
        // so a real deployment never gets known-password logins by accident.
        var seedDemoUsers = configuration.GetValue("Seed:DemoUsers", false);

        services.AddSingleton(TimeProvider.System);

        // Calendar dates (spray, harvest, expiry) are the farms' local dates, not UTC ones.
        var zone = TimeZoneInfo.FindSystemTimeZoneById(configuration.GetValue("Calendar:TimeZone", FarmCalendar.DefaultTimeZone)!);
        services.AddSingleton(sp => new FarmCalendar(sp.GetRequiredService<TimeProvider>(), zone));

        services.AddDbContext<AgriGuardDbContext>(options => options
            // No EnableRetryOnFailure: a retrying execution strategy forbids plain BeginTransaction(),
            // and the reservation/approval flows use explicit serializable transactions with their own retry.
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(AgriGuardDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            // Runs after Migrate()/MigrateAsync(); both variants are required by EF Core.
            .UseSeeding((context, _) =>
                SeedAsync((AgriGuardDbContext)context, seedDemoUsers).GetAwaiter().GetResult())
            .UseAsyncSeeding((context, _, ct) =>
                SeedAsync((AgriGuardDbContext)context, seedDemoUsers, ct)));

        services.AddHealthChecks().AddDbContextCheck<AgriGuardDbContext>("database");

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            // Validated at startup, not on first login: a missing signing key should stop
            // deployment, not surface as a 500 during the demo.
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();

        // Component A — registry
        services.AddScoped<IFarmService, FarmService>();
        services.AddScoped<IPlotService, PlotService>();
        services.AddScoped<ICropCycleService, CropCycleService>();
        // One instance per request serves both the user endpoint and the agent's tool.
        services.AddScoped<SafetyProfileService>();
        services.AddScoped<ISafetyProfileService>(sp => sp.GetRequiredService<SafetyProfileService>());

        // Component A — the deterministic safety gate every agent proposal passes through
        services.AddScoped<IPrescriptionValidationService, PrescriptionValidationService>();

        // Component B — cases and the agent workflow
        services.AddScoped<ICaseService, CaseService>();
        services.AddScoped<ICasePhotoService, CasePhotoService>();
        services.AddScoped<IAgentRunService, AgentRunService>();
        services.AddScoped<IApprovalService, ApprovalService>();
        services.AddScoped<IAgentCallbackService, AgentCallbackService>();
        services.AddScoped<IAgentToolService, AgentToolService>();
        services.AddScoped<AgentPrescriptionGate>();

        services.AddOptions<AgentServiceOptions>()
            .Bind(configuration.GetSection(AgentServiceOptions.SectionName))
            // Same reasoning as the JWT key: a missing agent key should stop deployment, not
            // surface as every agent callback failing during the demo.
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AgentServiceOptions>, AgentServiceOptionsValidator>();

        services.AddHttpClient<IAgentDispatcher, HttpAgentDispatcher>((sp, http) =>
        {
            var agent = sp.GetRequiredService<IOptions<AgentServiceOptions>>().Value;
            // A trailing slash, so a base URL with a path (https://host/agent) keeps it when "runs" is appended.
            http.BaseAddress = new Uri(agent.BaseUrl.ToString().TrimEnd('/') + "/");
            http.Timeout = agent.DispatchTimeout;
        });

        services.AddHostedService<AgentRunTimeoutSweeper>();

        // Component C — catalogue, the rules table, dealer stock, holds and orders
        services.AddScoped<StockLedger>();
        services.AddScoped<ProposalStockHolds>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IProductCropApprovalService, ProductCropApprovalService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<IOrderService, OrderService>();

        // Component D — Open-Meteo weather (§10), behind a cache and a resilience pipeline
        services.AddOptions<OpenMeteoOptions>().Bind(configuration.GetSection(OpenMeteoOptions.SectionName));
        services.AddHttpClient<IWeatherProvider, OpenMeteoClient>((sp, http) =>
            {
                http.BaseAddress = sp.GetRequiredService<IOptions<OpenMeteoOptions>>().Value.BaseUrl;
                http.DefaultRequestHeaders.UserAgent.ParseAdd("AgriGuard/1.0 (SLIIT SE3090 academic project)");
            })
            .AddResilienceHandler("open-meteo", pipeline =>
            {
                // Outermost first: two retries with jittered backoff; then a breaker that stops
                // calling for a minute after 5 failures in a row (every sampled call failed); then
                // a 5-second limit on each single attempt.
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 2,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(300)
                });
                pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 1.0,
                    MinimumThroughput = 5,
                    SamplingDuration = TimeSpan.FromMinutes(1),
                    BreakDuration = TimeSpan.FromMinutes(1)
                });
                pipeline.AddTimeout(TimeSpan.FromSeconds(5));
            });
        services.AddScoped<IWeatherService, WeatherService>();
        services.AddScoped<ISprayWindowService, SprayWindowService>();

        services.AddOptions<InventoryOptions>().Bind(configuration.GetSection(InventoryOptions.SectionName));
        // Registered as itself too, so tests can run one sweep on demand.
        services.AddSingleton<ReservationExpirySweeper>();
        services.AddHostedService(sp => sp.GetRequiredService<ReservationExpirySweeper>());

        return services;
    }

    private static async Task SeedAsync(AgriGuardDbContext db, bool seedDemoUsers, CancellationToken ct = default)
    {
        await ReferenceDataSeeder.SeedAsync(db, ct);

        if (seedDemoUsers)
        {
            await DemoUserSeeder.SeedAsync(db, new Pbkdf2PasswordHasher(), ct);
            await DemoInventorySeeder.SeedAsync(db, TimeProvider.System, ct);
            await DemoDryZoneSeeder.SeedAsync(db, new Pbkdf2PasswordHasher(), TimeProvider.System, ct);
        }
    }
}
