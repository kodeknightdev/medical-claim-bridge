using ClaimBridge.Claims.Data;
using ClaimBridge.Claims.Denials;
using ClaimBridge.ServiceDefaults.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Claims.Api;

[ApiController]
[Route("api/denials")]
[Authorize(Policy = Policies.ClaimsRead)]
public class DenialsController(ClaimsDb db, TimeProvider clock) : ControllerBase
{
    public sealed record DenialItemDto(Guid ClaimId, string ClaimNumber, Guid OrgId, string PayerId, string CarcCode, string Category,
        decimal DeniedAmount, DateTimeOffset DeniedAt, DateOnly AppealDeadline, int DaysToDeadline, string AgeBucket, string Status,
        Guid? CorrectedByClaimId, Guid? AppealId);

    public sealed record DenialGroup(string Key, int Count, decimal Amount);

    public sealed record Worklist(IReadOnlyList<DenialItemDto> Items, IReadOnlyList<DenialGroup> ByCategory,
        IReadOnlyList<DenialGroup> ByPayer, IReadOnlyList<DenialGroup> ByAge);

    [HttpGet]
    public async Task<Worklist> Get([FromQuery] Guid? orgId, [FromQuery] string? status = "Open", [FromQuery] string? payerId = null,
        [FromQuery] string? category = null, CancellationToken ct = default)
    {
        var query = db.DenialWorkItems.AsNoTracking();
        if (User.OrgFilter(orgId) is { } org) query = query.Where(d => d.OrgId == org);
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<DenialStatus>(status, out var s)) query = query.Where(d => d.Status == s);
        if (!string.IsNullOrEmpty(payerId)) query = query.Where(d => d.PayerId == payerId);
        if (!string.IsNullOrEmpty(category)) query = query.Where(d => d.Category == category);

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var rows = await query.OrderBy(d => d.AppealDeadline).Take(500).ToListAsync(ct);
        var items = rows.Select(d => new DenialItemDto(d.ClaimId, d.ClaimNumber, d.OrgId, d.PayerId, d.CarcCode, d.Category,
            d.DeniedAmount, d.DeniedAt, d.AppealDeadline, d.AppealDeadline.DayNumber - today.DayNumber,
            DenialWorklist.AgeBucket(d.DeniedAt, now), d.Status.ToString(), d.CorrectedByClaimId, d.AppealId)).ToList();

        return new Worklist(items,
            Group(items, i => i.Category), Group(items, i => i.PayerId), Group(items, i => i.AgeBucket));
    }

    private static List<DenialGroup> Group(IEnumerable<DenialItemDto> items, Func<DenialItemDto, string> key) =>
        items.GroupBy(key).Select(g => new DenialGroup(g.Key, g.Count(), g.Sum(i => i.DeniedAmount)))
            .OrderByDescending(g => g.Amount).ToList();
}
