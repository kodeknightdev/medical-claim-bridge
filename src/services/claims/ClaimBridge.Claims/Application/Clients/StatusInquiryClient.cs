namespace ClaimBridge.Claims.Application.Clients;

public class StatusInquiryClient(HttpClient http, ILogger<StatusInquiryClient> logger)
{
    public async Task<bool> RequestAsync(Guid claimId, string claimNumber, string payerId, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("internal/status-inquiries", new { claimId, claimNumber, payerId }, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Status inquiry for {ClaimNumber} failed: {Error}", claimNumber, ex.Message);
            return false;
        }
    }
}
