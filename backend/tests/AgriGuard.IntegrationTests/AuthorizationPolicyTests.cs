using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Domain.Identity;
using AgriGuard.IntegrationTests.Infrastructure;

namespace AgriGuard.IntegrationTests;

/// <summary>
/// The specification for the four policies in AuthorizationSetup: exactly which role each one
/// admits, and — more importantly — which roles it must refuse.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuthorizationPolicyTests(AgriGuardApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    // Only an agronomist may approve a prescription — the human check on the agent's proposal.
    [InlineData("approve", UserRole.FieldAgronomist, HttpStatusCode.OK)]
    [InlineData("approve", UserRole.Farmer, HttpStatusCode.Forbidden)]
    [InlineData("approve", UserRole.AgroDealer, HttpStatusCode.Forbidden)]
    [InlineData("approve", UserRole.CoopAdministrator, HttpStatusCode.Forbidden)]
    // Stock and pricing belong to the dealer.
    [InlineData("inventory", UserRole.AgroDealer, HttpStatusCode.OK)]
    [InlineData("inventory", UserRole.Farmer, HttpStatusCode.Forbidden)]
    [InlineData("inventory", UserRole.FieldAgronomist, HttpStatusCode.Forbidden)]
    // Regulatory rules and slots belong to the co-op administrator.
    [InlineData("rules", UserRole.CoopAdministrator, HttpStatusCode.OK)]
    [InlineData("rules", UserRole.Farmer, HttpStatusCode.Forbidden)]
    [InlineData("rules", UserRole.FieldAgronomist, HttpStatusCode.Forbidden)]
    [InlineData("rules", UserRole.AgroDealer, HttpStatusCode.Forbidden)]
    // Farm data: the farmer who owns it, the agronomist advising the district, the administrator.
    [InlineData("owns-farm", UserRole.Farmer, HttpStatusCode.OK)]
    [InlineData("owns-farm", UserRole.FieldAgronomist, HttpStatusCode.OK)]
    [InlineData("owns-farm", UserRole.CoopAdministrator, HttpStatusCode.OK)]
    [InlineData("owns-farm", UserRole.AgroDealer, HttpStatusCode.Forbidden)]
    public async Task Policy_admits_only_the_roles_that_own_the_operation(
        string endpoint, UserRole role, HttpStatusCode expected)
    {
        var token = await TokenFor(role);

        var response = await Get($"/_test/secure/{endpoint}", token);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("inventory")]
    [InlineData("rules")]
    [InlineData("owns-farm")]
    public async Task Policy_endpoints_reject_anonymous_callers_with_401_not_403(string endpoint)
    {
        var response = await _client.GetAsync($"/_test/secure/{endpoint}");

        // 401 means "who are you?", 403 means "I know you, and no" — clients branch on the difference.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Endpoints_are_protected_by_default_without_any_attribute()
    {
        // The fallback policy: forgetting [Authorize] on a new controller must fail closed.
        var response = await _client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_and_docs_stay_reachable_without_a_token()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/openapi/v1.json")).StatusCode);
    }

    private async Task<string> TokenFor(UserRole role)
    {
        var user = await factory.CreateUserAsync(role);
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = user.Email, password = AgriGuardApiFactory.TestPassword });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private Task<HttpResponseMessage> Get(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }
}
