using System.Text;
using System.Text.Json;
using ClaimBridge.Claims.Batches;
using ClaimBridge.Claims.Data;
using ClaimBridge.Contracts.Events;
using ClaimBridge.ServiceDefaults.Auth;
using ClaimBridge.ServiceDefaults.Messaging;
using ClaimBridge.ServiceDefaults.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Claims.Api;

[ApiController]
[Route("api/batches")]
[Authorize(Policy = Policies.ClaimsRead)]
public class BatchesController(ClaimsDb db, IBlobStore blobs, IEventOutbox outbox, TimeProvider clock) : ControllerBase
{
    public const string Container = "claim-batches";
    private const long MaxBytes = 5 * 1024 * 1024;

    public sealed record BatchDto(Guid Id, Guid OrgId, string FileName, string Format, string Status, int TotalClaims,
        int Accepted, int Rejected, DateTimeOffset UploadedAt, DateTimeOffset? CompletedAt, JsonElement? Errors);

    [HttpPost]
    [Authorize(Policy = Policies.ClaimsSubmit)]
    [RequestSizeLimit(MaxBytes)]
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] Guid? orgId, CancellationToken ct)
    {
        var org = User.IsStaff() ? orgId : User.OrgId();
        if (org is null) return Problem("orgId is required for clearinghouse staff", statusCode: 400);
        if (file.Length == 0 || file.Length > MaxBytes) return Problem("File must be between 1 byte and 5 MB.", statusCode: 400);

        var format = await DetectFormatAsync(file);
        if (format is null)
            return Problem("Upload a CSV using the template or a simplified X12 837P file.", title: "Unsupported file", statusCode: 400);

        var batch = new Batch
        {
            Id = Guid.NewGuid(), OrgId = org.Value, FileName = Path.GetFileName(file.FileName), Format = format,
            Status = BatchStatus.Uploaded, UploadedBy = User.Actor(), UploadedAt = clock.GetUtcNow()
        };
        await using (var stream = file.OpenReadStream())
        {
            var blob = await blobs.UploadAsync(Container, $"{org}/{batch.Id}/{batch.FileName}", stream,
                format == "csv" ? "text/csv" : "application/edi-x12", ct);
            batch.BlobContainer = blob.Container;
            batch.BlobName = blob.Name;
        }

        db.Batches.Add(batch);
        outbox.Enqueue(Topics.BatchUploaded, batch.Id.ToString(),
            new BatchUploaded(batch.Id, batch.OrgId, format, batch.BlobContainer, batch.BlobName, batch.UploadedBy));
        await db.SaveChangesAsync(ct);
        return Accepted($"/api/batches/{batch.Id}", ToDto(batch));
    }

    [HttpGet]
    public async Task<IEnumerable<BatchDto>> List([FromQuery] Guid? orgId, CancellationToken ct)
    {
        var query = db.Batches.AsNoTracking();
        if (User.OrgFilter(orgId) is { } org) query = query.Where(b => b.OrgId == org);
        var rows = await query.OrderByDescending(b => b.UploadedAt).Take(100).ToListAsync(ct);
        return rows.Select(ToDto);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BatchDto>> Get(Guid id, CancellationToken ct)
    {
        var batch = await db.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        return batch is null || !User.CanAccessOrg(batch.OrgId) ? NotFound() : ToDto(batch);
    }

    [HttpGet("template")]
    [AllowAnonymous]
    public FileContentResult Template() => File(Encoding.UTF8.GetBytes(CsvClaimParser.Header + "\n"), "text/csv", "claim-batch-template.csv");

    private static async Task<string?> DetectFormatAsync(IFormFile file)
    {
        using var reader = new StreamReader(file.OpenReadStream());
        var start = (await reader.ReadLineAsync())?.TrimStart() ?? "";
        if (start.StartsWith("ISA", StringComparison.Ordinal)) return "x12";
        return start.StartsWith("claim_ref", StringComparison.OrdinalIgnoreCase) ? "csv" : null;
    }

    private static BatchDto ToDto(Batch b) => new(b.Id, b.OrgId, b.FileName, b.Format, b.Status.ToString(), b.TotalClaims,
        b.Accepted, b.Rejected, b.UploadedAt, b.CompletedAt,
        b.ErrorsJson is null ? null : JsonSerializer.Deserialize<JsonElement>(b.ErrorsJson));
}
