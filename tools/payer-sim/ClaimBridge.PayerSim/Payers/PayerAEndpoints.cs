using ClaimBridge.Contracts.Reference;
using ClaimBridge.PayerSim.Engine;

namespace ClaimBridge.PayerSim.Payers;

/// <summary>Northwind Health Plan: synchronous JSON acknowledgement, then a callback and an 835 a little later.</summary>
public static class PayerAEndpoints
{
    public sealed record NwMember(string Id, string First, string Last, string Dob, string Sex);
    public sealed record NwProvider(string Npi, string TaxId, string Name);
    public sealed record NwService(string Code, string[] Mods, decimal Qty, decimal Amount, string Dos, string Pos, int[] DxRefs);
    public sealed record NwClaim(string SubmitterId, string ClaimRef, string Frequency, string? OriginalTrackingId, NwMember Member,
        NwProvider Billing, string? RenderingNpi, string? AuthNumber, string[] Dx, NwService[] Services, decimal Total, string CallbackUrl);

    public static void MapPayerA(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/payer-a/v1").WithTags("Payer A (Northwind)");

        group.MapPost("/claims", (NwClaim body, ClaimStore store, MemberDirectory members, SimScheduler scheduler,
            AdjudicationFlow flow, RemittanceWriter remits, IHttpClientFactory http, SimScenario scenario, IConfiguration config) =>
        {
            var claim = new SimClaim(SeedIds.PayerA, body.ClaimRef, body.Frequency, body.OriginalTrackingId, body.Member.Id,
                DateOnly.Parse(body.Member.Dob), body.Billing.Npi, body.Billing.Name, body.AuthNumber, body.Dx,
                body.Services.Select((s, i) => new SimLine(i + 1, s.Code, s.Mods, s.Qty, s.Amount, DateOnly.Parse(s.Dos), s.Pos)).ToList());

            if (members.Find(claim.MemberId) is null)
                return Results.Ok(new
                {
                    status = "REJECTED", trackingId = (string?)null,
                    errors = new[] { new { code = "A7-562", text = "Member id not found" } }
                });

            var record = store.Add(claim, AdjudicationFlow.Prefix(SeedIds.PayerA));
            if (scenario.SilentPayerA)
                return Results.Ok(new { status = "ACCEPTED", trackingId = record.PayerClaimId, errors = Array.Empty<object>() });

            var delay = TimeSpan.FromSeconds(scenario.AdjudicationDelaySeconds);
            scheduler.Schedule(delay / 2, ct => Callback(http, config, record, "IN_REVIEW", null, "Claim under review", ct));
            scheduler.Schedule(delay, async ct =>
            {
                var payments = flow.Adjudicate(record);
                var status = record.State == SimClaimState.Pended ? "PENDED" : "FINALIZED";
                var outcome = record.Outcome switch { "paid" => "PAID", "partial" => "PARTIAL", "denied" => "DENIED", _ => null };
                await Callback(http, config, record, status, status == "PENDED" ? null : outcome, record.Note, ct);
                await remits.WriteAsync(SeedIds.PayerA, claim.BillingNpi, claim.BillingName, payments, ct);
            });
            return Results.Ok(new { status = "ACCEPTED", trackingId = record.PayerClaimId, errors = Array.Empty<object>() });
        });

        group.MapPost("/eligibility", (EligibilityRequest req, MemberDirectory members) =>
        {
            var m = members.Find(req.MemberId);
            if (m is null || m.PayerId != SeedIds.PayerA || m.DateOfBirth.ToString("yyyy-MM-dd") != req.Dob)
                return Results.Ok(new { active = false, plan = (string?)null });
            var dos = DateOnly.Parse(req.Dos);
            return Results.Ok(new
            {
                active = members.IsCovered(m, dos), plan = m.Plan, coverageStart = m.CoverageStart.ToString("yyyy-MM-dd"),
                coverageEnd = m.CoverageEnd?.ToString("yyyy-MM-dd"), copay = m.Copay, deductibleRemaining = Math.Max(0, m.Deductible - m.DeductibleMet)
            });
        });

        group.MapPost("/claim-status", (StatusRequest req, ClaimStore store) =>
        {
            var r = store.Find(SeedIds.PayerA, req.ClaimRef, req.TrackingId);
            if (r is null) return Results.Ok(new { status = "UNKNOWN", note = "No record of this claim" });
            var status = r.State switch
            {
                SimClaimState.Received => "RECEIVED",
                SimClaimState.Pended => "PENDED",
                SimClaimState.InReview => "IN_REVIEW",
                SimClaimState.Rejected => "REJECTED",
                _ => "FINALIZED"
            };
            var outcome = r.Outcome switch { "paid" => "PAID", "partial" => "PARTIAL", "denied" => "DENIED", _ => null };
            return Results.Ok(new { status, outcome, note = r.Note, trackingId = r.PayerClaimId });
        });
    }

    public sealed record EligibilityRequest(string MemberId, string Dob, string Dos);
    public sealed record StatusRequest(string ClaimRef, string? TrackingId);

    private static async Task Callback(IHttpClientFactory http, IConfiguration config, SimClaimRecord record, string status,
        string? outcome, string? note, CancellationToken ct)
    {
        if (status == "IN_REVIEW") record.State = SimClaimState.InReview;
        var client = http.CreateClient();
        client.DefaultRequestHeaders.Add("X-Northwind-Token", config["Sim:NorthwindCallbackToken"] ?? "");
        var url = config["Sim:NorthwindCallbackUrl"] ?? "http://localhost:5105/api/payers/payer-a/callbacks/adjudication";
        using var response = await client.PostAsJsonAsync(url,
            new { trackingId = record.PayerClaimId, claimRef = record.Claim.ClaimNumber, status, outcome, note }, ct);
    }
}
