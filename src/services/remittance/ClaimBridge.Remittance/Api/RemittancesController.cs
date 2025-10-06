using ClaimBridge.Remittance.Data;
using ClaimBridge.ServiceDefaults.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Remittance.Api;

[ApiController]
[Route("api/remittances")]
[Authorize(Policy = Policies.ClaimsRead)]
public class RemittancesController(RemittanceDb db) : ControllerBase
{
    public sealed record FileSummary(Guid Id, string PayerId, string? EftTraceNumber, DateOnly? PaymentDate, decimal TotalPaid,
        int ClaimCount, int MatchedCount, int UnmatchedCount, string Status, DateTimeOffset ReceivedAt);

    public sealed record AdjustmentDto(string GroupCode, string ReasonCode, decimal Amount, string? Explanation);

    public sealed record PostingDto(Guid Id, Guid ClaimId, string ClaimNumber, string Kind, string ClpStatus, string? PayerClaimId,
        decimal Charge, decimal Paid, decimal Allowed, decimal PatientResponsibility, decimal ContractualAdjustments,
        decimal OtherAdjustments, string? DenialCarc, string? DenialCategory, IReadOnlyList<string> RemarkCodes,
        IReadOnlyList<AdjustmentDto> Adjustments, DateTimeOffset PostedAt);

    public sealed record FileDetail(FileSummary File, IReadOnlyList<PostingDto> Postings, IReadOnlyDictionary<string, string> RemarkExplanations);

    [HttpGet]
    public async Task<IEnumerable<FileSummary>> List([FromQuery] string? payerId, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var query = db.Files.AsNoTracking();
        if (!string.IsNullOrEmpty(payerId)) query = query.Where(f => f.PayerId == payerId);

        var org = User.OrgFilter();
        if (org is { } o)
        {
            // provider users only see the files that paid their claims, with their own totals
            var mine = await db.Postings.AsNoTracking().Where(p => p.OrgId == o)
                .GroupBy(p => p.RemittanceFileId)
                .Select(g => new { FileId = g.Key, Paid = g.Sum(p => p.Paid), Count = g.Count() })
                .ToListAsync(ct);
            var ids = mine.Select(m => m.FileId).ToList();
            var files = await query.Where(f => ids.Contains(f.Id)).OrderByDescending(f => f.ReceivedAt).Take(take).ToListAsync(ct);
            return files.Select(f =>
            {
                var m = mine.First(x => x.FileId == f.Id);
                return new FileSummary(f.Id, f.PayerId, f.EftTraceNumber, f.PaymentDate, m.Paid, m.Count, m.Count, 0, f.Status, f.ReceivedAt);
            });
        }

        return await query.OrderByDescending(f => f.ReceivedAt).Take(Math.Clamp(take, 1, 200))
            .Select(f => new FileSummary(f.Id, f.PayerId, f.EftTraceNumber, f.PaymentDate, f.TotalPaid, f.ClaimCount, f.MatchedCount,
                f.UnmatchedCount, f.Status, f.ReceivedAt))
            .ToListAsync(ct);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FileDetail>> Get(Guid id, CancellationToken ct)
    {
        var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null) return NotFound();

        var query = db.Postings.AsNoTracking().Include(p => p.Adjustments).Where(p => p.RemittanceFileId == id);
        if (User.OrgFilter() is { } o) query = query.Where(p => p.OrgId == o);
        var postings = await query.OrderBy(p => p.ClaimNumber).ToListAsync(ct);
        if (postings.Count == 0 && !User.IsStaff()) return NotFound();

        var codes = await ReasonLookupAsync(ct);
        var remarks = postings.SelectMany(p => p.RemarkCodes).Distinct()
            .ToDictionary(r => r, r => codes.GetValueOrDefault(("RARC", r)) ?? "");
        return new FileDetail(
            new FileSummary(file.Id, file.PayerId, file.EftTraceNumber, file.PaymentDate,
                User.IsStaff() ? file.TotalPaid : postings.Sum(p => p.Paid), file.ClaimCount, file.MatchedCount, file.UnmatchedCount,
                file.Status, file.ReceivedAt),
            postings.Select(p => ToDto(p, codes)).ToList(), remarks);
    }

    [HttpGet("by-claim/{claimId:guid}")]
    public async Task<ActionResult<IEnumerable<PostingDto>>> ByClaim(Guid claimId, CancellationToken ct)
    {
        var postings = await db.Postings.AsNoTracking().Include(p => p.Adjustments).Where(p => p.ClaimId == claimId)
            .OrderBy(p => p.PostedAt).ToListAsync(ct);
        if (postings.Count > 0 && !User.CanAccessOrg(postings[0].OrgId)) return NotFound();
        var codes = await ReasonLookupAsync(ct);
        return postings.Select(p => ToDto(p, codes)).ToList();
    }

    [HttpGet("~/api/reason-codes")]
    public async Task<IEnumerable<ReasonCode>> ReasonCodes(CancellationToken ct) =>
        await db.ReasonCodes.AsNoTracking().OrderBy(r => r.Kind).ThenBy(r => r.Code).ToListAsync(ct);

    internal async Task<Dictionary<(string Kind, string Code), string>> ReasonLookupAsync(CancellationToken ct) =>
        (await db.ReasonCodes.AsNoTracking().ToListAsync(ct)).ToDictionary(r => (r.Kind, r.Code), r => r.Explanation);

    internal static PostingDto ToDto(Posting p, Dictionary<(string Kind, string Code), string> codes) => new(
        p.Id, p.ClaimId, p.ClaimNumber, p.Kind, p.ClpStatus, p.PayerClaimId, p.Charge, p.Paid, p.Allowed, p.PatientResponsibility,
        p.ContractualAdjustments, p.OtherAdjustments, p.DenialCarc, p.DenialCategory, p.RemarkCodes,
        p.Adjustments.Select(a => new AdjustmentDto(a.GroupCode, a.ReasonCode, a.Amount, codes.GetValueOrDefault(("CARC", a.ReasonCode)))).ToList(),
        p.PostedAt);
}
