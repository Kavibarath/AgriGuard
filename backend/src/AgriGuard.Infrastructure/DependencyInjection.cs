using AgriGuard.Infrastructure.Persistence;
using AgriGuard.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriGuard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddDbContext<AgriGuardDbContext>(options => options
            // No EnableRetryOnFailure: a retrying execution strategy forbids plain BeginTransaction(),
            // and the reservation/approval flows use explicit serializable transactions with their own retry.
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(AgriGuardDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            // Runs after Migrate()/MigrateAsync(); both variants are required by EF Core.
            .UseSeeding((context, _) =>
                ReferenceDataSeeder.SeedAsync((AgriGuardDbContext)context).GetAwaiter().GetResult())
            .UseAsyncSeeding((context, _, ct) =>
                ReferenceDataSeeder.SeedAsync((AgriGuardDbContext)context, ct)));

        return services;
    }
}
