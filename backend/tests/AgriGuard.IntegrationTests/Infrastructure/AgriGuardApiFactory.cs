using AgriGuard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace AgriGuard.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API against a throwaway PostgreSQL container with the real migrations and seed.
/// One container is shared by every test in the "api" collection.
/// </summary>
public sealed class AgriGuardApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Same major version as docker-compose.yml, so tests exercise what we run locally.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AgriGuardDbContext>().Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing", not "Development": user-secrets only load in Development, and they must never
        // redirect the tests at the developer's real database.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());

        // Test-only controllers (e.g. FaultsController) live in this assembly.
        builder.ConfigureTestServices(services =>
            services.AddControllers().AddApplicationPart(typeof(AgriGuardApiFactory).Assembly));
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<AgriGuardApiFactory>
{
    public const string Name = "api";
}
