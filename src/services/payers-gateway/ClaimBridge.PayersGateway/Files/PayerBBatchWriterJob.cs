using ClaimBridge.Contracts.Reference;
using ClaimBridge.PayersGateway.Adapters;
using ClaimBridge.PayersGateway.Consumers;
using ClaimBridge.PayersGateway.Data;
using ClaimBridge.ServiceDefaults.Storage;
using ClaimBridge.X12.Core;
using ClaimBridge.X12.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

namespace ClaimBridge.PayersGateway.Files;

/// <summary>Collects queued Bluepeak submissions into one 837P file and drops it into the payer's inbound folder.</summary>
[DisallowConcurrentExecution]
public class PayerBBatchWriterJob(
    PayersDb db,
    ClaimsClient claims,
    SubmissionService submissions,
    IBlobStore blobs,
    IOptionsMonitor<PayerOptions> options,
    TimeProvider clock,
    ILogger<PayerBBatchWriterJob> logger) : IJob
{
    public const int MaxClaimsPerFile = 100;

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var queued = await db.Submissions
            .Where(s => s.PayerId == SeedIds.PayerB && s.Status == SubmissionStatus.Queued)
            .OrderBy(s => s.CreatedAt).Take(MaxClaimsPerFile).ToListAsync(ct);
        if (queued.Count == 0) return;

        var documents = new List<ClaimBridge.Contracts.Submission.SubmissionDocument>();
        foreach (var s in queued)
            documents.Add(await claims.GetSubmissionAsync(s.ClaimId, ct));

        var now = clock.GetUtcNow();
        var control = await db.NextInterchangeControlNumberAsync(ct);
        var payer = options.Get(SeedIds.PayerB);
        var x12 = Claim837PWriter.Write(documents,
            new SubmitterInfo(payer.SubmitterId, "Claim Bridge Clearinghouse", "edi@claimbridge.local"),
            new ReceiverInfo("BLUEPEAK", "Bluepeak Mutual", SeedIds.PayerB),
            new InterchangeOptions(payer.SubmitterId, "BLUEPEAK", control, now), $"CB{control}");

        var fileName = $"CB837_{control:D9}.x12";
        var blob = await blobs.UploadTextAsync(payer.FileContainer, $"inbound/{fileName}", x12, "application/edi-x12", ct);
        await blobs.UploadTextAsync("x12-archive", $"{SeedIds.PayerB}/outbound/{fileName}", x12, "application/edi-x12", ct);

        db.Files.Add(new PayerFile
        {
            Id = Guid.NewGuid(), PayerId = SeedIds.PayerB, Direction = FileDirection.Outbound, Kind = "837", FileName = fileName,
            BlobContainer = blob.Container, BlobName = blob.Name, ControlNumber = control.ToString(), ClaimCount = queued.Count,
            Status = "Sent", At = now
        });
        foreach (var s in queued)
        {
            s.FileName = fileName;
            s.InterchangeControlNumber = control.ToString("D9");
            s.SentAt = now;
            submissions.PublishSubmitted(s);
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Wrote {FileName} with {Count} claim(s) for {PayerId}", fileName, queued.Count, SeedIds.PayerB);
    }
}
