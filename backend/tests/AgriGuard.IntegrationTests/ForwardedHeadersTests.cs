using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGuard.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AgriGuard.IntegrationTests;

/// <summary>
/// The API behind Render's TLS proxy. The test host runs outside Development, so HTTPS
/// redirection is on; an HTTPS port makes it actually redirect plain-HTTP requests, which is
/// exactly what loops forever in the cloud if the proxy's X-Forwarded-Proto is ignored.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ForwardedHeadersTests(AgriGuardApiFactory factory)
{
    private static readonly WebApplicationFactoryClientOptions NoRedirects = new() { AllowAutoRedirect = false };

    private WebApplicationFactory<Program> BehindProxy(bool forwardedHeadersEnabled) =>
        factory.WithWebHostBuilder(builder => builder
            .UseSetting("https_port", "443")
            .UseSetting("ForwardedHeaders:Enabled", forwardedHeadersEnabled ? "true" : "false"));

    private static HttpRequestMessage ProxiedRequest(string path, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return request;
    }

    [Fact]
    public async Task Https_terminated_at_the_proxy_is_not_redirected_again()
    {
        using var api = BehindProxy(forwardedHeadersEnabled: true);
        var client = api.CreateClient(NoRedirects);

        var response = await client.SendAsync(ProxiedRequest("/health/live", "203.0.113.7"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_real_scheme_and_client_come_from_the_proxy()
    {
        using var api = BehindProxy(forwardedHeadersEnabled: true);
        var client = api.CreateClient(NoRedirects);

        var response = await client.SendAsync(ProxiedRequest("/_test/connection", "203.0.113.7"));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("https", body.GetProperty("scheme").GetString());
        Assert.Equal("203.0.113.7", body.GetProperty("remoteIp").GetString());
    }

    [Fact]
    public async Task Only_the_hop_the_proxy_appended_is_trusted()
    {
        using var api = BehindProxy(forwardedHeadersEnabled: true);
        var client = api.CreateClient(NoRedirects);

        // A client sends its own X-Forwarded-For; the proxy appends the address it really saw.
        var response = await client.SendAsync(ProxiedRequest("/_test/connection", "198.51.100.1, 203.0.113.7"));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("203.0.113.7", body.GetProperty("remoteIp").GetString());
    }

    [Fact]
    public async Task Without_the_setting_forwarded_headers_are_ignored()
    {
        using var api = BehindProxy(forwardedHeadersEnabled: false);
        var client = api.CreateClient(NoRedirects);

        var response = await client.SendAsync(ProxiedRequest("/_test/connection", "203.0.113.7"));

        // Plain HTTP is redirected to HTTPS, as it was before this setting existed.
        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https", response.Headers.Location?.Scheme);
    }
}
