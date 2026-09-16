using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Application.Auth;
using AgriGuard.Domain.Identity;
using AgriGuard.IntegrationTests.Infrastructure;

namespace AgriGuard.IntegrationTests;

/// <summary>
/// The specification for what JwtTokenService.CreateAccessToken must put inside the token.
/// Reads the payload without validating it — this is about content, not trust.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TokenClaimsTests(AgriGuardApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Token_identifies_the_user_and_their_role()
    {
        var user = await factory.CreateUserAsync(UserRole.FieldAgronomist);

        var token = await LoginAndDecode(user);

        Assert.Equal(user.Id.ToString(), token.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(nameof(UserRole.FieldAgronomist), token.Claims.First(c => c.Type == AgriGuardClaims.Role).Value);
        Assert.Equal(user.Email, token.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value);
    }

    [Fact]
    public async Task Token_carries_the_district_that_scopes_case_access()
    {
        var user = await factory.CreateUserAsync(UserRole.FieldAgronomist, withDistrict: true);

        var token = await LoginAndDecode(user);

        Assert.Equal(user.DistrictId!.Value.ToString(),
            token.Claims.First(c => c.Type == AgriGuardClaims.DistrictId).Value);
    }

    [Fact]
    public async Task Token_omits_the_district_claim_when_the_user_has_no_district()
    {
        var user = await factory.CreateUserAsync(UserRole.CoopAdministrator, withDistrict: false);

        var token = await LoginAndDecode(user);

        Assert.DoesNotContain(token.Claims, c => c.Type == AgriGuardClaims.DistrictId);
    }

    [Fact]
    public async Task Each_token_has_a_unique_id()
    {
        var user = await factory.CreateUserAsync(UserRole.Farmer);

        var first = await LoginAndDecode(user);
        var second = await LoginAndDecode(user);

        Assert.NotEqual(
            first.Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value,
            second.Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value);
    }

    [Fact]
    public async Task Token_is_stamped_with_this_issuer_audience_and_a_short_lifetime()
    {
        var user = await factory.CreateUserAsync(UserRole.Farmer);

        var token = await LoginAndDecode(user);

        Assert.Equal("AgriGuard", token.Issuer);
        Assert.Contains("AgriGuard.Clients", token.Audiences);
        Assert.InRange(token.ValidTo - DateTime.UtcNow, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(16));
    }

    [Fact]
    public async Task Token_never_carries_the_password_hash_or_credit_limit()
    {
        var user = await factory.CreateUserAsync(UserRole.Farmer);

        var token = await LoginAndDecode(user);

        // A JWT is signed, not encrypted: anything in here is readable by whoever holds it.
        Assert.DoesNotContain(token.Claims, c =>
            c.Type.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            c.Type.Contains("credit", StringComparison.OrdinalIgnoreCase) ||
            c.Value.StartsWith("pbkdf2-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Claims_survive_the_round_trip_into_the_api()
    {
        var user = await factory.CreateUserAsync(UserRole.AgroDealer);
        var accessToken = await LoginForToken(user);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/_test/secure/claims");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var claims = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(user.Id, claims.GetProperty("userId").GetGuid());
        Assert.Equal("AgroDealer", claims.GetProperty("role").GetString());
        Assert.Equal(user.DistrictId, claims.GetProperty("districtId").GetGuid());
    }

    [Fact]
    public async Task Me_endpoint_returns_the_caller_identity()
    {
        var user = await factory.CreateUserAsync(UserRole.Farmer);
        var accessToken = await LoginForToken(user);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(user.Id, me.GetProperty("id").GetGuid());
        Assert.Equal("Farmer", me.GetProperty("role").GetString());
    }

    private async Task<string> LoginForToken(User user)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = user.Email, password = AgriGuardApiFactory.TestPassword });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private async Task<JwtSecurityToken> LoginAndDecode(User user) =>
        new JwtSecurityTokenHandler().ReadJwtToken(await LoginForToken(user));
}
