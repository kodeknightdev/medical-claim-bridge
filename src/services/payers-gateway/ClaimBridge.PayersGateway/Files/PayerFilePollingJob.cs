using ClaimBridge.Contracts.Events;
using ClaimBridge.Contracts.Reference;
using ClaimBridge.PayersGateway.Adapters;
using ClaimBridge.PayersGateway.Consumers;
using ClaimBridge.PayersGateway.Data;
using ClaimBridge.ServiceDefaults.Messaging;
using ClaimBridge.ServiceDefaults.Storage;
using ClaimBridge.X12.Core;
using ClaimBridge.X12.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

namespace ClaimBridge.PayersGateway.Files;

/// <summary>
/// Polling consumer for the payers' outbound folders: 999 and 277CA acknowledgements, and 835 remittances
/// which are handed to the remittance service by reference.
/// </summary>
[DisallowConcurrentExecution]
public class PayerFilePollingJob(
    PayersDb db,
    IBlobStore blobs,
    SubmissionService submissions,
    IEventOutbox outbox,
    IOptionsMonitor<PayerOptions> options,
    TimeProvider clock,
    ILogger<PayerFilePollingJob> logger) : IJob
{
    public const string RemittanceContainer = "remittances";

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        foreach (var payerId in SeedIds.Payers)
        {
            var container = options.Get(payerId).FileContainer;
            if (string.IsNullOrEmpty(container)) continue;

            IReadOnlyList<string> names;
            try
            {
                names = await blobs.ListAsync(container, "outbound/", ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Listing {Container} failed: {Error}", container, ex.Message);
                continue;
            }

            // payer B sends 999 and 277CA out of order, so each file is handled on its own
            foreach (var name in names.Where(n => n.Count(c => c == '/') == 1).Order())
                await ProcessFileAsync(payerId, new BlobRef(container, name), ct);
        }
    }

    private async Task ProcessFileAsync(string payerId, BlobRef blob, CancellationToken ct)
    {
        var fileName = blob.Name["outbound/".Length..];
        var file = new PayerFile
        {
            Id = Guid.NewGuid(), PayerId = payerId, Direction = FileDirection.Inbound, FileName = fileName,
            BlobContainer = blob.Container, BlobName = blob.Name, At = clock.GetUtcNow()
        };

        try
        {
            var text = await blobs.ReadTextAsync(blob, ct);
            file.Kind = X12FileKind.Detect(text);
            switch (file.Kind)
            {
                case "999": await Handle999Async(payerId, Ack999.Read(text), file, ct); break;
                case "277": await Handle277Async(payerId, ClaimAck277CA.Read(text), file, ct); break;
                case "835": await Handle835Async(payerId, text, file, ct); break;
                default: throw new X12ParseException($"Unexpected transaction {file.Kind}");
            }
            file.Status = "Processed";
            db.Files.Add(file);
            await db.SaveChangesAsync(ct);
            await blobs.MoveAsync(blob, $"outbound/processed/{fileName}", ct);
        }
        catch (X12ParseException ex)
        {
            db.ChangeTracker.Clear();
            logger.LogError("Malformed file {File} from {PayerId}: {Error}", fileName, payerId, ex.Message);
            file.Kind = file.Kind.Length == 0 ? "?" : file.Kind;
            file.Status = "Error";
            file.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            db.Files.Add(file);
            await db.SaveChangesAsync(ct);
            await blobs.MoveAsync(blob, $"outbound/error/{fileName}", ct);
        }
    }

    private async Task Handle999Async(string payerId, Ack999 ack, PayerFile file, CancellationToken ct)
    {
        file.ControlNumber = ack.AcknowledgedGroupControlNumber;
        var controlNumber = int.TryParse(ack.AcknowledgedGroupControlNumber, out var n) ? n.ToString("D9") : ack.AcknowledgedGroupControlNumber;
        var inFile = await db.Submissions
            .Where(s => s.PayerId == payerId && s.InterchangeControlNumber == controlNumber).ToListAsync(ct);
        file.ClaimCount = inFile.Count;

        foreach (var s in inFile)
        {
            if (ack.Accepted)
            {
                submissions.History(s, "999", "accepted", $"File accepted by {payerId}");
                continue;
            }
            // a rejected 999 means the whole file never reached claim-level edits
            if (s.Status == SubmissionStatus.Sent)
                submissions.ApplyAck(s, false, null,
                    ["999 rejected the file", .. ack.Sets.SelectMany(x => x.Errors)], "999");
        }
    }

    private async Task Handle277Async(string payerId, ClaimAck277CA ack, PayerFile file, CancellationToken ct)
    {
        file.ClaimCount = ack.Claims.Count;
        foreach (var c in ack.Claims)
        {
            var s = await db.Submissions.Where(x => x.PayerId == payerId && x.ClaimNumber == c.ClaimNumber)
                .OrderByDescending(x => x.Attempt).FirstOrDefaultAsync(ct);
            if (s is null)
            {
                logger.LogWarning("277CA for unknown claim {ClaimNumber}", c.ClaimNumber);
                continue;
            }
            if (s.Status is SubmissionStatus.Accepted or SubmissionStatus.Rejected) continue;
            submissions.ApplyAck(s, c.Accepted, c.PayerClaimId,
                c.Accepted ? [] : [$"{c.StatusCategory}:{c.StatusCode} {c.Message}".Trim()], "277CA");
        }
    }

    private async Task Handle835Async(string payerId, string text, PayerFile file, CancellationToken ct)
    {
        var era = RemittanceAdvice.Read(text);
        file.ClaimCount = era.Claims.Count;
        file.ControlNumber = era.EftTraceNumber?.Length > 9 ? era.EftTraceNumber[..9] : era.EftTraceNumber;

        var stored = await blobs.UploadTextAsync(RemittanceContainer, $"{payerId}/{file.Id:N}_{file.FileName}", text,
            "application/edi-x12", ct);
        outbox.Enqueue(Topics.RemittanceReceived, file.Id.ToString(), new RemittanceReceived(
            file.Id, payerId, stored.Container, stored.Name, era.EftTraceNumber, clock.GetUtcNow()));

        foreach (var c in era.Claims)
        {
            var s = await db.Submissions.Where(x => x.PayerId == payerId && x.ClaimNumber == c.ClaimNumber)
                .OrderByDescending(x => x.Attempt).FirstOrDefaultAsync(ct);
            if (s is null) continue;
            s.LastPayerStatus = "remitted";
            submissions.History(s, "835", c.IsReversal ? "reversal" : c.IsDenied ? "denied" : "paid",
                    $"Paid {c.PaidAmount:0.00} of {c.ChargeAmount:0.00}, EFT {era.EftTraceNumber}");
        }
    }
}
