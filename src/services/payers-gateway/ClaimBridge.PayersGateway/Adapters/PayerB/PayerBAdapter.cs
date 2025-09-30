using System.Globalization;
using ClaimBridge.Contracts.Reference;
using ClaimBridge.Contracts.Submission;

namespace ClaimBridge.PayersGateway.Adapters.PayerB;

/// <summary>
/// Bluepeak Mutual: claims go out as simplified 837P batch files through the SFTP-style blob folder;
/// acknowledgements and remittances come back as files. Eligibility and status use Bluepeak's small JSON API.
/// </summary>
public class PayerBAdapter(HttpClient http) : IPayerAdapter
{
    public string PayerId => SeedIds.PayerB;
    public string Channel => "x12-files";

    private sealed record BpEligibility(string EligibilityCode, string? PlanDescription, string? EffectiveDate, string? TermDate,
        decimal? OfficeCopay, decimal? DeductibleLeft);

    private sealed record BpStatus(string StatusCategory, string? StatusCode, string? PayerClaimNumber, string? Message);

    public Task<SubmitResult> SubmitAsync(SubmissionDocument claim, int attempt, CancellationToken ct) =>
        Task.FromResult(SubmitResult.Queued());

    public async Task<EligibilityAnswer> CheckEligibilityAsync(EligibilityQuery query, CancellationToken ct)
    {
        using var response = await Call(() => http.PostAsJsonAsync("payer-b/eligibility",
            new { subscriberId = query.MemberId, birthDate = query.DateOfBirth.ToString("yyyyMMdd"), serviceDate = query.DateOfService.ToString("yyyyMMdd") }, ct));
        var body = await response.Content.ReadFromJsonAsync<BpEligibility>(ct);
        if (body is null) return new EligibilityAnswer("Unknown", null, null, null, null, null);

        // 271 EB01 style: 1 = active coverage, 6 = inactive
        var status = body.EligibilityCode switch { "1" => "Active", "6" => "Inactive", _ => "Unknown" };
        return new EligibilityAnswer(status, Date(body.EffectiveDate), Date(body.TermDate), body.PlanDescription, body.OfficeCopay,
            body.DeductibleLeft);
    }

    public async Task<StatusAnswer> InquireStatusAsync(StatusQuery query, CancellationToken ct)
    {
        using var response = await Call(() => http.PostAsJsonAsync("payer-b/claim-status",
            new { patientControlNumber = query.ClaimNumber, payerClaimNumber = query.PayerClaimId }, ct));
        var body = await response.Content.ReadFromJsonAsync<BpStatus>(ct);
        if (body is null) return new StatusAnswer("not-found", null, null, null, null);

        return body.StatusCategory switch
        {
            "A1" or "A2" => new StatusAnswer("received", null, body.StatusCode, body.Message, body.PayerClaimNumber),
            "A3" or "A6" or "A7" => new StatusAnswer("rejected", null, body.StatusCode, body.Message, body.PayerClaimNumber),
            "P1" or "P2" => new StatusAnswer("pended", null, body.StatusCode, body.Message ?? "Pended", body.PayerClaimNumber),
            "P3" or "P4" => new StatusAnswer("in-process", null, body.StatusCode, body.Message, body.PayerClaimNumber),
            "F1" => new StatusAnswer("finalized", "paid", body.StatusCode, body.Message, body.PayerClaimNumber),
            "F2" => new StatusAnswer("finalized", "denied", body.StatusCode, body.Message, body.PayerClaimNumber),
            "F3" => new StatusAnswer("finalized", "partial", body.StatusCode, body.Message, body.PayerClaimNumber),
            _ => new StatusAnswer("not-found", null, body.StatusCode, body.Message, null)
        };
    }

    private async Task<HttpResponseMessage> Call(Func<Task<HttpResponseMessage>> call)
    {
        try
        {
            var response = await call();
            if ((int)response.StatusCode >= 500) throw new PayerUnavailableException(PayerId, $"HTTP {(int)response.StatusCode}");
            return response;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayerUnavailableException(PayerId, ex.Message, ex);
        }
    }

    private static DateOnly? Date(string? s) =>
        DateOnly.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
