using ClaimBridge.PayersGateway.Adapters;
using ClaimBridge.PayersGateway.Adapters.PayerA;
using ClaimBridge.PayersGateway.Adapters.PayerB;
using ClaimBridge.PayersGateway.Adapters.PayerC;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace ClaimBridge.PayersGateway.Tests;

public class PayerAAdapterTests : IDisposable
{
    private readonly AdapterHost _host = new();

    [Fact]
    public async Task Accepted_submission_returns_the_tracking_id_and_sends_northwind_fields()
    {
        _host.Payer.Given(Request.Create().WithPath("/payer-a/v1/claims").UsingPost()
                .WithBody(new JsonPartialMatcher("""{"claimRef":"CB250100200","member":{"id":"NWH1000002"},"services":[{"code":"99213"}]}""")))
            .RespondWith(Response.Create().WithBodyAsJson(new { status = "ACCEPTED", trackingId = "NW123" }));

        var result = await _host.Adapter<PayerAAdapter>().SubmitAsync(AdapterHost.Claim(), 1, default);
        result.ShouldBe(new SubmitResult(SubmitOutcome.Accepted, "NW123"));
    }

    [Fact]
    public async Task Rejection_errors_are_translated_into_plain_reasons()
    {
        _host.Payer.Given(Request.Create().WithPath("/payer-a/v1/claims").UsingPost())
            .RespondWith(Response.Create().WithBodyAsJson(new { status = "REJECTED", errors = new[] { new { code = "A7-562", text = "Member id not found" } } }));

        var result = await _host.Adapter<PayerAAdapter>().SubmitAsync(AdapterHost.Claim(), 1, default);
        result.Outcome.ShouldBe(SubmitOutcome.Rejected);
        result.Rejections.ShouldBe(["A7-562: Member id not found"]);
    }

    [Fact]
    public async Task Eligibility_maps_northwind_coverage()
    {
        _host.Payer.Given(Request.Create().WithPath("/payer-a/v1/eligibility").UsingPost())
            .RespondWith(Response.Create().WithBodyAsJson(new { active = true, plan = "Northwind Silver PPO", coverageStart = "2022-01-01", copay = 25, deductibleRemaining = 300 }));

        var e = await _host.Adapter<PayerAAdapter>().CheckEligibilityAsync(new EligibilityQuery("NWH1000002", new DateOnly(1958, 2, 12), new DateOnly(2025, 11, 3)), default);
        e.ShouldSatisfyAllConditions(x => x.Status.ShouldBe("Active"), x => x.CoverageStart.ShouldBe(new DateOnly(2022, 1, 1)), x => x.Copay.ShouldBe(25));
    }

    [Theory]
    [InlineData("PENDED", null, "pended", null)]
    [InlineData("IN_REVIEW", null, "in-process", null)]
    [InlineData("FINALIZED", "PARTIAL", "finalized", "partial")]
    [InlineData("FINALIZED", "DENIED", "finalized", "denied")]
    [InlineData("UNKNOWN", null, "not-found", null)]
    public void Northwind_status_words_map_to_277_vocabulary(string status, string? outcome, string expected, string? expectedOutcome)
    {
        var answer = PayerAAdapter.Translate(status, outcome, null, "NW1");
        answer.Status.ShouldBe(expected);
        answer.Outcome.ShouldBe(expectedOutcome);
    }

    [Fact]
    public async Task Server_errors_surface_as_payer_unavailable_after_retries()
    {
        _host.Payer.Given(Request.Create().WithPath("/payer-a/v1/claims").UsingPost()).RespondWith(Response.Create().WithStatusCode(503));
        await Should.ThrowAsync<PayerUnavailableException>(() => _host.Adapter<PayerAAdapter>().SubmitAsync(AdapterHost.Claim(), 1, default));
        // first try plus two retries
        _host.Payer.LogEntries.Count(e => e.RequestMessage.Path == "/payer-a/v1/claims").ShouldBe(3);
    }

    public void Dispose() => _host.Dispose();
}

public class PayerBAdapterTests : IDisposable
{
    private readonly AdapterHost _host = new();

    [Fact]
    public async Task Submissions_are_queued_for_the_next_batch_file() =>
        (await _host.Adapter<PayerBAdapter>().SubmitAsync(AdapterHost.Claim("PAYER-B"), 1, default)).Outcome.ShouldBe(SubmitOutcome.Queued);

    [Theory]
    [InlineData("1", "Active")]
    [InlineData("6", "Inactive")]
    [InlineData("X", "Unknown")]
    public async Task Eligibility_codes_follow_271_eb01(string code, string expected)
    {
        _host.Payer.Given(Request.Create().WithPath("/payer-b/eligibility").UsingPost())
            .RespondWith(Response.Create().WithBodyAsJson(new { eligibilityCode = code, effectiveDate = "20240101" }));
        (await _host.Adapter<PayerBAdapter>().CheckEligibilityAsync(new EligibilityQuery("BPK1", new DateOnly(1962, 3, 13), new DateOnly(2025, 11, 3)), default))
            .Status.ShouldBe(expected);
    }

    [Theory]
    [InlineData("A2", "received", null)]
    [InlineData("P1", "pended", null)]
    [InlineData("F3", "finalized", "partial")]
    [InlineData("F2", "finalized", "denied")]
    public async Task Status_categories_translate(string category, string status, string? outcome)
    {
        _host.Payer.Given(Request.Create().WithPath("/payer-b/claim-status").UsingPost())
            .RespondWith(Response.Create().WithBodyAsJson(new { statusCategory = category, statusCode = "20", payerClaimNumber = "BPK1" }));
        var answer = await _host.Adapter<PayerBAdapter>().InquireStatusAsync(new StatusQuery("CB1", null), default);
        (answer.Status, answer.Outcome).ShouldBe((status, outcome));
    }

    public void Dispose() => _host.Dispose();
}

public class PayerCAdapterTests : IDisposable
{
    private readonly AdapterHost _host = new();

    [Fact]
    public async Task Transient_failures_are_retried_until_cascade_answers()
    {
        _host.Payer.Given(Request.Create().WithPath("/payer-c/api/submit").UsingPost()).InScenario("flaky").WillSetStateTo("once")
            .RespondWith(Response.Create().WithStatusCode(500));
        _host.Payer.Given(Request.Create().WithPath("/payer-c/api/submit").UsingPost()).InScenario("flaky").WhenStateIs("once")
            .RespondWith(Response.Create().WithBodyAsJson(new { result = "OK", @ref = "CC77" }));

        var result = await _host.Adapter<PayerCAdapter>().SubmitAsync(AdapterHost.Claim("PAYER-C"), 1, default);
        result.ShouldBe(new SubmitResult(SubmitOutcome.Accepted, "CC77"));
    }

    [Fact]
    public async Task Slow_responses_hit_the_attempt_timeout()
    {
        _host.Payer.Given(Request.Create().WithPath("/payer-c/api/submit").UsingPost())
            .RespondWith(Response.Create().WithDelay(TimeSpan.FromSeconds(3)).WithBodyAsJson(new { result = "OK" }));
        var ex = await Should.ThrowAsync<PayerUnavailableException>(() => _host.Adapter<PayerCAdapter>().SubmitAsync(AdapterHost.Claim("PAYER-C"), 1, default));
        ex.Message.ShouldContain("Timeout");
    }

    [Fact]
    public async Task Outage_opens_the_circuit_and_later_calls_fail_fast()
    {
        _host.Payer.Given(Request.Create().WithPath("/payer-c/api/submit").UsingPost()).RespondWith(Response.Create().WithStatusCode(503));
        var adapter = _host.Adapter<PayerCAdapter>();
        for (var i = 0; i < 3; i++)
            await Should.ThrowAsync<PayerUnavailableException>(() => adapter.SubmitAsync(AdapterHost.Claim("PAYER-C"), 1, default));

        _host.Health.Snapshot("PAYER-C").CircuitState.ShouldBe("Open");
        var calls = _host.Payer.LogEntries.Count();
        var ex = await Should.ThrowAsync<PayerUnavailableException>(() => adapter.SubmitAsync(AdapterHost.Claim("PAYER-C"), 1, default));
        ex.Message.ShouldContain("circuit open");
        _host.Payer.LogEntries.Count().ShouldBe(calls);
    }

    [Fact]
    public async Task Each_payer_has_its_own_circuit()
    {
        _host.Payer.Given(Request.Create().WithPath("/payer-c/api/submit").UsingPost()).RespondWith(Response.Create().WithStatusCode(503));
        _host.Payer.Given(Request.Create().WithPath("/payer-a/v1/claims").UsingPost())
            .RespondWith(Response.Create().WithBodyAsJson(new { status = "ACCEPTED", trackingId = "NW9" }));
        for (var i = 0; i < 3; i++)
            await Should.ThrowAsync<PayerUnavailableException>(() => _host.Adapter<PayerCAdapter>().SubmitAsync(AdapterHost.Claim("PAYER-C"), 1, default));

        (await _host.Adapter<PayerAAdapter>().SubmitAsync(AdapterHost.Claim(), 1, default)).Outcome.ShouldBe(SubmitOutcome.Accepted);
        _host.Health.Snapshot("PAYER-A").CircuitState.ShouldBe("Closed");
    }

    [Fact]
    public async Task Cascade_vocabulary_stays_inside_the_adapter()
    {
        _host.Payer.Given(Request.Create().WithPath("/payer-c/api/status/CB1").UsingGet())
            .RespondWith(Response.Create().WithBodyAsJson(new { state = "PARTPAID", detail = "line 2 bundled", @ref = "CC1" }));
        var answer = await _host.Adapter<PayerCAdapter>().InquireStatusAsync(new StatusQuery("CB1", null), default);
        answer.ShouldBe(new StatusAnswer("finalized", "partial", "F1", "line 2 bundled", "CC1"));
    }

    public void Dispose() => _host.Dispose();
}
