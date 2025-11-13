using System.Net;
using System.Net.Http.Headers;
using ClaimBridge.Contracts.Reference;
using ClaimBridge.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimBridge.Gateway.Tests;

/// <summary>The gateway's own pipeline: token validation, rate limits and endpoint filters. Downstream services are not running.</summary>
public class GatewayPipelineTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GatewayPipelineTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:UserPerMinute", "1000");
            b.ConfigureServices(s => s.UseTestTokens());
        });

    private HttpClient Client(string? token)
    {
        var c = _factory.CreateClient();
        if (token is not null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [Fact]
    public async Task Composition_endpoint_requires_a_token() =>
        (await Client(null).GetAsync($"/api/claims/{Guid.NewGuid()}/details")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Tokens_without_a_read_scope_are_forbidden() =>
        (await Client(TestTokens.ApiClient(SeedIds.SunriseOrg, "claims.submit")).GetAsync($"/api/claims/{Guid.NewGuid()}/details"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Validation_filter_rejects_unknown_sections()
    {
        var response = await Client(TestTokens.ProviderUser(SeedIds.SunriseOrg)).GetAsync($"/api/claims/{Guid.NewGuid()}/details?include=everything");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("include may only list");
    }

    [Fact]
    public async Task Api_clients_get_their_own_quota_from_the_token()
    {
        var client = Client(TestTokens.ApiClient(SeedIds.SunriseOrg, "claims.read", clientId: "tiny-quota", rateLimit: 2));
        var url = $"/api/claims/{Guid.NewGuid()}/details?include=bogus";
        (await client.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var limited = await client.GetAsync(url);
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.Contains("Retry-After").ShouldBeTrue();

        // another client is unaffected
        (await Client(TestTokens.ApiClient(SeedIds.SunriseOrg, "claims.read", clientId: "other", rateLimit: 2)).GetAsync(url))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Me_endpoint_reflects_token_claims()
    {
        var body = await Client(TestTokens.ProviderUser(SeedIds.LakesideOrg, Roles.ProviderAdmin)).GetStringAsync("/api/gateway/me");
        body.ShouldContain(SeedIds.LakesideOrg.ToString());
        body.ShouldContain("ProviderAdmin");
        body.ShouldContain("\"isStaff\":false");
    }

    [Fact]
    public async Task Proxy_routes_are_staff_only()
    {
        (await Client(TestTokens.ProviderUser(SeedIds.SunriseOrg)).GetAsync("/api/gateway/routes")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var routes = await Client(TestTokens.Staff()).GetStringAsync("/api/gateway/routes");
        routes.ShouldContain("/api/v1/claims/{**catch-all}");
    }

    [Fact]
    public async Task Health_is_anonymous() => (await Client(null).GetAsync("/alive")).StatusCode.ShouldBe(HttpStatusCode.OK);
}
