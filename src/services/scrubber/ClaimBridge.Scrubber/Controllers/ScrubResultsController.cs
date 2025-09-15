using ClaimBridge.Scrubber.Data;
using ClaimBridge.ServiceDefaults.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Scrubber.Controllers;

[ApiController]
[Route("api/scrub-results")]
[Authorize(Policy = Policies.ClaimsRead)]
public class ScrubResultsController(ScrubberDb db) : ControllerBase
{
    public sealed record EditDto(string RuleCode, string Severity, string Message, string? Field, string? FixSuggestion, int? LineNumber);
    public sealed record RunDto(Guid Id, Guid ClaimId, int ClaimVersion, DateTimeOffset RanAt, bool Passed,
        int ErrorCount, int WarningCount, int RulesEvaluated, IEnumerable<EditDto> Edits);

    [HttpGet("{claimId:guid}")]
    public async Task<ActionResult<IEnumerable<RunDto>>> ForClaim(Guid claimId)
    {
        var runs = await db.ScrubRuns.AsNoTracking().Include(r => r.Edits)
            .Where(r => r.ClaimId == claimId)
            .OrderByDescending(r => r.RanAt)
            .Take(10)
            .ToListAsync();
        if (runs.Count > 0 && !User.CanAccessOrg(runs[0].OrgId)) return NotFound();

        return runs.Select(r => new RunDto(r.Id, r.ClaimId, r.ClaimVersion, r.RanAt, r.Passed, r.ErrorCount, r.WarningCount,
            r.RulesEvaluated, r.Edits.Select(e => new EditDto(e.RuleCode, e.Severity, e.Message, e.Field, e.FixSuggestion, e.LineNumber))))
            .ToList();
    }

    [HttpGet("top-edits")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<IEnumerable<object>> TopEdits([FromQuery] int days = 30)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days, 1, 365));
        return await db.ScrubRuns.AsNoTracking()
            .Where(r => r.RanAt >= since)
            .SelectMany(r => r.Edits)
            .GroupBy(e => new { e.RuleCode, e.Severity })
            .Select(g => new { g.Key.RuleCode, g.Key.Severity, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(15)
            .ToListAsync();
    }
}
