using System.Text.Json;
using ClaimBridge.Claims.Application;
using ClaimBridge.Claims.Application.Clients;
using ClaimBridge.Claims.Data;
using ClaimBridge.Claims.Domain;
using ClaimBridge.Contracts.Events;
using ClaimBridge.ServiceDefaults.Messaging;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Claims.Lifecycle;

/// <summary>Scrub step of the process manager: eligibility, final scrub over gRPC and the denial risk score.</summary>
public class ScrubStepHandler(
    ClaimsDb db,
    ClaimPhi phi,
    ClaimTransitions transitions,
    ScrubberClient scrubber,
    EligibilityClient eligibility,
    RiskClient risk,
    IEventOutbox outbox,
    TimeProvider clock) : IEventHandler
{
    public async Task HandleAsync(EventEnvelope envelope, string topic, CancellationToken ct)
    {
        var received = envelope.DataAs<ClaimReceived>();
        var claim = await db.Claims.Include(c => c.History).FirstOrDefaultAsync(c => c.Id == received.ClaimId, ct);
        if (claim is null || claim.Status != ClaimStatus.Received) return;

        transitions.Fire(claim, LifecycleTrigger.StartScrub, "process-manager");
        claim.ScrubAttempts++;

        var request = phi.ToRequest(claim);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var elig = await eligibility.CheckAsync(claim.PayerId, request.Patient.MemberId, request.Patient.DateOfBirth,
            claim.FirstServiceDate, ct);

        var score = await risk.ScoreAsync(claim.Id, claim.OrgId, request, today, ct);
        if (score is not null)
        {
            claim.DenialRisk = (decimal)Math.Round(score.Probability, 4);
            claim.DenialRiskModel = score.ModelVersion;
            claim.DenialRiskFactorsJson = JsonSerializer.Serialize(score.Factors, EventEnvelope.Json);
        }

        var result = await scrubber.ScrubAsync(claim.Id, claim.Version, claim.OrgId, request, elig, claim.PossibleDuplicateOf,
            today, persist: true, ct);

        outbox.Enqueue(Topics.ClaimScrubbed, claim.Id.ToString(), new ClaimScrubbed(
            claim.Id, claim.ClaimNumber, claim.OrgId, claim.PayerId, result.Passed,
            result.Edits.Select(e => new ScrubEditSummary(e.RuleCode, e.Severity, e.LineNumber)).ToList(),
            claim.DenialRisk, claim.ScrubAttempts == 1));
        await transitions.ProjectAsync(claim, ct);
    }
}
