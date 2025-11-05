using ClaimBridge.Claims.Application.Clients;
using ClaimBridge.Claims.Data;
using ClaimBridge.Claims.Domain;
using ClaimBridge.Contracts.Events;
using ClaimBridge.Contracts.Reference;
using ClaimBridge.ServiceDefaults.Messaging;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Claims.Application;

public sealed record IntakeProblem(int Status, string Title, string Detail);

public sealed record IntakeOutcome(Claim? Claim, IntakeProblem? Problem)
{
    public static IntakeOutcome Fail(int status, string title, string detail) => new(null, new IntakeProblem(status, title, detail));
}

public class ClaimIntakeService(
    ClaimsDb db,
    ClaimPhi phi,
    ProvidersClient providers,
    ClaimTransitions transitions,
    DuplicateDetector duplicates,
    Denials.DenialWorklist worklist,
    IEventOutbox outbox,
    ClaimsMetrics metrics,
    TimeProvider clock)
{
    public async Task<IntakeOutcome> AcceptAsync(ClaimRequest request, Guid orgId, ClaimSource source, string actor,
        Guid? batchId, CancellationToken ct)
    {
        if (PayerDirectory.Find(request.PayerId) is null)
            return IntakeOutcome.Fail(422, "Unknown payer", $"Payer {request.PayerId} is not connected to claim-bridge.");
        if (!await providers.IsEnrolledAsync(orgId, request.PayerId, ct))
            return IntakeOutcome.Fail(422, "Not enrolled with payer",
                $"The organisation is not actively enrolled with {request.PayerId}. Request enrollment before submitting claims.");

        Claim? original = null;
        var replaces = request.FrequencyCode is "7" or "8";
        if (replaces)
        {
            original = await db.Claims.Include(c => c.History)
                .FirstOrDefaultAsync(c => c.OrgId == orgId && c.ClaimNumber == request.OriginalClaimNumber, ct);
            if (original is null)
                return IntakeOutcome.Fail(422, "Original claim not found",
                    $"No claim {request.OriginalClaimNumber} exists for this organisation.");
            var trigger = request.FrequencyCode == "7" ? LifecycleTrigger.Supersede : LifecycleTrigger.Void;
            if (!ClaimLifecycle.CanFire(original.Status, trigger))
                return IntakeOutcome.Fail(409, "Original claim cannot be corrected yet",
                    $"Claim {original.ClaimNumber} is {original.Status}; wait for the payer's decision before replacing or voiding it.");
            if (original.PayerId != request.PayerId)
                return IntakeOutcome.Fail(422, "Payer mismatch", "A corrected claim must go to the same payer as the original.");
        }

        var now = clock.GetUtcNow();
        var claim = new Claim
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ClaimNumber = await db.NextClaimNumberAsync(now, ct),
            Source = source,
            BatchId = batchId,
            SubmittedBy = actor,
            CreatedAt = now,
            Status = ClaimStatus.Received,
            StatusChangedAt = now
        };
        Apply(claim, request);
        if (claim.FrequencyCode == "1")
            claim.PossibleDuplicateOf = await duplicates.FindAsync(claim, ct);

        if (original is not null)
        {
            claim.PreviousClaimId = original.Id;
            claim.Version = original.Version + 1;
            original.SupersededByClaimId = claim.Id;
            transitions.Fire(original, claim.FrequencyCode == "7" ? LifecycleTrigger.Supersede : LifecycleTrigger.Void,
                source.ToString().ToLowerInvariant(),
                claim.FrequencyCode == "7" ? $"Replaced by corrected claim {claim.ClaimNumber}" : $"Voided by {claim.ClaimNumber}");
            await transitions.ProjectAsync(original, ct);
            await worklist.MarkAsync(original.Id, Data.DenialStatus.Corrected, ct, correctedBy: claim.Id);
        }

        db.Claims.Add(claim);
        db.Lifecycles.Add(new ClaimLifecycleState { ClaimId = claim.Id, UpdatedAt = now });
        var created = claim.Note("Created", now, source.ToString().ToLowerInvariant(), $"Received via {source}");
        transitions.PublishCreated(claim, created);
        outbox.Enqueue(Topics.ClaimReceived, claim.Id.ToString(),
            new ClaimReceived(claim.Id, claim.ClaimNumber, orgId, claim.PayerId, claim.Version, source.ToString()));
        await transitions.ProjectAsync(claim, ct);

        await db.SaveChangesAsync(ct);
        metrics.Received(source, claim.PayerId);
        return new IntakeOutcome(claim, null);
    }

    /// <summary>Fixing a claim that failed the scrub; it goes back through the scrub step.</summary>
    public async Task<IntakeOutcome> UpdateAsync(Claim claim, ClaimRequest request, string actor, CancellationToken ct)
    {
        if (!ClaimLifecycle.CanFire(claim.Status, LifecycleTrigger.Edited))
            return IntakeOutcome.Fail(409, "Claim cannot be edited",
                $"Claim is {claim.Status}. Only claims that failed the scrub can be edited; submitted claims need a corrected claim.");
        if (request.PayerId != claim.PayerId && !await providers.IsEnrolledAsync(claim.OrgId, request.PayerId, ct))
            return IntakeOutcome.Fail(422, "Not enrolled with payer", $"The organisation is not actively enrolled with {request.PayerId}.");

        Apply(claim, request with { FrequencyCode = claim.FrequencyCode, OriginalClaimNumber = claim.OriginalClaimNumber });
        claim.PossibleDuplicateOf = claim.FrequencyCode == "1" ? await duplicates.FindAsync(claim, ct) : null;
        transitions.Fire(claim, LifecycleTrigger.Edited, "portal", $"Edited by {actor}");
        await transitions.ProjectAsync(claim, ct);
        outbox.Enqueue(Topics.ClaimReceived, claim.Id.ToString(),
            new ClaimReceived(claim.Id, claim.ClaimNumber, claim.OrgId, claim.PayerId, claim.Version, "Edit"));
        await db.SaveChangesAsync(ct);
        return new IntakeOutcome(claim, null);
    }

    private void Apply(Claim claim, ClaimRequest r)
    {
        claim.PayerId = r.PayerId;
        claim.BillingNpi = r.BillingNpi;
        claim.RenderingNpi = string.IsNullOrWhiteSpace(r.RenderingNpi) ? null : r.RenderingNpi;
        claim.PriorAuthNumber = string.IsNullOrWhiteSpace(r.PriorAuthNumber) ? null : r.PriorAuthNumber.Trim();
        claim.FrequencyCode = r.FrequencyCode;
        claim.OriginalClaimNumber = r.OriginalClaimNumber;
        claim.Diagnoses = r.Diagnoses.Select(d => d.Trim().ToUpperInvariant()).ToList();
        claim.Lines = r.Lines.Select((l, i) => new ServiceLine
        {
            LineNumber = i + 1,
            ProcedureCode = l.ProcedureCode.Trim().ToUpperInvariant(),
            Modifiers = (l.Modifiers ?? []).Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim().ToUpperInvariant()).ToList(),
            Units = l.Units,
            Charge = l.Charge,
            ServiceDate = l.ServiceDate,
            PlaceOfService = l.PlaceOfService,
            DxPointers = [.. l.DxPointers]
        }).ToList();
        claim.RecalculateTotal();
        phi.Protect(claim, r.Patient);
        claim.Fingerprint = ClaimFingerprint.Compute(claim.OrgId, claim.MemberIdHash, claim.BillingNpi, claim.PayerId, claim.Lines);
    }
}
