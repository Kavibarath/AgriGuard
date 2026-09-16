using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.Domain.Identity;
using AgriGuard.Infrastructure.Persistence;
using AgriGuard.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriGuard.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ApiFoundationTests(AgriGuardApiFactory factory)
{
    private const string CorrelationHeader = "X-Correlation-Id";
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_reports_healthy_database()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        var check = Assert.Single(body.GetProperty("checks").EnumerateArray());
        Assert.Equal("database", check.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Liveness_probe_runs_no_dependency_checks()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("checks").EnumerateArray());
    }

    [Fact]
    public async Task Migrations_and_reference_seed_are_applied()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgriGuardDbContext>();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(4, await db.Districts.CountAsync());
        Assert.Equal(80, await db.ProductCropApprovals.CountAsync());
        // Carbofuran is withdrawn on every crop (rule V2 / golden case G5).
        Assert.Equal(3, await db.ProductCropApprovals.CountAsync(a => !a.IsActive));
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details_404_to_an_authenticated_caller()
    {
        var user = await factory.CreateUserAsync(UserRole.Farmer);
        var login = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = user.Email, password = AgriGuardApiFactory.TestPassword });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/does-not-exist");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        var problem = await AssertProblem(response, HttpStatusCode.NotFound);
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Unknown_route_returns_401_to_an_anonymous_caller()
    {
        // The fallback policy covers unmatched routes too, so anonymous probing cannot map which routes exist.
        var response = await _client.GetAsync("/api/does-not-exist");

        await AssertProblem(response, HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("not-found", HttpStatusCode.NotFound, "Plot '42' was not found.")]
    [InlineData("forbidden", HttpStatusCode.Forbidden, "You do not have access to this resource.")]
    [InlineData("conflict", HttpStatusCode.Conflict, "Case already has a pending run.")]
    [InlineData("business-rule", (HttpStatusCode)422, "Cannot move from Sown to Harvested.")]
    public async Task App_exceptions_map_to_status_and_keep_their_message(
        string fault, HttpStatusCode expected, string detail)
    {
        var response = await _client.GetAsync($"/_test/faults/{fault}");

        var problem = await AssertProblem(response, expected);
        Assert.Equal(detail, problem.GetProperty("detail").GetString());
        Assert.Equal($"/_test/faults/{fault}", problem.GetProperty("instance").GetString());
    }

    [Fact]
    public async Task Business_rule_violation_exposes_machine_readable_code()
    {
        var problem = await AssertProblem(
            await _client.GetAsync("/_test/faults/business-rule"), (HttpStatusCode)422);

        Assert.Equal("ILLEGAL_STAGE_TRANSITION", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Validation_exception_lists_field_errors()
    {
        var problem = await AssertProblem(
            await _client.GetAsync("/_test/faults/validation"), HttpStatusCode.BadRequest);

        var messages = problem.GetProperty("errors").GetProperty("areaHectares");
        Assert.Equal("Must be greater than 0.", messages[0].GetString());
    }

    [Fact]
    public async Task Unhandled_exception_returns_500_without_leaking_details()
    {
        var response = await _client.GetAsync("/_test/faults/unhandled");

        var problem = await AssertProblem(response, HttpStatusCode.InternalServerError);
        Assert.DoesNotContain("secret connection detail", problem.GetRawText());
    }

    [Fact]
    public async Task Correlation_id_is_echoed_and_stamped_on_problem_details()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_test/faults/not-found");
        request.Headers.Add(CorrelationHeader, "flutter-req-123");

        var response = await _client.SendAsync(request);

        Assert.Equal("flutter-req-123", Assert.Single(response.Headers.GetValues(CorrelationHeader)));
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("flutter-req-123", problem.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Correlation_id_is_generated_when_missing()
    {
        var response = await _client.GetAsync("/health/live");

        var id = Assert.Single(response.Headers.GetValues(CorrelationHeader));
        Assert.Matches("^[0-9a-f]{32}$", id);
    }

    [Fact]
    public async Task Unsafe_inbound_correlation_id_is_replaced()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation(CorrelationHeader, "abc def<script>");

        var response = await _client.SendAsync(request);

        Assert.Matches("^[0-9a-f]{32}$", Assert.Single(response.Headers.GetValues(CorrelationHeader)));
    }

    [Fact]
    public async Task OpenApi_document_and_swagger_ui_are_served()
    {
        var document = await _client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var scheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());

        var ui = await _client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
    }

    private static async Task<JsonElement> AssertProblem(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)expected, problem.GetProperty("status").GetInt32());
        return problem;
    }
}
