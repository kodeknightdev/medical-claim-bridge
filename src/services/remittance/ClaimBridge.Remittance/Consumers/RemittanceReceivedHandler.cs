using ClaimBridge.Contracts.Events;
using ClaimBridge.Remittance.Data;
using ClaimBridge.Remittance.Domain;
using ClaimBridge.ServiceDefaults.Messaging;
using ClaimBridge.ServiceDefaults.Storage;
using ClaimBridge.X12.Core;
using ClaimBridge.X12.Transactions;

namespace ClaimBridge.Remittance.Consumers;

/// <summary>
/// First step of the remittance choreography: read the 835 by reference, post each claim, and publish
/// payment-posted / claim-denied / reversal-posted. Unmatched lines are parked, never failing the batch.
/// </summary>
public class RemittanceReceivedHandler(
    RemittanceDb db,
    IBlobStore blobs,
    PostingService posting,
    TimeProvider clock,
    ILogger<RemittanceReceivedHandler> logger) : IEventHandler
{
    public async Task HandleAsync(EventEnvelope envelope, string topic, CancellationToken ct)
    {
        var received = envelope.DataAs<RemittanceReceived>();
        if (await db.Files.FindAsync([received.RemittanceFileId], ct) is not null) return;

        var file = new RemittanceFile
        {
            Id = received.RemittanceFileId, PayerId = received.PayerId, BlobContainer = received.BlobContainer,
            BlobName = received.BlobName, EftTraceNumber = received.EftTraceNumber, ReceivedAt = clock.GetUtcNow()
        };
        db.Files.Add(file);

        RemittanceAdvice era;
        try
        {
            era = RemittanceAdvice.Read(await blobs.ReadTextAsync(new BlobRef(received.BlobContainer, received.BlobName), ct));
        }
        catch (X12ParseException ex)
        {
            file.Status = "Unreadable";
            file.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            logger.LogError("835 {Blob} could not be parsed: {Error}", received.BlobName, ex.Message);
            return;
        }

        file.EftTraceNumber = era.EftTraceNumber;
        file.PaymentDate = era.PaymentDate;
        file.TotalPaid = era.TotalPaid;
        file.PaymentMethod = era.PaymentMethod;
        file.ClaimCount = era.Claims.Count;

        foreach (var clp in era.Claims)
        {
            if (await posting.PostAsync(file, clp, ct)) file.MatchedCount++;
            else file.UnmatchedCount++;
        }
        file.Status = file.UnmatchedCount == 0 ? "Posted" : "PartiallyPosted";
        logger.LogInformation("835 {Trace} from {PayerId}: {Matched} posted, {Unmatched} to reconciliation",
            file.EftTraceNumber, file.PayerId, file.MatchedCount, file.UnmatchedCount);
    }
}
