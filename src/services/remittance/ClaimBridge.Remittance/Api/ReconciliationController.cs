using System.Text.Json;
using ClaimBridge.Contracts.Events;
using ClaimBridge.Remittance.Data;
using ClaimBridge.Remittance.Domain;
using ClaimBridge.ServiceDefaults.Auth;
using ClaimBridge.X12.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Remittance.Api;

/// <summary>Ops worklist for remittance lines that could not be matched or did not balance.</summary>
[ApiController]
[Route("api/reconciliation")]
[Authorize(Policy = Policies.Staff)]
public class ReconciliationController(RemittanceDb db, PostingService posting, TimeProvider clock) : ControllerBase
{
    public sealed record MatchRequest(string ClaimNumber);
    public sealed record DismissRequest(string Note);

    [HttpGet]
    public async Task<IEnumerable<UnmatchedLine>> Open([FromQuery] bool includeResolved = false, CancellationToken ct = default) =>
        await db.Unmatched.AsNoTracking().Where(u => includeResolved || u.ResolvedAt == null)
            .OrderBy(u => u.CreatedAt).Take(200).ToListAsync(ct);

    /// <summary>Posts a parked line against a claim number chosen by the analyst (e.g. a typo in the payer's CLP01).</summary>
    [HttpPost("{id:guid}/match")]
    public async Task<IActionResult> Match(Guid id, MatchRequest request, CancellationToken ct)
    {
        var line = await db.Unmatched.FindAsync([id], ct);
        if (line is null || line.ResolvedAt is not null) return NotFound();
        var file = await db.Files.FindAsync([line.RemittanceFileId], ct);
        if (file is null) return NotFound();

        var clp = JsonSerializer.Deserialize<ClaimPayment>(line.RawClpJson, EventEnvelope.Json)! with { ClaimNumber = request.ClaimNumber };
        if (!await posting.PostAsync(file, clp, ct))
        {
            // PostAsync parked it again; drop the new duplicate and keep the original line open
            foreach (var parked in db.ChangeTracker.Entries<UnmatchedLine>().Where(e => e.State == EntityState.Added).ToList())
                parked.State = EntityState.Detached;
            return Problem($"Claim {request.ClaimNumber} is not a submitted claim for {file.PayerId} or the line does not balance.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        Resolve(line, $"Matched to {request.ClaimNumber}");
        file.MatchedCount++;
        file.UnmatchedCount--;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/dismiss")]
    public async Task<IActionResult> Dismiss(Guid id, DismissRequest request, CancellationToken ct)
    {
        var line = await db.Unmatched.FindAsync([id], ct);
        if (line is null || line.ResolvedAt is not null) return NotFound();
        Resolve(line, "Dismissed: " + request.Note);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private void Resolve(UnmatchedLine line, string resolution)
    {
        line.ResolvedAt = clock.GetUtcNow();
        line.ResolvedBy = User.Actor();
        line.Resolution = resolution.Length > 300 ? resolution[..300] : resolution;
        db.AuditLog.Add(new AuditLogEntry
        {
            At = clock.GetUtcNow(), Actor = User.Actor(), Action = "reconciliation.resolved", Target = line.ClaimNumber, Detail = line.Resolution
        });
    }
}
