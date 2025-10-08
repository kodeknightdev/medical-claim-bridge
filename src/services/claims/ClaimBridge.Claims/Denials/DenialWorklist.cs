using ClaimBridge.Claims.Data;
using ClaimBridge.Claims.Domain;
using Microsoft.Extensions.Options;

namespace ClaimBridge.Claims.Denials;

public sealed class AppealOptions
{
    public Dictionary<string, int> WindowDays { get; set; } = new() { ["PAYER-A"] = 90, ["PAYER-B"] = 180, ["PAYER-C"] = 60 };
    public int DefaultWindowDays { get; set; } = 90;

    public int WindowFor(string payerId) => WindowDays.TryGetValue(payerId, out var d) ? d : DefaultWindowDays;
}

public class DenialWorklist(ClaimsDb db, IOptions<AppealOptions> options, TimeProvider clock)
{
    public async Task OpenAsync(Claim claim, string carc, string category, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var item = await db.DenialWorkItems.FindAsync([claim.Id], ct);
        if (item is null)
        {
            item = new DenialWorkItem { ClaimId = claim.Id };
            db.DenialWorkItems.Add(item);
        }
        item.ClaimNumber = claim.ClaimNumber;
        item.OrgId = claim.OrgId;
        item.PayerId = claim.PayerId;
        item.CarcCode = carc;
        item.Category = category;
        item.DeniedAmount = claim.TotalCharge - (claim.PaidAmount ?? 0);
        item.DeniedAt = now;
        item.AppealDeadline = DateOnly.FromDateTime(now.UtcDateTime).AddDays(options.Value.WindowFor(claim.PayerId));
        item.Status = DenialStatus.Open;
        item.UpdatedAt = now;
    }

    public async Task MarkAsync(Guid claimId, DenialStatus status, CancellationToken ct, Guid? correctedBy = null, Guid? appealId = null)
    {
        var item = await db.DenialWorkItems.FindAsync([claimId], ct);
        if (item is null) return;
        item.Status = status;
        item.CorrectedByClaimId = correctedBy ?? item.CorrectedByClaimId;
        item.AppealId = appealId ?? item.AppealId;
        item.UpdatedAt = clock.GetUtcNow();
    }

    public static string AgeBucket(DateTimeOffset deniedAt, DateTimeOffset now) => (now - deniedAt).TotalDays switch
    {
        <= 30 => "0-30",
        <= 60 => "31-60",
        <= 90 => "61-90",
        _ => "90+"
    };
}
