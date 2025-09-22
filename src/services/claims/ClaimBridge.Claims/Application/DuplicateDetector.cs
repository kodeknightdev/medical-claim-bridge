using ClaimBridge.Claims.Data;
using ClaimBridge.Claims.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Claims.Application;

public class DuplicateDetector(ClaimsDb db)
{
    // a rejected or scrub-failed claim never reached adjudication, so billing it again is not a duplicate
    private static readonly ClaimStatus[] Ignored =
        [ClaimStatus.Superseded, ClaimStatus.Voided, ClaimStatus.ScrubFailed, ClaimStatus.RejectedByPayer];

    public Task<string?> FindAsync(Claim candidate, CancellationToken ct) =>
        db.Claims.AsNoTracking()
            .Where(c => c.OrgId == candidate.OrgId
                        && c.Fingerprint == candidate.Fingerprint
                        && c.Id != candidate.Id
                        && c.FrequencyCode == "1"
                        && !Ignored.Contains(c.Status))
            .OrderBy(c => c.CreatedAt)
            .Select(c => c.ClaimNumber)
            .FirstOrDefaultAsync(ct);
}
