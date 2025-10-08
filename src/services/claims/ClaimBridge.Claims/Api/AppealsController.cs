using System.ComponentModel.DataAnnotations;
using ClaimBridge.Claims.Application;
using ClaimBridge.Claims.Application.Clients;
using ClaimBridge.Claims.Data;
using ClaimBridge.Claims.Denials;
using ClaimBridge.Claims.Domain;
using ClaimBridge.ServiceDefaults.Auth;
using ClaimBridge.ServiceDefaults.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClaimBridge.Claims.Api;

[ApiController]
[Route("api/appeals")]
[Authorize(Policy = Policies.ClaimsRead)]
public class AppealsController(
    ClaimsDb db,
    ClaimTransitions transitions,
    DenialWorklist worklist,
    ProvidersClient providers,
    IBlobStore blobs,
    IOptions<AppealOptions> options,
    TimeProvider clock) : ControllerBase
{
    public const string LetterContainer = "appeal-letters";

    public sealed record CreateAppeal(Guid ClaimId, [property: Required, StringLength(4000, MinimumLength = 20)] string Argument,
        List<string>? Enclosures);

    public sealed record Outcome(bool Won, [property: StringLength(500)] string? Note);

    public sealed record AppealDto(Guid Id, Guid ClaimId, string ClaimNumber, string PayerId, string CarcCode, string Status,
        DateOnly Deadline, int DaysLeft, DateTimeOffset CreatedAt, DateTimeOffset? SubmittedAt, DateTimeOffset? OutcomeAt,
        string? OutcomeNote, IReadOnlyList<string> Enclosures);

    [HttpGet]
    public async Task<IEnumerable<AppealDto>> List([FromQuery] Guid? orgId, [FromQuery] string? status, CancellationToken ct)
    {
        var query = db.Appeals.AsNoTracking();
        if (User.OrgFilter(orgId) is { } org) query = query.Where(a => a.OrgId == org);
        if (Enum.TryParse<AppealStatus>(status, out var s)) query = query.Where(a => a.Status == s);
        var rows = await query.OrderBy(a => a.Deadline).Take(200).ToListAsync(ct);
        return rows.Select(ToDto);
    }

    [HttpPost]
    [Authorize(Policy = Policies.ClaimsSubmit)]
    public async Task<IActionResult> Create(CreateAppeal request, CancellationToken ct)
    {
        var claim = await db.Claims.Include(c => c.History).FirstOrDefaultAsync(c => c.Id == request.ClaimId, ct);
        if (claim is null || !User.CanAccessOrg(claim.OrgId)) return NotFound();
        if (claim.Status is not (ClaimStatus.Denied or ClaimStatus.Posted or ClaimStatus.PartiallyPaid))
            return Problem($"Only denied or underpaid claims can be appealed; this claim is {claim.Status}.", statusCode: 409);
        if (await db.Appeals.AnyAsync(a => a.ClaimId == claim.Id && a.Status != AppealStatus.Withdrawn && a.Status != AppealStatus.Lost, ct))
            return Problem("An appeal is already open for this claim.", statusCode: 409);

        var now = clock.GetUtcNow();
        var denial = await db.DenialWorkItems.FindAsync([claim.Id], ct);
        var appeal = new Appeal
        {
            Id = Guid.NewGuid(), ClaimId = claim.Id, ClaimNumber = claim.ClaimNumber, OrgId = claim.OrgId, PayerId = claim.PayerId,
            CarcCode = denial?.CarcCode ?? claim.DenialCarc ?? "16", Argument = request.Argument, Enclosures = request.Enclosures ?? [],
            Status = AppealStatus.Draft, CreatedBy = User.Identity?.Name ?? User.Actor(), CreatedAt = now,
            Deadline = denial?.AppealDeadline ?? DateOnly.FromDateTime(now.UtcDateTime).AddDays(options.Value.WindowFor(claim.PayerId))
        };

        var org = await providers.GetOrgAsync(claim.OrgId, ct);
        var pdf = AppealLetter.Render(appeal, claim, org, DateOnly.FromDateTime(now.UtcDateTime));
        using var stream = new MemoryStream(pdf);
        var blob = await blobs.UploadAsync(LetterContainer, $"{claim.OrgId}/{appeal.Id}.pdf", stream, "application/pdf", ct);
        appeal.LetterBlobName = blob.Name;

        db.Appeals.Add(appeal);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(List), null, ToDto(appeal));
    }

    [HttpGet("{id:guid}/letter")]
    public async Task<IActionResult> Letter(Guid id, CancellationToken ct)
    {
        var appeal = await db.Appeals.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (appeal?.LetterBlobName is null || !User.CanAccessOrg(appeal.OrgId)) return NotFound();
        var stream = await blobs.OpenReadAsync(new BlobRef(LetterContainer, appeal.LetterBlobName), ct);
        return File(stream, "application/pdf", $"appeal-{appeal.ClaimNumber}.pdf");
    }

    [HttpPost("{id:guid}/submit")]
    [Authorize(Policy = Policies.ClaimsSubmit)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
    {
        var appeal = await db.Appeals.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (appeal is null || !User.CanAccessOrg(appeal.OrgId)) return NotFound();
        if (appeal.Status != AppealStatus.Draft) return Problem("Only draft appeals can be submitted.", statusCode: 409);
        if (DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) > appeal.Deadline)
            return Problem($"The appeal window closed on {appeal.Deadline:yyyy-MM-dd}.", statusCode: 422);

        var claim = await db.Claims.Include(c => c.History).FirstAsync(c => c.Id == appeal.ClaimId, ct);
        if (!transitions.TryFire(claim, LifecycleTrigger.Appeal, "portal", $"Appeal sent, deadline {appeal.Deadline:yyyy-MM-dd}"))
            return Problem($"Claim is {claim.Status} and cannot be appealed.", statusCode: 409);

        appeal.Status = AppealStatus.Submitted;
        appeal.SubmittedAt = clock.GetUtcNow();
        await worklist.MarkAsync(claim.Id, DenialStatus.Appealed, ct, appealId: appeal.Id);
        await transitions.ProjectAsync(claim, ct);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(appeal));
    }

    /// <summary>Records the payer's decision. A win reopens the claim to wait for the corrected payment.</summary>
    [HttpPost("{id:guid}/outcome")]
    [Authorize(Policy = Policies.ClaimsSubmit)]
    public async Task<IActionResult> RecordOutcome(Guid id, Outcome outcome, CancellationToken ct)
    {
        var appeal = await db.Appeals.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (appeal is null || !User.CanAccessOrg(appeal.OrgId)) return NotFound();
        if (appeal.Status != AppealStatus.Submitted) return Problem("Only submitted appeals can get an outcome.", statusCode: 409);

        var claim = await db.Claims.Include(c => c.History).FirstAsync(c => c.Id == appeal.ClaimId, ct);
        transitions.TryFire(claim, outcome.Won ? LifecycleTrigger.AppealWon : LifecycleTrigger.AppealLost, "portal", outcome.Note);

        appeal.Status = outcome.Won ? AppealStatus.Won : AppealStatus.Lost;
        appeal.OutcomeAt = clock.GetUtcNow();
        appeal.OutcomeNote = outcome.Note;
        await worklist.MarkAsync(claim.Id, outcome.Won ? DenialStatus.Resolved : DenialStatus.WrittenOff, ct);
        await transitions.ProjectAsync(claim, ct);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(appeal));
    }

    private AppealDto ToDto(Appeal a) => new(a.Id, a.ClaimId, a.ClaimNumber, a.PayerId, a.CarcCode, a.Status.ToString(), a.Deadline,
        a.Deadline.DayNumber - DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).DayNumber, a.CreatedAt, a.SubmittedAt, a.OutcomeAt,
        a.OutcomeNote, a.Enclosures);
}
