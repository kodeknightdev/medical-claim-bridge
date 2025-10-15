using ClaimBridge.Claims.Application;
using ClaimBridge.Claims.Data;
using ClaimBridge.Claims.Domain;
using ClaimBridge.Contracts.ReferenceData;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Claims.Seeding;

/// <summary>
/// Replays the synthetic history through the real state machine (with historical timestamps) so imported claims
/// have the same status trail as live ones. Nothing is published to Kafka; each service imports its own slice.
/// </summary>
public class HistoryImporter(ClaimsDb db, ClaimPhi phi, ClaimReadModelProjector projector, ILogger<HistoryImporter> logger)
{
    private const string Source = "seed";

    public async Task<int> ImportAsync(IReadOnlyList<HistoryRecord> records, CancellationToken ct)
    {
        var existing = (await db.Claims.Select(c => c.ClaimNumber).ToListAsync(ct)).ToHashSet();
        var imported = 0;
        db.ChangeTracker.AutoDetectChangesEnabled = false;

        foreach (var chunk in records.Where(r => !existing.Contains(r.ClaimNumber)).Chunk(250))
        {
            foreach (var r in chunk)
            {
                var claim = Build(r);
                db.Claims.Add(claim);
                db.Lifecycles.Add(new ClaimLifecycleState { ClaimId = claim.Id, UpdatedAt = claim.StatusChangedAt });
                await projector.ProjectAsync(claim, ct);
                AddDenialAndAppeal(r, claim);
                imported++;
            }
            db.ChangeTracker.DetectChanges();
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        db.ChangeTracker.AutoDetectChangesEnabled = true;
        logger.LogInformation("Imported {Count} historical claims", imported);
        return imported;
    }

    private Claim Build(HistoryRecord r)
    {
        var claim = new Claim
        {
            Id = r.ClaimId, ClaimNumber = r.ClaimNumber, OrgId = r.OrgId, PayerId = r.PayerId, BillingNpi = r.BillingNpi,
            RenderingNpi = r.RenderingNpi, PriorAuthNumber = r.PriorAuthNumber, FrequencyCode = r.FrequencyCode,
            Diagnoses = r.Diagnoses, Source = ClaimSource.Batch, SubmittedBy = "seed", CreatedAt = r.CreatedAt,
            Status = ClaimStatus.Received, StatusChangedAt = r.CreatedAt, ScrubAttempts = r.Scrub.FirstPassClean ? 1 : 2,
            Lines = r.Lines.Select((l, i) => new ServiceLine
            {
                LineNumber = i + 1, ProcedureCode = l.ProcedureCode, Modifiers = l.Modifiers, Units = l.Units, Charge = l.Charge,
                ServiceDate = l.ServiceDate, PlaceOfService = l.PlaceOfService, DxPointers = l.DxPointers
            }).ToList()
        };
        claim.RecalculateTotal();
        phi.Protect(claim, new PatientRequest(r.MemberId, r.Patient.FirstName, r.Patient.LastName, r.Patient.DateOfBirth, r.Patient.Gender));
        claim.Fingerprint = ClaimFingerprint.Compute(claim.OrgId, claim.MemberIdHash, claim.BillingNpi, claim.PayerId, claim.Lines);
        claim.Note("Created", r.CreatedAt, Source, "Imported from synthetic history");

        var t = r.CreatedAt.AddMinutes(1);
        claim.Fire(LifecycleTrigger.StartScrub, t, Source);
        if (!r.Scrub.FirstPassClean)
        {
            claim.Fire(LifecycleTrigger.ScrubFailed, t.AddSeconds(2), Source, "Scrub errors: " + string.Join(", ", r.Scrub.Edits.Select(e => e.RuleCode)));
            claim.Fire(LifecycleTrigger.Edited, t.AddMinutes(20), Source, "Corrected by biller");
            claim.Fire(LifecycleTrigger.StartScrub, t.AddMinutes(21), Source);
        }
        claim.Fire(LifecycleTrigger.ScrubPassed, r.SubmittedAt.AddMinutes(-2), Source);
        claim.Fire(LifecycleTrigger.Submit, r.SubmittedAt, Source, $"Sent to {r.PayerId}");
        claim.FirstSubmittedAt = claim.LastSubmittedAt = r.SubmittedAt;
        if (!r.AcceptedFirstPass)
        {
            claim.Note("Resubmitted", r.SubmittedAt.AddHours(1), Source, "Front-end rejection fixed and resent");
        }
        claim.Fire(LifecycleTrigger.PayerAccepted, r.SubmittedAt.AddHours(2), Source, "Accepted by payer");
        claim.PayerClaimId = $"H{r.ClaimNumber[^7..]}";

        if (r.AdjudicatedAt is not { } decided) return claim;

        var adjustments = r.Adjustments.Where(a => a.Group != "PR").Sum(a => a.Amount);
        if (r.DeniedInitially == true)
        {
            claim.DenialCarc = r.Carc;
            claim.DenialCategory = r.Category ?? AdjustmentReasons.CategoryFor(r.Carc ?? "16");
            claim.PaidAmount = 0;
            claim.Fire(LifecycleTrigger.AdjudicatedDenied, decided, Source, $"Denied CARC {r.Carc}");
            if (r.Appeal is { } appeal)
            {
                claim.Fire(LifecycleTrigger.Appeal, appeal.SubmittedAt, Source, "Appeal sent");
                if (appeal.Status == "Won")
                {
                    claim.Fire(LifecycleTrigger.AppealWon, appeal.SubmittedAt.AddDays(20), Source, "Appeal won");
                    claim.Fire(LifecycleTrigger.AdjudicatedPaid, appeal.SubmittedAt.AddDays(25), Source, "Paid after appeal");
                    claim.PaidAmount = r.PaidAmount;
                    claim.Fire(LifecycleTrigger.RemittancePosted, appeal.SubmittedAt.AddDays(26), "remittance");
                    claim.Fire(LifecycleTrigger.Close, appeal.SubmittedAt.AddDays(26), Source);
                }
                else if (appeal.Status == "Lost")
                {
                    claim.Fire(LifecycleTrigger.AppealLost, appeal.SubmittedAt.AddDays(30), Source, "Appeal upheld the denial");
                }
            }
            return claim;
        }

        claim.PaidAmount = r.PaidAmount;
        claim.AllowedAmount = r.AllowedAmount;
        claim.PatientResponsibility = r.PatientResponsibility;
        claim.AdjustmentAmount = adjustments;
        var partial = r.Outcome == "partial";
        claim.Fire(partial ? LifecycleTrigger.AdjudicatedPartial : LifecycleTrigger.AdjudicatedPaid, decided, Source);
        claim.Fire(LifecycleTrigger.RemittancePosted, decided.AddHours(6), "remittance", $"EFT {r.EftTraceNumber}");
        if (!partial) claim.Fire(LifecycleTrigger.Close, decided.AddHours(6), Source, "Payer balance resolved");
        else
        {
            claim.DenialCarc = r.Carc;
            claim.DenialCategory = r.Category;
        }
        return claim;
    }

    private void AddDenialAndAppeal(HistoryRecord r, Claim claim)
    {
        if (r.AdjudicatedAt is not { } decided || (r.DeniedInitially != true && r.Outcome != "partial")) return;

        var window = r.PayerId switch { "PAYER-B" => 180, "PAYER-C" => 60, _ => 90 };
        var status = r.Appeal?.Status switch
        {
            "Won" => DenialStatus.Resolved,
            "Lost" => DenialStatus.WrittenOff,
            "Submitted" => DenialStatus.Appealed,
            _ => DenialStatus.Open
        };
        var appealId = r.Appeal is null ? (Guid?)null : Guid.NewGuid();
        db.DenialWorkItems.Add(new DenialWorkItem
        {
            ClaimId = claim.Id, ClaimNumber = claim.ClaimNumber, OrgId = claim.OrgId, PayerId = claim.PayerId,
            CarcCode = r.Carc ?? "16", Category = r.Category ?? "Other",
            DeniedAmount = r.DeniedInitially == true ? claim.TotalCharge : r.Adjustments.Where(a => a.Group == "CO" && a.Reason != "45").Sum(a => a.Amount),
            DeniedAt = decided, AppealDeadline = DateOnly.FromDateTime(decided.UtcDateTime).AddDays(window), Status = status,
            AppealId = appealId, UpdatedAt = decided
        });

        if (r.Appeal is { } appeal)
            db.Appeals.Add(new Appeal
            {
                Id = appealId!.Value, ClaimId = claim.Id, ClaimNumber = claim.ClaimNumber, OrgId = claim.OrgId, PayerId = claim.PayerId,
                CarcCode = r.Carc ?? "16", Argument = "Medical records attached show the service was documented and authorised.",
                Enclosures = ["Office note", "Authorisation confirmation"],
                Status = Enum.Parse<AppealStatus>(appeal.Status), Deadline = DateOnly.FromDateTime(decided.UtcDateTime).AddDays(window),
                CreatedBy = "seed", CreatedAt = appeal.SubmittedAt.AddDays(-1), SubmittedAt = appeal.SubmittedAt,
                OutcomeAt = appeal.Status is "Won" or "Lost" ? appeal.SubmittedAt.AddDays(25) : null
            });
    }
}
