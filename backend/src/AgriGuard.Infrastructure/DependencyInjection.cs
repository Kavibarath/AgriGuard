using AgriGuard.Application.Auth;
using AgriGuard.Application.Registry;
using AgriGuard.Infrastructure.Identity;
using AgriGuard.Infrastructure.Persistence;
using AgriGuard.Infrastructure.Persistence.Seed;
using AgriGuard.Infrastructure.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

        return services;
    }

    private static async Task SeedAsync(AgriGuardDbContext db, bool seedDemoUsers, CancellationToken ct = default)
    {
        await ReferenceDataSeeder.SeedAsync(db, ct);

        if (seedDemoUsers)
            await DemoUserSeeder.SeedAsync(db, new Pbkdf2PasswordHasher(), ct);
    }
}
