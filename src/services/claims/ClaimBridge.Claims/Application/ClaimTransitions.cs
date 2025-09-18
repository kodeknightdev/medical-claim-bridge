using ClaimBridge.Claims.Domain;
using ClaimBridge.Contracts.Events;
using ClaimBridge.ServiceDefaults.Messaging;

namespace ClaimBridge.Claims.Application;

/// <summary>Fires lifecycle triggers and stages the matching claims.status-changed event in the same transaction.</summary>
public class ClaimTransitions(IEventOutbox outbox, TimeProvider clock)
{
    public ClaimStatusEvent Fire(Claim claim, LifecycleTrigger trigger, string source, string? reason = null, string? detail = null)
    {
        var entry = claim.Fire(trigger, clock.GetUtcNow(), source, reason, detail);
        Publish(claim, entry);
        return entry;
    }

    public bool TryFire(Claim claim, LifecycleTrigger trigger, string source, string? reason = null, string? detail = null)
    {
        if (!ClaimLifecycle.CanFire(claim.Status, trigger)) return false;
        Fire(claim, trigger, source, reason, detail);
        return true;
    }

    public void PublishCreated(Claim claim, ClaimStatusEvent entry) => Publish(claim, entry);

    private void Publish(Claim claim, ClaimStatusEvent entry) =>
        outbox.Enqueue(Topics.ClaimStatusChanged, claim.Id.ToString(), new ClaimStatusChanged(
            claim.Id, claim.ClaimNumber, claim.OrgId, claim.PayerId,
            entry.FromStatus.ToString(), entry.ToStatus.ToString(), entry.Trigger, entry.Reason,
            claim.TotalCharge, claim.PaidAmount, claim.PatientResponsibility, claim.DenialCategory, claim.DenialCarc,
            claim.FirstSubmittedAt, claim.Lines.Count == 0 ? null : claim.FirstServiceDate, claim.FrequencyCode != "1"));
}
