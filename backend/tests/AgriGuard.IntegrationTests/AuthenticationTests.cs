using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using AgriGuard.Domain.Identity;
using AgriGuard.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace AgriGuard.IntegrationTests;

/// <summary>
/// The specification for the login flow. Tests that need a working access token fail until
/// JwtTokenService.CreateAccessToken and the JWT validation parameters are implemented.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuthenticationTests(AgriGuardApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Login_returns_tokens_and_the_user_summary()
    {
        var user = await factory.CreateUserAsync(UserRole.FieldAgronomist);

        var response = await Login(user.Email, AgriGuardApiFactory.TestPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("refreshToken").GetString()));
        Assert.Equal(user.Email, body.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal("FieldAgronomist", body.GetProperty("user").GetProperty("role").GetString());
    }

    [Fact]
    public async Task Login_is_case_insensitive_on_email()
    {
        var user = await factory.CreateUserAsync(UserRole.Farmer);

        var response = await Login(user.Email.ToUpperInvariant(), AgriGuardApiFactory.TestPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_records_the_time_of_the_last_successful_login()
    {
        var user = await factory.CreateUserAsync(UserRole.Farmer);

        await Login(user.Email, AgriGuardApiFactory.TestPassword);

        var lastLoginAt = await factory.QueryAsync(db =>
            db.Users.Where(u => u.Id == user.Id).Select(u => u.LastLoginAt).FirstAsync());
        Assert.NotNull(lastLoginAt);
    }

    [Fact]
    public async Task Login_with_a_wrong_password_is_401()
    {
        var user = await factory.CreateUserAsync(UserRole.Farmer);

        var response = await Login(user.Email, "WrongPassword!1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_an_unknown_email_is_401_and_says_nothing_useful()
    {
        var response = await Login($"nobody-{Guid.NewGuid():N}@test.local", "WrongPassword!1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Must not distinguish "no such account" from "wrong password" — that is account enumeration.
        Assert.Equal("Invalid credentials.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Login_on_a_disabled_account_is_401()
    {
        var user = await factory.CreateUserAsync(UserRole.AgroDealer, isActive: false);

        var response = await Login(user.Email, AgriGuardApiFactory.TestPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_rejects_a_malformed_request_with_field_errors()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = "not-an-email", password = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task Refresh_returns_a_new_pair_and_kills_the_old_refresh_token()
    {
        var tokens = await LoginAsync(UserRole.Farmer);

        var refreshed = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var body = await refreshed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(tokens.RefreshToken, body.GetProperty("refreshToken").GetString());

        // The old one is spent.
        var reused = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
    }

    [Fact]
    public async Task Replaying_a_rotated_refresh_token_revokes_every_session()
    {
        var tokens = await LoginAsync(UserRole.Farmer);
        var rotated = await Refresh(tokens.RefreshToken);

        // Replay of the spent token: treat as theft and log the account out everywhere.
        var replay = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        var afterReplay = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = rotated.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterReplay.StatusCode);

        var active = await factory.QueryAsync(db =>
            db.RefreshTokens.CountAsync(t => t.UserId == tokens.UserId && t.RevokedAt == null));
        Assert.Equal(0, active);
    }

    [Fact]
    public async Task Refresh_with_an_unknown_token_is_401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_and_is_idempotent()
    {
        var tokens = await LoginAsync(UserRole.Farmer);

        var first = await _client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = tokens.RefreshToken });
        var second = await _client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        var refresh = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Refresh_tokens_are_never_stored_in_plain_text()
    {
        var tokens = await LoginAsync(UserRole.Farmer);

        var storedHashes = await factory.QueryAsync(db =>
            db.RefreshTokens.Where(t => t.UserId == tokens.UserId).Select(t => t.TokenHash).ToListAsync());

        Assert.NotEmpty(storedHashes);
        Assert.DoesNotContain(tokens.RefreshToken, storedHashes);
    }

    [Fact]
    public async Task Protected_endpoint_without_a_token_is_401()
    {
        var response = await _client.GetAsync("/_test/secure/authenticated");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_valid_token_reaches_a_protected_endpoint()
    {
        var tokens = await LoginAsync(UserRole.Farmer);

        var response = await GetWithToken("/_test/secure/authenticated", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_rejected()
    {
        var user = await factory.CreateUserAsync(UserRole.FieldAgronomist);
        var forged = ForgeToken(user, Convert.ToBase64String(new byte[32]), "AgriGuard");

        var response = await GetWithToken("/_test/secure/authenticated", forged);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_from_another_issuer_is_rejected()
    {
        var user = await factory.CreateUserAsync(UserRole.FieldAgronomist);
        var foreign = ForgeToken(user, AgriGuardApiFactory.SigningKey, issuer: "SomeOtherSystem");

        var response = await GetWithToken("/_test/secure/authenticated", foreign);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_expired_token_is_rejected()
    {
        // A two-second access token, so expiry can be observed without mocking the clock:
        // JWT lifetime validation reads the system clock inside the bearer handler.
        using var shortLived = factory.WithWebHostBuilder(b => b.UseSetting("Jwt:AccessTokenLifetime", "00:00:02"));
        var client = shortLived.CreateClient();
        var user = await factory.CreateUserAsync(UserRole.Farmer);

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = user.Email, password = AgriGuardApiFactory.TestPassword });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();

        await Task.Delay(TimeSpan.FromSeconds(3));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/_test/secure/authenticated");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);

        // Requires ClockSkew = TimeSpan.Zero; the five-minute default would let this through.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Auth_endpoints_are_rate_limited()
    {
        // Own host with a tiny bucket: brute-forcing the real 10-per-minute limit would
        // throttle every other test sharing this loopback address.
        using var throttled = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:Auth:PermitLimit", "3"));
        var client = throttled.CreateClient();
        var email = $"ratelimit-{Guid.NewGuid():N}@test.local";

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login",
                new { email, password = "WrongPassword!1" });
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    private Task<HttpResponseMessage> Login(string email, string password) =>
        _client.PostAsJsonAsync("/api/auth/login", new { email, password });

    private async Task<(Guid UserId, string AccessToken, string RefreshToken)> LoginAsync(UserRole role)
    {
        var user = await factory.CreateUserAsync(role);
        var response = await Login(user.Email, AgriGuardApiFactory.TestPassword);
        response.EnsureSuccessStatusCode();
        return Read(await response.Content.ReadFromJsonAsync<JsonElement>(), user.Id);
    }

    private async Task<(Guid UserId, string AccessToken, string RefreshToken)> Refresh(string refreshToken)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return Read(body, body.GetProperty("user").GetProperty("id").GetGuid());
    }

    private static (Guid, string, string) Read(JsonElement body, Guid userId) => (
        userId,
        body.GetProperty("accessToken").GetString()!,
        body.GetProperty("refreshToken").GetString()!);

    private Task<HttpResponseMessage> GetWithToken(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    /// <summary>Mints a token the API never issued, to prove signature and issuer are actually checked.</summary>
    private static string ForgeToken(User user, string signingKey, string issuer)
    {
        var key = new SymmetricSecurityKey(Convert.FromBase64String(signingKey));
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: "AgriGuard.Clients",
            claims: [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString())],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
