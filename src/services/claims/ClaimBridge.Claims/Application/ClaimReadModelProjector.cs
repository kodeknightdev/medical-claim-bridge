using ClaimBridge.Claims.Data;
using ClaimBridge.Claims.Domain;

namespace ClaimBridge.Claims.Application;

public class ClaimReadModelProjector(ClaimsDb db)
{
    public async Task ProjectAsync(Claim claim, CancellationToken ct)
    {
        var entry = await db.SearchEntries.FindAsync([claim.Id], ct);
        if (entry is null)
        {
            entry = new ClaimSearchEntry { ClaimId = claim.Id };
            db.SearchEntries.Add(entry);
        }

        entry.ClaimNumber = claim.ClaimNumber;
        entry.OrgId = claim.OrgId;
        entry.PayerId = claim.PayerId;
        entry.Status = claim.Status.ToString();
        entry.FrequencyCode = claim.FrequencyCode;
        entry.Version = claim.Version;
        entry.MemberIdLast4 = claim.MemberIdLast4;
        entry.MemberIdHash = claim.MemberIdHash;
        entry.ProcedureCodes = string.Join(',', claim.Lines.Select(l => l.ProcedureCode).Distinct());
        entry.TotalCharge = claim.TotalCharge;
        entry.PaidAmount = claim.PaidAmount;
        entry.Balance = claim.TotalCharge - (claim.PaidAmount ?? 0) - (claim.AdjustmentAmount ?? 0) - (claim.PatientResponsibility ?? 0);
        entry.FirstServiceDate = claim.Lines.Count == 0 ? DateOnly.MinValue : claim.FirstServiceDate;
        entry.CreatedAt = claim.CreatedAt;
        entry.StatusChangedAt = claim.StatusChangedAt;
        entry.SubmittedAt = claim.FirstSubmittedAt;
        entry.DenialRisk = claim.DenialRisk;
        entry.DenialCategory = claim.DenialCategory;
        entry.Source = claim.Source.ToString();
        entry.HasOpenEdits = claim.Status == ClaimStatus.ScrubFailed;
    }
}
