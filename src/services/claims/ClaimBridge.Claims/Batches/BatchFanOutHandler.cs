using System.Text.Json;
using ClaimBridge.Claims.Application;
using ClaimBridge.Claims.Data;
using ClaimBridge.Claims.Domain;
using ClaimBridge.Contracts.Events;
using ClaimBridge.ServiceDefaults.Messaging;
using ClaimBridge.ServiceDefaults.Storage;
using FluentValidation;

namespace ClaimBridge.Claims.Batches;

/// <summary>Claim check consumer: pulls the uploaded file from blob storage and fans it out into individual claims.</summary>
public class BatchFanOutHandler(
    ClaimsDb db,
    IBlobStore blobs,
    ClaimIntakeService intake,
    IValidator<ClaimRequest> validator,
    TimeProvider clock,
    ILogger<BatchFanOutHandler> logger) : IEventHandler
{
    public sealed record BatchError(string Reference, string Message);

    public async Task HandleAsync(EventEnvelope envelope, string topic, CancellationToken ct)
    {
        var uploaded = envelope.DataAs<BatchUploaded>();
        var batch = await db.Batches.FindAsync([uploaded.BatchId], ct);
        if (batch is null || batch.Status is BatchStatus.Completed) return;

        batch.Status = BatchStatus.Processing;
        var parsed = await ParseAsync(new BlobRef(uploaded.BlobContainer, uploaded.BlobName), uploaded.Format, ct);

        var errors = new List<BatchError>();
        foreach (var item in parsed)
        {
            if (item.Request is null)
            {
                errors.Add(new BatchError(item.Reference, item.Error ?? "Unreadable claim"));
                continue;
            }
            var validation = await validator.ValidateAsync(item.Request, ct);
            if (!validation.IsValid)
            {
                errors.Add(new BatchError(item.Reference, string.Join("; ", validation.Errors.Select(e => e.ErrorMessage))));
                continue;
            }
            var outcome = await intake.AcceptAsync(item.Request, batch.OrgId, ClaimSource.Batch, uploaded.UploadedBy, batch.Id, ct);
            if (outcome.Problem is { } p)
                errors.Add(new BatchError(item.Reference, p.Detail));
        }

        batch.TotalClaims = parsed.Count;
        batch.Rejected = errors.Count;
        batch.Accepted = parsed.Count - errors.Count;
        batch.ErrorsJson = errors.Count == 0 ? null : JsonSerializer.Serialize(errors, EventEnvelope.Json);
        batch.Status = BatchStatus.Completed;
        batch.CompletedAt = clock.GetUtcNow();
        logger.LogInformation("Batch {BatchId}: {Accepted} accepted, {Rejected} rejected", batch.Id, batch.Accepted, batch.Rejected);
    }

    private async Task<IReadOnlyList<ParsedClaim>> ParseAsync(BlobRef blob, string format, CancellationToken ct)
    {
        await using var stream = await blobs.OpenReadAsync(blob, ct);
        using var reader = new StreamReader(stream);
        return format switch
        {
            "csv" => CsvClaimParser.Parse(reader),
            _ => throw new NotSupportedException($"Batch format {format} is not supported")
        };
    }
}
