using System.Text.Json;
using ClaimBridge.Claims.Application;
using ClaimBridge.Claims.Application.Clients;
using ClaimBridge.Claims.Data;
using ClaimBridge.Claims.Domain;
using ClaimBridge.ServiceDefaults.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Claims.Api;

[ApiController]
[Route("api/claims")]
[Authorize(Policy = Policies.ClaimsRead)]
public class ClaimsController(
    ClaimsDb db,
    ClaimIntakeService intake,
    ClaimPhi phi,
    ScrubberClient scrubber,
    EligibilityClient eligibility,
    RiskClient risk,
    TimeProvider clock) : ControllerBase
{
    public sealed record ScrubPreviewResponse(bool Passed, IReadOnlyList<EditDto> Edits, EligibilityResult Eligibility, RiskScore? Risk);

    public sealed record ClaimSearchQuery(string? Status, string? PayerId, Guid? OrgId, DateOnly? From, DateOnly? To,
        string? Search, int Page = 1, int PageSize = 25);

    public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

    [HttpGet]
    public async Task<PagedResult<ClaimSearchEntry>> Search([FromQuery] ClaimSearchQuery q, CancellationToken ct)
    {
        var query = db.SearchEntries.AsNoTracking();
        if (User.OrgFilter(q.OrgId) is { } org) query = query.Where(c => c.OrgId == org);
        if (!string.IsNullOrWhiteSpace(q.Status))
        {
            var statuses = q.Status.Split(',', StringSplitOptions.RemoveEmptyEntries);
            query = query.Where(c => statuses.Contains(c.Status));
        }
        if (!string.IsNullOrWhiteSpace(q.PayerId)) query = query.Where(c => c.PayerId == q.PayerId);
        if (q.From is { } from) query = query.Where(c => c.FirstServiceDate >= from);
        if (q.To is { } to) query = query.Where(c => c.FirstServiceDate <= to);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim().ToUpperInvariant();
            // a full member id is matched through its hash so the plain value never hits the database
            var memberHash = s.Length > 4 ? phi.HashMemberId(s) : null;
            query = query.Where(c => c.ClaimNumber.StartsWith(s) || c.MemberIdLast4 == s || c.MemberIdHash == memberHash
                                     || c.ProcedureCodes.Contains(s));
        }

        var size = Math.Clamp(q.PageSize, 1, 200);
        var page = Math.Max(q.Page, 1);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(c => c.CreatedAt).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<ClaimSearchEntry>(items, total, page, size);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClaimDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var claim = await db.Claims.AsNoTracking().Include(c => c.History).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (claim is null || !User.CanAccessOrg(claim.OrgId)) return NotFound();

        db.AuditLog.Add(new AuditLogEntry
        {
            At = clock.GetUtcNow(), Actor = User.Actor(), OrgId = User.OrgId(), Action = "phi.viewed", Target = claim.ClaimNumber
        });
        await db.SaveChangesAsync(ct);

        return new ClaimDetailDto(
            claim.ToSummary(), phi.Reveal(claim), claim.BillingNpi, claim.RenderingNpi, claim.PriorAuthNumber,
            claim.OriginalClaimNumber, claim.PreviousClaimId, claim.SupersededByClaimId, claim.Diagnoses,
            claim.Lines.OrderBy(l => l.LineNumber).Select(l => new ServiceLineDto(l.LineNumber, l.ProcedureCode, l.Modifiers, l.Units,
                l.Charge, l.ServiceDate, l.PlaceOfService, l.DxPointers)).ToList(),
            claim.History.OrderBy(h => h.Sequence).Select(h => h.ToDto()).ToList(),
            claim.PayerClaimId, claim.AllowedAmount, claim.PatientResponsibility, claim.AdjustmentAmount,
            claim.DenialCategory, claim.DenialCarc, claim.PossibleDuplicateOf,
            claim.DenialRiskFactorsJson is null ? null : JsonSerializer.Deserialize<JsonElement>(claim.DenialRiskFactorsJson));
    }

    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult<IEnumerable<HistoryDto>>> History(Guid id, CancellationToken ct)
    {
        var orgId = await db.Claims.Where(c => c.Id == id).Select(c => (Guid?)c.OrgId).FirstOrDefaultAsync(ct);
        if (orgId is null || !User.CanAccessOrg(orgId.Value)) return NotFound();
        return await db.StatusEvents.AsNoTracking().Where(h => h.ClaimId == id).OrderBy(h => h.Sequence)
            .Select(h => new HistoryDto(h.Sequence, h.FromStatus.ToString(), h.ToStatus.ToString(), h.Trigger, h.Reason, h.Source, h.OccurredAt))
            .ToListAsync(ct);
    }

    [HttpPost]
    [Authorize(Policy = Policies.ClaimsSubmit)]
    public async Task<IActionResult> Create(ClaimRequest request, CancellationToken ct)
    {
        var orgId = ResolveOrg(request.OrgId);
        if (orgId is null) return Problem("orgId is required for clearinghouse staff", statusCode: 400);

        var outcome = await intake.AcceptAsync(request, orgId.Value, ClaimSource.Portal, User.Actor(), null, ct);
        return outcome.Problem is { } p
            ? Problem(p.Detail, title: p.Title, statusCode: p.Status)
            : CreatedAtAction(nameof(Get), new { id = outcome.Claim!.Id }, outcome.Claim.ToSummary());
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ClaimsSubmit)]
    public async Task<IActionResult> Update(Guid id, ClaimRequest request, CancellationToken ct)
    {
        var claim = await db.Claims.Include(c => c.History).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (claim is null || !User.CanAccessOrg(claim.OrgId)) return NotFound();

        var outcome = await intake.UpdateAsync(claim, request, User.Actor(), ct);
        return outcome.Problem is { } p ? Problem(p.Detail, title: p.Title, statusCode: p.Status) : Ok(claim.ToSummary());
    }

    /// <summary>Debounced inline check while the claim is typed. Nothing is persisted.</summary>
    [HttpPost("scrub-preview")]
    [Authorize(Policy = Policies.ClaimsSubmit)]
    public async Task<ActionResult<ScrubPreviewResponse>> ScrubPreview(ClaimRequest request, CancellationToken ct)
    {
        var orgId = ResolveOrg(request.OrgId) ?? Guid.Empty;
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var dos = request.Lines.Count == 0 ? today : request.Lines.Min(l => l.ServiceDate);

        var eligibilityTask = request.Patient is null
            ? Task.FromResult(EligibilityResult.Unknown)
            : eligibility.CheckAsync(request.PayerId, request.Patient.MemberId, request.Patient.DateOfBirth, dos, ct);
        var riskTask = risk.ScoreAsync(null, orgId, request, today, ct);
        var elig = await eligibilityTask;

        var scrub = await scrubber.ScrubAsync(Guid.Empty, 0, orgId, request, elig, null, today, persist: false, ct);
        return new ScrubPreviewResponse(scrub.Passed, scrub.Edits, elig, await riskTask);
    }

    /// <summary>Corrected claim (frequency 7). The original is superseded and the new version is linked to it.</summary>
    [HttpPost("{id:guid}/correct")]
    [Authorize(Policy = Policies.ClaimsSubmit)]
    public async Task<IActionResult> Correct(Guid id, ClaimRequest request, CancellationToken ct)
    {
        var original = await db.Claims.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (original is null || !User.CanAccessOrg(original.OrgId)) return NotFound();

        var outcome = await intake.AcceptAsync(request with { FrequencyCode = "7", OriginalClaimNumber = original.ClaimNumber },
            original.OrgId, ClaimSource.Portal, User.Actor(), null, ct);
        return outcome.Problem is { } p
            ? Problem(p.Detail, title: p.Title, statusCode: p.Status)
            : CreatedAtAction(nameof(Get), new { id = outcome.Claim!.Id }, outcome.Claim.ToSummary());
    }

    /// <summary>Void (frequency 8): sends a cancel to the payer for a claim that should never have been billed.</summary>
    [HttpPost("{id:guid}/void")]
    [Authorize(Policy = Policies.ClaimsSubmit)]
    public async Task<IActionResult> Void(Guid id, CancellationToken ct)
    {
        var original = await db.Claims.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (original is null || !User.CanAccessOrg(original.OrgId)) return NotFound();

        var request = phi.ToRequest(original) with { FrequencyCode = "8", OriginalClaimNumber = original.ClaimNumber };
        var outcome = await intake.AcceptAsync(request, original.OrgId, ClaimSource.Portal, User.Actor(), null, ct);
        return outcome.Problem is { } p
            ? Problem(p.Detail, title: p.Title, statusCode: p.Status)
            : Ok(outcome.Claim!.ToSummary());
    }

    private Guid? ResolveOrg(Guid? requested) => User.IsStaff() ? requested : User.OrgId();
}
