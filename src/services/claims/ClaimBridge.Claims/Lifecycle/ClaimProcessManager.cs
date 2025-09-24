using ClaimBridge.Claims.Application;
using ClaimBridge.Claims.Data;
using ClaimBridge.Claims.Domain;
using ClaimBridge.Contracts.Events;
using ClaimBridge.ServiceDefaults.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClaimBridge.Claims.Lifecycle;

/// <summary>
/// Orchestrates the claim lifecycle from scrub result to closure. Payer events can arrive out of order
/// (an ack before the submitted event, a 277 before the 999), so each handler first catches the claim up.
/// </summary>
public class ClaimProcessManager(
    ClaimsDb db,
    ClaimTransitions transitions,
    IEventOutbox outbox,
    IOptions<LifecycleOptions> options,
    TimeProvider clock,
    ILogger<ClaimProcessManager> logger) : IEventHandler
{
    private const string Source = "process-manager";

    public async Task HandleAsync(EventEnvelope envelope, string topic, CancellationToken ct)
    {
        var claimId = envelope.Data.GetProperty("claimId").GetGuid();
        var claim = await db.Claims.Include(c => c.History).FirstOrDefaultAsync(c => c.Id == claimId, ct);
        if (claim is null)
        {
            logger.LogWarning("{EventType} for unknown claim {ClaimId}", envelope.Type, claimId);
            return;
        }
        var state = await db.Lifecycles.FindAsync([claimId], ct)
                    ?? db.Lifecycles.Add(new ClaimLifecycleState { ClaimId = claimId }).Entity;

        if (ClaimLifecycle.Terminal.Contains(claim.Status) && envelope.Type != ReversalPosted.EventType)
        {
            claim.Note("IgnoredLateEvent", clock.GetUtcNow(), Source, $"{envelope.Type} after claim was {claim.Status}");
            return;
        }

        switch (envelope.Type)
        {
            case var t when t == ClaimScrubbed.EventType: OnScrubbed(claim, state, envelope.DataAs<ClaimScrubbed>()); break;
            case var t when t == ClaimSubmitted.EventType: OnSubmitted(claim, state, envelope.DataAs<ClaimSubmitted>()); break;
            case var t when t == AckReceived.EventType: OnAck(claim, state, envelope.DataAs<AckReceived>()); break;
            case var t when t == PayerStatusUpdated.EventType: OnPayerStatus(claim, state, envelope.DataAs<PayerStatusUpdated>()); break;
            case var t when t == PaymentPosted.EventType: OnPaymentPosted(claim, state, envelope.DataAs<PaymentPosted>()); break;
            case var t when t == ClaimDenied.EventType: OnDenied(claim, state, envelope.DataAs<ClaimDenied>()); break;
            case var t when t == ReversalPosted.EventType: OnReversal(claim, envelope.DataAs<ReversalPosted>()); break;
            default:
                logger.LogDebug("Process manager ignores {EventType}", envelope.Type);
                return;
        }
        await transitions.ProjectAsync(claim, ct);
    }

    private void OnScrubbed(Claim claim, ClaimLifecycleState state, ClaimScrubbed e)
    {
        if (claim.Status != ClaimStatus.Scrubbing) return;
        if (e.Passed)
        {
            transitions.Fire(claim, LifecycleTrigger.ScrubPassed, Source,
                e.Edits.Count == 0 ? "Clean claim" : $"{e.Edits.Count} warning(s)");
            outbox.Enqueue(Topics.ClaimReady, claim.Id.ToString(),
                new ClaimReady(claim.Id, claim.ClaimNumber, claim.OrgId, claim.PayerId, state.Resubmissions + 1));
        }
        else
        {
            var errors = e.Edits.Where(x => x.Severity == "Error").Select(x => x.RuleCode).Distinct();
            transitions.Fire(claim, LifecycleTrigger.ScrubFailed, Source, "Scrub errors: " + string.Join(", ", errors));
        }
    }

    private void OnSubmitted(Claim claim, ClaimLifecycleState state, ClaimSubmitted e)
    {
        if (claim.Status is not (ClaimStatus.ReadyToSubmit or ClaimStatus.Submitted or ClaimStatus.Escalated))
        {
            claim.Note("Submitted", e.SubmittedAt, Source, $"Attempt {e.Attempt} confirmed after the payer already responded");
            return;
        }

        var via = e.FileName is null ? e.Channel : $"{e.Channel} file {e.FileName}";
        transitions.Fire(claim, LifecycleTrigger.Submit, Source, $"Sent to {e.PayerId} via {via} (attempt {e.Attempt})");
        claim.FirstSubmittedAt ??= e.SubmittedAt;
        claim.LastSubmittedAt = e.SubmittedAt;

        var now = clock.GetUtcNow();
        state.SetDeadline(e.Attempt > 1 ? DeadlineKind.Resubmission : DeadlineKind.Acknowledgement,
            now + options.Value.AckSla(e.PayerId), now);
    }

    private void OnAck(Claim claim, ClaimLifecycleState state, AckReceived e)
    {
        CatchUpToSubmitted(claim, e.ReceivedAt);
        state.ClearDeadline(clock.GetUtcNow());

        if (e.Accepted)
        {
            if (transitions.TryFire(claim, LifecycleTrigger.PayerAccepted, Source, $"Accepted by payer ({e.AckType})"))
                claim.PayerClaimId = e.PayerClaimId ?? claim.PayerClaimId;
        }
        else
        {
            transitions.TryFire(claim, LifecycleTrigger.PayerRejected, Source,
                $"Rejected ({e.AckType}): {string.Join("; ", e.RejectionReasons)}");
        }
    }

    private void OnPayerStatus(Claim claim, ClaimLifecycleState state, PayerStatusUpdated e)
    {
        CatchUpToSubmitted(claim, e.At);
        state.ClearDeadline(clock.GetUtcNow());
        var note = e.Description ?? e.StatusCode;

        switch (e.Status)
        {
            case "pended":
                if (!transitions.TryFire(claim, LifecycleTrigger.PayerPended, Source, note))
                    claim.Note("Pended", e.At, e.Source, note);
                break;
            case "in-process":
                EnsureAccepted(claim, "Payer reports the claim is in process");
                claim.Note("InProcess", e.At, e.Source, note);
                break;
            case "finalized":
                EnsureAccepted(claim, "Payer finalized the claim");
                var trigger = e.Outcome switch
                {
                    "paid" => LifecycleTrigger.AdjudicatedPaid,
                    "partial" => LifecycleTrigger.AdjudicatedPartial,
                    "denied" => LifecycleTrigger.AdjudicatedDenied,
                    _ => (LifecycleTrigger?)null
                };
                if (trigger is { } tr && !transitions.TryFire(claim, tr, Source, note))
                    claim.Note("Finalized", e.At, e.Source, note);
                break;
            default:
                claim.Note("PayerStatus", e.At, e.Source, $"{e.Status}: {note}");
                break;
        }
    }

    private void OnPaymentPosted(Claim claim, ClaimLifecycleState state, PaymentPosted e)
    {
        CatchUpToSubmitted(claim, clock.GetUtcNow());
        state.ClearDeadline(clock.GetUtcNow());
        if (claim.Status == ClaimStatus.Appealed)
            transitions.Fire(claim, LifecycleTrigger.AppealWon, Source, "Payment received after appeal");
        EnsureAccepted(claim, "Remittance received");

        if (claim.Status is ClaimStatus.AcceptedByPayer or ClaimStatus.Pended)
            transitions.Fire(claim, e.PartiallyPaid ? LifecycleTrigger.AdjudicatedPartial : LifecycleTrigger.AdjudicatedPaid, Source,
                e.PartiallyPaid ? "Partially paid" : "Paid in full");

        claim.PaidAmount = e.PaidAmount;
        claim.AllowedAmount = e.AllowedAmount;
        claim.PatientResponsibility = e.PatientResponsibility;
        claim.AdjustmentAmount = e.Adjustments.Where(a => a.GroupCode != "PR").Sum(a => a.Amount);

        if (transitions.TryFire(claim, LifecycleTrigger.RemittancePosted, "remittance",
                $"Paid {e.PaidAmount:0.00}, patient owes {e.PatientResponsibility:0.00}, EFT {e.EftTraceNumber}"))
        {
            var balance = claim.TotalCharge - e.PaidAmount - claim.AdjustmentAmount - e.PatientResponsibility;
            if (balance == 0)
                transitions.Fire(claim, LifecycleTrigger.Close, Source, "Payer balance resolved");
        }
    }

    private void OnDenied(Claim claim, ClaimLifecycleState state, ClaimDenied e)
    {
        CatchUpToSubmitted(claim, clock.GetUtcNow());
        state.ClearDeadline(clock.GetUtcNow());
        EnsureAccepted(claim, "Remittance received");

        claim.DenialCategory = e.Category;
        claim.DenialCarc = e.CarcCode;
        claim.PaidAmount = 0;
        if (claim.Status != ClaimStatus.Denied)
            transitions.TryFire(claim, LifecycleTrigger.AdjudicatedDenied, "remittance", $"Denied CARC {e.CarcCode} ({e.Category})");
        else
            claim.Note("DenialPosted", clock.GetUtcNow(), "remittance", $"CARC {e.CarcCode}");
    }

    private void OnReversal(Claim claim, ReversalPosted e)
    {
        claim.PaidAmount = Math.Max(0, (claim.PaidAmount ?? 0) - e.ReversedAmount);
        if (!transitions.TryFire(claim, LifecycleTrigger.ReversalPosted, "remittance", $"Payer took back {e.ReversedAmount:0.00}"))
            claim.Note("Reversal", clock.GetUtcNow(), "remittance", $"Takeback of {e.ReversedAmount:0.00} while {claim.Status}");
    }

    private void CatchUpToSubmitted(Claim claim, DateTimeOffset at)
    {
        if (claim.Status != ClaimStatus.ReadyToSubmit) return;
        transitions.Fire(claim, LifecycleTrigger.Submit, Source, "Implied by a payer response that arrived first");
        claim.FirstSubmittedAt ??= at;
    }

    private void EnsureAccepted(Claim claim, string why)
    {
        if (claim.Status is ClaimStatus.Submitted or ClaimStatus.Escalated)
            transitions.Fire(claim, LifecycleTrigger.PayerAccepted, Source, why);
    }
}
