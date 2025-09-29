using ClaimBridge.PayersGateway.Adapters;
using ClaimBridge.PayersGateway.Data;

namespace ClaimBridge.PayersGateway.Consumers;

/// <summary>276/277-style status inquiry. A useful answer is published as the matching payers.* event.</summary>
public class StatusInquiryService(PayerAdapterRegistry adapters, SubmissionService submissions, TimeProvider clock)
{
    public async Task<StatusAnswer?> InquireAsync(Submission submission, CancellationToken ct)
    {
        submission.LastInquiryAt = clock.GetUtcNow();
        StatusAnswer answer;
        try
        {
            answer = await adapters.For(submission.PayerId)
                .InquireStatusAsync(new StatusQuery(submission.ClaimNumber, submission.PayerClaimId), ct);
        }
        catch (PayerUnavailableException ex)
        {
            submissions.History(submission, "Inquiry", "failed", ex.Message);
            return null;
        }

        switch (answer.Status)
        {
            case "received" when submission.Status == SubmissionStatus.Sent:
                submissions.ApplyAck(submission, true, answer.PayerClaimId, [], "277-inquiry");
                break;
            case "rejected" when submission.Status == SubmissionStatus.Sent:
                submissions.ApplyAck(submission, false, answer.PayerClaimId, [answer.Description ?? "Rejected"], "277-inquiry");
                break;
            case "pended" or "in-process" or "finalized":
                if (submission.Status == SubmissionStatus.Sent)
                    submissions.ApplyAck(submission, true, answer.PayerClaimId, [], "277-inquiry");
                if (answer.Status != submission.LastPayerStatus || answer.Status == "finalized")
                    submissions.PublishStatus(submission, answer, "277-inquiry");
                break;
            default:
                submissions.History(submission, "Inquiry", answer.Status, answer.Description);
                break;
        }
        return answer;
    }
}
