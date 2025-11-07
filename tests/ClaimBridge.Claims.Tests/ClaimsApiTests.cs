using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClaimBridge.Claims.Data;
using ClaimBridge.Contracts.Reference;
using ClaimBridge.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace ClaimBridge.Claims.Tests;

[CollectionDefinition("sql")]
public class SqlCollection : ICollectionFixture<SqlServerFixture>;

/// <summary>Claims API against a real SQL Server (Testcontainers); providers is stubbed with WireMock.</summary>
[Collection("sql")]
public sealed class ClaimsApiTests : IAsyncLifetime
{
    private static readonly Guid Sunrise = SeedIds.SunriseOrg;
    private static readonly Guid Lakeside = SeedIds.LakesideOrg;

    private readonly SqlServerFixture _sql;
    private WireMockServer? _downstream;
    private WebApplicationFactory<Program>? _factory;

    public ClaimsApiTests(SqlServerFixture sql) => _sql = sql;

    public Task InitializeAsync()
    {
        if (!DockerAvailability.IsAvailable) return Task.CompletedTask;
        _downstream = WireMockServer.Start();
        _downstream.Given(Request.Create().WithPath("/internal/enrollments/check").WithParam("payerId", "PAYER-A"))
            .RespondWith(Response.Create().WithBodyAsJson(new { orgId = Sunrise, payerId = "PAYER-A", active = true, status = "Active" }));
        _downstream.Given(Request.Create().WithPath("/internal/enrollments/check").WithParam("payerId", "PAYER-C"))
            .RespondWith(Response.Create().WithBodyAsJson(new { orgId = Sunrise, payerId = "PAYER-C", active = false, status = "Pending" }));

        var url = _downstream.Url + "/";
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Test");
            b.UseSetting("ConnectionStrings:ClaimsDb", _sql.ConnectionString("cb_claims_" + Guid.NewGuid().ToString("N")[..8]));
            b.UseSetting("ConnectionStrings:Redis", "");
            b.UseSetting("Kafka:BootstrapServers", "");
            b.UseSetting("Phi:Key", Convert.ToBase64String(new byte[32]));
            b.UseSetting("Seed:Enabled", "true");
            b.UseSetting("Services:Providers", url);
            b.UseSetting("Services:PayersGateway", url);
            b.UseSetting("Services:DenialPredictor", url);
            b.UseSetting("Lifecycle:TimeoutScanSeconds", "3600");
            b.ConfigureServices(s => s.UseTestTokens());
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        _downstream?.Stop();
    }

    private HttpClient Client(string token)
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static object ClaimBody(string payer = "PAYER-A", decimal charge = 120, string dos = "2025-11-03") => new
    {
        payerId = payer,
        billingNpi = "1999000015",
        renderingNpi = "1999000114",
        patient = new { memberId = "NWH1000002", firstName = "Jamie", lastName = "Example", dateOfBirth = "1958-02-12", gender = "F" },
        diagnoses = new[] { "I10" },
        lines = new[] { new { procedureCode = "99213", modifiers = Array.Empty<string>(), units = 1, charge, serviceDate = dos, placeOfService = "11", dxPointers = new[] { 1 } } }
    };

    private static HttpRequestMessage Submit(object body, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/claims") { Content = JsonContent.Create(body) };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    [DockerFact]
    public async Task Same_idempotency_key_replays_the_first_response_without_a_duplicate()
    {
        var client = Client(TestTokens.ApiClient(Sunrise, "claims.submit claims.read"));
        var first = await client.SendAsync(Submit(ClaimBody(), "key-1"));
        var second = await client.SendAsync(Submit(ClaimBody(), "key-1"));

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        second.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        second.Headers.GetValues("Idempotent-Replayed").Single().ShouldBe("true");
        (await second.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());

        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimsDb>();
        (await db.Claims.CountAsync()).ShouldBe(1);
        // the claim and its events committed in one transaction (outbox)
        (await db.OutboxMessages.Select(o => o.Topic).ToListAsync()).ShouldBe(["claims.status-changed", "claims.claim-received"], ignoreOrder: true);
    }

    [DockerFact]
    public async Task Reusing_a_key_with_a_different_body_is_rejected_and_a_missing_key_is_a_bad_request()
    {
        var client = Client(TestTokens.ApiClient(Sunrise, "claims.submit"));
        (await client.SendAsync(Submit(ClaimBody(charge: 120), "key-2"))).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await client.SendAsync(Submit(ClaimBody(charge: 999), "key-2"))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await client.SendAsync(Submit(ClaimBody(), null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [DockerFact]
    public async Task Client_without_the_submit_scope_is_forbidden()
    {
        var client = Client(TestTokens.ApiClient(Sunrise, "claims.read"));
        (await client.SendAsync(Submit(ClaimBody(), "key-3"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var anonymous = _factory!.CreateClient();
        (await anonymous.SendAsync(Submit(ClaimBody(), "key-3"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [DockerFact]
    public async Task Claims_to_a_payer_without_active_enrollment_are_blocked()
    {
        var response = await Client(TestTokens.ApiClient(Sunrise, "claims.submit")).SendAsync(Submit(ClaimBody(payer: "PAYER-C"), "key-4"));
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).ShouldContain("not actively enrolled");
    }

    [DockerFact]
    public async Task Provider_users_cannot_read_another_organisations_claims()
    {
        var created = await (await Client(TestTokens.ApiClient(Sunrise, "claims.submit claims.read"))
            .SendAsync(Submit(ClaimBody(), "key-5"))).Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var id = created!["claimId"].ToString();

        (await Client(TestTokens.ProviderUser(Sunrise)).GetAsync($"/api/claims/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Client(TestTokens.ProviderUser(Lakeside)).GetAsync($"/api/claims/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Client(TestTokens.Staff()).GetAsync($"/api/claims/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var lakesideList = await Client(TestTokens.ProviderUser(Lakeside)).GetFromJsonAsync<Dictionary<string, object>>("/api/claims");
        lakesideList!["total"].ToString().ShouldBe("0");
    }

    [DockerFact]
    public async Task Viewing_a_claim_with_phi_is_audited()
    {
        var created = await (await Client(TestTokens.ApiClient(Sunrise, "claims.submit claims.read"))
            .SendAsync(Submit(ClaimBody(), "key-6"))).Content.ReadFromJsonAsync<Dictionary<string, object>>();
        await Client(TestTokens.ProviderUser(Sunrise)).GetAsync($"/api/claims/{created!["claimId"]}");

        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimsDb>();
        (await db.AuditLog.CountAsync(a => a.Action == "phi.viewed" && a.Target == created["claimNumber"].ToString())).ShouldBe(1);
        // member id is stored encrypted, only the last four in clear
        var stored = await db.Claims.SingleAsync(c => c.ClaimNumber == created["claimNumber"].ToString());
        stored.MemberIdProtected.ShouldStartWith("v1:");
        stored.MemberIdProtected.ShouldNotContain("NWH1000002");
        stored.MemberIdLast4.ShouldBe("0002");
    }

    [DockerFact]
    public async Task Second_identical_claim_is_flagged_as_a_possible_duplicate()
    {
        var client = Client(TestTokens.ApiClient(Sunrise, "claims.submit claims.read"));
        var first = await (await client.SendAsync(Submit(ClaimBody(charge: 120), "key-7"))).Content.ReadFromJsonAsync<Dictionary<string, object>>();
        await client.SendAsync(Submit(ClaimBody(charge: 125), "key-8"));

        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimsDb>();
        var duplicate = await db.Claims.OrderByDescending(c => c.CreatedAt).FirstAsync();
        duplicate.PossibleDuplicateOf.ShouldBe(first!["claimNumber"].ToString());
    }

    [DockerFact]
    public async Task A_claim_still_being_scrubbed_cannot_be_corrected_yet()
    {
        var client = Client(TestTokens.ProviderUser(Sunrise));
        var created = await (await client.PostAsJsonAsync("/api/claims", ClaimBody())).Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var response = await client.PostAsJsonAsync($"/api/claims/{created!["id"]}/correct", ClaimBody(charge: 130));
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
