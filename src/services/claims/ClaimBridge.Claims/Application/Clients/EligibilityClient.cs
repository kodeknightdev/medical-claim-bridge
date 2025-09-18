namespace ClaimBridge.Claims.Application.Clients;

public sealed record EligibilityResult(
    string Status,
    DateOnly? CoverageStart = null,
    DateOnly? CoverageEnd = null,
    string? PlanName = null,
    decimal? Copay = null,
    decimal? DeductibleRemaining = null,
    bool FromCache = false)
{
    public static readonly EligibilityResult Unknown = new("Unknown");
}

public class EligibilityClient(HttpClient http, ILogger<EligibilityClient> logger)
{
    public async Task<EligibilityResult> CheckAsync(string payerId, string memberId, DateOnly dob, DateOnly dateOfService, CancellationToken ct)
    {
        try
        {
            var response = await http.PostAsJsonAsync("internal/eligibility",
                new { payerId, memberId, dateOfBirth = dob, dateOfService }, ct);
            if (!response.IsSuccessStatusCode) return EligibilityResult.Unknown;
            return await response.Content.ReadFromJsonAsync<EligibilityResult>(ct) ?? EligibilityResult.Unknown;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // eligibility is advisory for the scrub; an outage downgrades it to a warning instead of blocking intake
            logger.LogWarning("Eligibility check for {PayerId} unavailable: {Error}", payerId, ex.Message);
            return EligibilityResult.Unknown;
        }
    }
}
