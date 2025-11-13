using System.Net;
using System.Text;
using ClaimBridge.Gateway.Composition;
using ClaimBridge.Gateway.Filters;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClaimBridge.Gateway.Tests;

public class ClaimDetailsComposerTests
{
    private static readonly Guid ClaimId = Guid.NewGuid();

    private const string ClaimJson = """
        {"summary":{"claimNumber":"CB1"},"history":[
          {"sequence":1,"fromStatus":"Received","toStatus":"Received","trigger":"Created","reason":null,"occurredAt":"2025-11-03T10:00:00Z"},
          {"sequence":2,"fromStatus":"Submitted","toStatus":"AcceptedByPayer","trigger":"PayerAccepted","reason":"ok","occurredAt":"2025-11-03T10:05:00Z"}]}
        """;
    private const string PayerJson = """{"claimId":"x","submissions":[],"history":[{"kind":"999","status":"accepted","detail":null,"at":"2025-11-03T10:02:00Z"}]}""";
    private const string RemittanceJson = """[{"kind":"Payment","paid":80.0,"patientResponsibility":20.0,"postedAt":"2025-11-10T08:00:00Z"}]""";

    private static ClaimDetailsComposer Composer(Dictionary<string, Func<HttpResponseMessage>> sources) =>
        new(new Factory(sources), new GatewayMetrics(new MeterFactory()), NullLogger<ClaimDetailsComposer>.Instance);

    private static Func<HttpResponseMessage> Json(string body) =>
        () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Dictionary<string, Func<HttpResponseMessage>> AllUp() => new()
    {
        [Sources.Claims] = Json(ClaimJson),
        [Sources.Scrubber] = Json("[]"),
        [Sources.Payers] = Json(PayerJson),
        [Sources.Remittance] = Json(RemittanceJson),
        [Sources.Risk] = Json("""{"probability":0.12}""")
    };

    [Fact]
    public async Task All_sources_up_gives_a_full_response_with_a_merged_timeline()
    {
        var result = await Composer(AllUp()).ComposeAsync(ClaimId, Sources.Optional.ToHashSet(), "token", default);

        result.Outcome.ShouldBe(CompositionOutcome.Ok);
        result.Response!.Partial.ShouldBeFalse();
        result.Response.Timeline.Select(t => t.Source).ShouldBe(["claims", "payer", "claims", "remittance"]);
        result.Response.Timeline[1].Text.ShouldBe("accepted");
    }

    [Fact]
    public async Task A_failing_optional_source_is_flagged_not_fatal()
    {
        var sources = AllUp();
        sources[Sources.Risk] = () => throw new HttpRequestException("connection refused");
        sources[Sources.Remittance] = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var result = await Composer(sources).ComposeAsync(ClaimId, Sources.Optional.ToHashSet(), "token", default);
        result.Outcome.ShouldBe(CompositionOutcome.Ok);
        result.Response!.Partial.ShouldBeTrue();
        result.Response.Unavailable.ShouldBe([Sources.Remittance, Sources.Risk], ignoreOrder: true);
        result.Response.Risk.ShouldBeNull();
    }

    [Fact]
    public async Task Not_found_from_an_optional_source_just_means_no_data_yet()
    {
        var sources = AllUp();
        sources[Sources.Risk] = () => new HttpResponseMessage(HttpStatusCode.NotFound);
        var result = await Composer(sources).ComposeAsync(ClaimId, Sources.Optional.ToHashSet(), "token", default);
        result.Response!.Partial.ShouldBeFalse();
    }

    [Fact]
    public async Task Claims_service_decides_not_found_and_failure()
    {
        var sources = AllUp();
        sources[Sources.Claims] = () => new HttpResponseMessage(HttpStatusCode.NotFound);
        (await Composer(sources).ComposeAsync(ClaimId, Sources.Optional.ToHashSet(), null, default)).Outcome.ShouldBe(CompositionOutcome.NotFound);

        sources[Sources.Claims] = () => throw new HttpRequestException("down");
        (await Composer(sources).ComposeAsync(ClaimId, Sources.Optional.ToHashSet(), null, default)).Outcome.ShouldBe(CompositionOutcome.Failed);
    }

    [Fact]
    public async Task Only_requested_sections_are_fetched_and_the_bearer_token_is_forwarded()
    {
        var factory = new Factory(AllUp());
        var composer = new ClaimDetailsComposer(factory, new GatewayMetrics(new MeterFactory()), NullLogger<ClaimDetailsComposer>.Instance);
        await composer.ComposeAsync(ClaimId, new HashSet<string> { Sources.Payers }, "abc", default);

        factory.Calls.Select(c => c.Source).ShouldBe([Sources.Claims, Sources.Payers], ignoreOrder: true);
        factory.Calls.ShouldAllBe(c => c.Bearer == "abc");
    }

    private sealed class Factory(Dictionary<string, Func<HttpResponseMessage>> sources) : IHttpClientFactory
    {
        public List<(string Source, string? Bearer)> Calls { get; } = [];

        public HttpClient CreateClient(string name) => new(new Handler(() => sources[name](), bearer => { lock (Calls) Calls.Add((name, bearer)); }))
        {
            BaseAddress = new Uri($"http://{name}/")
        };
    }

    private sealed class Handler(Func<HttpResponseMessage> respond, Action<string?> record) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            record(request.Headers.Authorization?.Parameter);
            return Task.FromResult(respond());
        }
    }

    private sealed class MeterFactory : System.Diagnostics.Metrics.IMeterFactory
    {
        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
