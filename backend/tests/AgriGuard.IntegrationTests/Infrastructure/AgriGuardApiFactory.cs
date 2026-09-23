using AgriGuard.Application.Agent;
using AgriGuard.Application.Auth;
using AgriGuard.Domain.Identity;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace AgriGuard.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API against a throwaway PostgreSQL container with the real migrations and seed.
/// One container is shared by every test in the "api" collection.
/// </summary>
public sealed class AgriGuardApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Test-only signing key (32 bytes). Tests forge tokens with a different key to prove they are rejected.</summary>
    public const string SigningKey = "dGVzdC1vbmx5LWtleS1kby1ub3QtdXNlLWluLXByb2Q=";

    public const string TestPassword = "Test!Password1";

    /// <summary>Test-only agent key (the real one is a user-secret / environment variable).</summary>
    public const string AgentKey = "test-only-agent-key-0123456789abcdef";

    /// <summary>Replaces the HTTP dispatcher: tests never need the Python service running.</summary>
    public FakeAgentDispatcher Dispatcher { get; } = new();

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
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("AgentService:ApiKey", AgentKey);

        // Every test shares one loopback address, so the real 10-per-minute auth bucket would
        // throttle unrelated tests. The rate-limit test lowers this again for itself.
        builder.UseSetting("RateLimiting:Auth:PermitLimit", "10000");

        // Test-only controllers (FaultsController, SecureController) live in this assembly.
        builder.ConfigureTestServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(AgriGuardApiFactory).Assembly);
            services.RemoveAll<IAgentDispatcher>();
            services.AddSingleton<IAgentDispatcher>(Dispatcher);
        });
    }

    /// <summary>Creates an active user with a known password. Emails are unique per call so tests stay independent.</summary>
    public async Task<User> CreateUserAsync(
        UserRole role,
        bool isActive = true,
        bool withDistrict = true,
        string password = TestPassword,
        Guid? districtId = null)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgriGuardDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = new User
        {
            Email = $"{role}.{Guid.NewGuid():N}@test.local".ToLowerInvariant(),
            FullName = $"Test {role}",
            Role = role,
            IsActive = isActive,
            DistrictId = withDistrict ? districtId ?? await db.Districts.Select(d => d.Id).FirstAsync() : null,
            PasswordHash = hasher.Hash(password)
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public async Task<T> QueryAsync<T>(Func<AgriGuardDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AgriGuardDbContext>());
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
