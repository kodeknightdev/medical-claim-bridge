using ClaimBridge.Contracts.Events;
using ClaimBridge.Contracts.Submission;
using ClaimBridge.PayersGateway.Adapters;
using ClaimBridge.PayersGateway.Data;
using ClaimBridge.ServiceDefaults.Messaging;

namespace ClaimBridge.PayersGateway.Consumers;

/// <summary>Hands a claim to its payer adapter and turns the result into payers.* events.</summary>
public class SubmissionService(
    PayersDb db,
    PayerAdapterRegistry adapters,
    IEventOutbox outbox,
    TimeProvider clock,
    ILogger<SubmissionService> logger)
{
    public static readonly TimeSpan[] DeferBackoff =
        [TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)];

    public async Task SubmitAsync(Submission submission, SubmissionDocument document, CancellationToken ct)
    {
        var adapter = adapters.For(submission.PayerId);
        var now = clock.GetUtcNow();
        submission.DeliveryAttempts++;

        SubmitResult result;
        try
        {
            result = await adapter.SubmitAsync(document, submission.Attempt, ct);
        }
        catch (PayerUnavailableException ex)
        {
            // keep the claim parked rather than burning Kafka retries during a long payer outage
            var wait = DeferBackoff[Math.Min(submission.DeliveryAttempts - 1, DeferBackoff.Length - 1)];
            submission.Status = SubmissionStatus.Deferred;
            submission.NextAttemptAt = now + wait;
            submission.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            History(submission, "Submission", "deferred", $"{ex.Message}; next try in {wait.TotalSeconds:0}s");
            logger.LogWarning("Submission of {ClaimNumber} to {PayerId} deferred: {Error}", submission.ClaimNumber, submission.PayerId, ex.Message);
            return;
        }

        submission.LastError = null;
        submission.NextAttemptAt = null;
        if (result.Outcome == SubmitOutcome.Queued)
        {
            submission.Status = SubmissionStatus.Queued;
            History(submission, "Submission", "queued", "Waiting for the next outbound batch file");
            return;
        }

        submission.SentAt = now;
        PublishSubmitted(submission);
        ApplyAck(submission, result.Outcome == SubmitOutcome.Accepted, result.PayerClaimId, result.Rejections ?? [], "sync-ack");
    }

    public void PublishSubmitted(Submission submission)
    {
        submission.Status = SubmissionStatus.Sent;
        History(submission, "Submission", "sent", submission.FileName is null ? $"via {submission.Channel}" : $"in file {submission.FileName}");
        outbox.Enqueue(Topics.ClaimSubmitted, submission.ClaimId.ToString(), new ClaimSubmitted(
            submission.ClaimId, submission.ClaimNumber, submission.PayerId, submission.Id, submission.Attempt, submission.Channel,
            submission.SentAt ?? clock.GetUtcNow(), submission.FileName));
    }

    public void ApplyAck(Submission submission, bool accepted, string? payerClaimId, IReadOnlyList<string> rejections, string ackType)
    {
        submission.Status = accepted ? SubmissionStatus.Accepted : SubmissionStatus.Rejected;
        submission.AckAt = clock.GetUtcNow();
        submission.PayerClaimId = payerClaimId ?? submission.PayerClaimId;
        submission.RejectReasons = rejections.Count == 0 ? null : Truncate(string.Join("; ", rejections), 1000);
        History(submission, ackType, accepted ? "accepted" : "rejected", accepted ? $"Payer claim id {payerClaimId}" : submission.RejectReasons);
        outbox.Enqueue(Topics.AckReceived, submission.ClaimId.ToString(), new AckReceived(
            submission.ClaimId, submission.ClaimNumber, submission.PayerId, accepted, submission.PayerClaimId, rejections, ackType,
            submission.AckAt.Value));
    }

    public void PublishStatus(Submission submission, StatusAnswer answer, string source)
    {
        submission.LastPayerStatus = answer.Status;
        if (answer.PayerClaimId is not null) submission.PayerClaimId ??= answer.PayerClaimId;
        History(submission, source, answer.Status, answer.Outcome is null ? answer.Description : $"{answer.Outcome}: {answer.Description}");
        outbox.Enqueue(Topics.PayerStatusUpdated, submission.ClaimId.ToString(), new PayerStatusUpdated(
            submission.ClaimId, submission.ClaimNumber, submission.PayerId, answer.Status, answer.Outcome, answer.StatusCode,
            answer.Description, source, clock.GetUtcNow()));
    }

    public void History(Submission s, string kind, string status, string? detail) =>
        db.StatusEntries.Add(new PayerStatusEntry
        {
            ClaimId = s.ClaimId, ClaimNumber = s.ClaimNumber, PayerId = s.PayerId, Kind = kind, Status = status,
            Detail = detail is null ? null : Truncate(detail, 1000), At = clock.GetUtcNow()
        });

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] : s;
}
