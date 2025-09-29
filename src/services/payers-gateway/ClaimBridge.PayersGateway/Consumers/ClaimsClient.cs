using ClaimBridge.Contracts.Submission;

namespace ClaimBridge.PayersGateway.Consumers;

public class ClaimsClient(HttpClient http)
{
    public async Task<SubmissionDocument> GetSubmissionAsync(Guid claimId, CancellationToken ct) =>
        await http.GetFromJsonAsync<SubmissionDocument>($"internal/claims/{claimId}/submission", ct)
        ?? throw new InvalidOperationException($"Claim {claimId} not found in claims service");
}
