using System.Globalization;
using ClaimBridge.Contracts.Reference;
using ClaimBridge.PayerSim.Engine;

namespace ClaimBridge.PayerSim.Payers;

/// <summary>Cascade Care: slow and unreliable on purpose, to exercise the gateway's circuit breaker and retries.</summary>
public static class PayerCEndpoints
{
    public sealed record CcLine(string Cpt, string ModifierList, int Units, string Charge, string ServiceDate, string Pos, string DxLinks);
    public sealed record CcSubmit(string ExternalRef, string ClaimType, string? ReplacesRef, string MemberNumber, string LastName,
        string FirstName, string BirthDate, string Gender, string ProviderNpi, string ProviderTin, string? AttendingNpi,
        string? PreauthNumber, string DiagnosisList, CcLine[] Lines);

    public static void MapPayerC(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/payer-c/api").WithTags("Payer C (Cascade)")
            .AddEndpointFilter(async (ctx, next) =>
            {
                var scenario = ctx.HttpContext.RequestServices.GetRequiredService<SimScenario>();
                if (scenario.PayerCOutage) return Results.StatusCode(503);
                var roll = Random.Shared.NextDouble();
                if (roll < scenario.PayerCTimeoutRate)
                {
                    await Task.Delay(TimeSpan.FromSeconds(6), ctx.HttpContext.RequestAborted);
                    return Results.StatusCode(504);
                }
                if (roll < scenario.PayerCTimeoutRate + scenario.PayerCFailureRate) return Results.StatusCode(500);
                await Task.Delay(Random.Shared.Next(150, 900), ctx.HttpContext.RequestAborted);
                return await next(ctx);
            });

        group.MapPost("/submit", (CcSubmit body, ClaimStore store, MemberDirectory members, SimScheduler scheduler,
            AdjudicationFlow flow, RemittanceWriter remits, SimScenario scenario) =>
        {
            if (members.Find(body.MemberNumber) is null)
                return Results.Ok(new { result = "ERROR", messages = new[] { "MEMBER NOT ON FILE" } });

            // Cascade resends are common; treat the same ref and type as idempotent
            var existing = store.Find(SeedIds.PayerC, body.ExternalRef);
            if (existing is not null && body.ClaimType == "ORIGINAL")
                return Results.Ok(new { result = "OK", @ref = existing.PayerClaimId, messages = Array.Empty<string>() });

            var claim = new SimClaim(SeedIds.PayerC, body.ExternalRef,
                body.ClaimType switch { "REPLACEMENT" => "7", "VOID" => "8", _ => "1" }, body.ReplacesRef, body.MemberNumber,
                DateOnly.ParseExact(body.BirthDate, "MM/dd/yyyy", CultureInfo.InvariantCulture), body.ProviderNpi, "PROVIDER",
                body.PreauthNumber, body.DiagnosisList.Split(';', StringSplitOptions.RemoveEmptyEntries),
                body.Lines.Select((l, i) => new SimLine(i + 1, l.Cpt, l.ModifierList.Split(',', StringSplitOptions.RemoveEmptyEntries), l.Units,
                    decimal.Parse(l.Charge, CultureInfo.InvariantCulture),
                    DateOnly.ParseExact(l.ServiceDate, "MM/dd/yyyy", CultureInfo.InvariantCulture), l.Pos)).ToList());
            var record = store.Add(claim, AdjudicationFlow.Prefix(SeedIds.PayerC));

            scheduler.Schedule(TimeSpan.FromSeconds(scenario.AdjudicationDelaySeconds), ct =>
                remits.WriteAsync(SeedIds.PayerC, claim.BillingNpi, "Provider", flow.Adjudicate(record), ct));
            return Results.Ok(new { result = "OK", @ref = record.PayerClaimId, messages = Array.Empty<string>() });
        });

        group.MapGet("/eligibility", (string member, string dob, string dos, MemberDirectory members) =>
        {
            var m = members.Find(member);
            if (m is null || m.PayerId != SeedIds.PayerC || m.DateOfBirth.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture) != dob)
                return Results.Ok(new { coverage = "NOT_FOUND" });
            var on = DateOnly.ParseExact(dos, "MM/dd/yyyy", CultureInfo.InvariantCulture);
            return Results.Ok(new
            {
                coverage = members.IsCovered(m, on) ? "ACTIVE" : "TERMED", product = m.Plan,
                from = m.CoverageStart.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
                thru = m.CoverageEnd?.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
                copay = m.Copay.ToString(CultureInfo.InvariantCulture),
                deductibleBalance = Math.Max(0, m.Deductible - m.DeductibleMet).ToString(CultureInfo.InvariantCulture)
            });
        });

        group.MapGet("/status/{claimRef}", (string claimRef, ClaimStore store) =>
        {
            var r = store.Find(SeedIds.PayerC, claimRef);
            if (r is null) return Results.Ok(new { state = "UNKNOWN" });
            var state = r.State switch
            {
                SimClaimState.Received or SimClaimState.InReview => "WORKING",
                SimClaimState.Pended => "ON_HOLD",
                SimClaimState.Rejected => "BOUNCED",
                _ => r.Outcome switch { "denied" => "DECLINED", "partial" => "PARTPAID", _ => "PAID" }
            };
            return Results.Ok(new { state, detail = r.Note, @ref = r.PayerClaimId });
        });
    }
}

public static class SimControlEndpoints
{
    public static void MapSimControl(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/sim").WithTags("Simulator control");
        group.MapGet("/scenarios", (SimScenario s) => s);
        group.MapPost("/scenarios", (SimScenario update, SimScenario s) =>
        {
            s.PayerCOutage = update.PayerCOutage;
            s.PayerCFailureRate = update.PayerCFailureRate;
            s.PayerCTimeoutRate = update.PayerCTimeoutRate;
            s.AdjudicationDelaySeconds = update.AdjudicationDelaySeconds;
            s.PayerBAckDelaySeconds = update.PayerBAckDelaySeconds;
            s.SilentPayerA = update.SilentPayerA;
            s.PendNext = update.PendNext;
            s.Reject999Next = update.Reject999Next;
            s.RandomDenialRate = update.RandomDenialRate;
            return Results.Ok(s);
        });
        group.MapPost("/payer-c/outage", (bool on, SimScenario s) =>
        {
            s.PayerCOutage = on;
            return Results.Ok(new { s.PayerCOutage });
        });
        group.MapGet("/claims", (ClaimStore store) => store.All.Select(r => new
        {
            r.Claim.PayerId, r.Claim.ClaimNumber, r.PayerClaimId, State = r.State.ToString(), r.Outcome, r.Paid, r.Note
        }));
        group.MapGet("/members/{memberId}", (string memberId, MemberDirectory members) =>
            members.Find(memberId) is { } m ? Results.Ok(m) : Results.NotFound());
        group.MapPost("/members", (SimMember member, MemberDirectory members) =>
        {
            members.Add(member);
            return Results.Created($"/sim/members/{member.MemberId}", member);
        });
    }
}
