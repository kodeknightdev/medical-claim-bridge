using System.Globalization;
using ClaimBridge.Contracts.Reference;
using ClaimBridge.Contracts.Submission;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace ClaimBridge.PayersGateway.Adapters.PayerC;

/// <summary>Cascade Care: JSON API that times out and returns 5xx regularly. All calls go through its resilience pipeline.</summary>
public class PayerCAdapter(HttpClient http) : IPayerAdapter
{
    public string PayerId => SeedIds.PayerC;
    public string Channel => "json-api";

    private sealed record CcLine(string Cpt, string ModifierList, int Units, string Charge, string ServiceDate, string Pos, string DxLinks);

    private sealed record CcSubmit(string ExternalRef, string ClaimType, string? ReplacesRef, string MemberNumber, string LastName,
        string FirstName, string BirthDate, string Gender, string ProviderNpi, string ProviderTin, string? AttendingNpi,
        string? PreauthNumber, string DiagnosisList, CcLine[] Lines);

    private sealed record CcSubmitResponse(string Result, string? Ref, string[]? Messages);

    private sealed record CcEligibility(string Coverage, string? Product, string? From, string? Thru, string? Copay, string? DeductibleBalance);

    private sealed record CcStatus(string State, string? Detail, string? Ref);

    public async Task<SubmitResult> SubmitAsync(SubmissionDocument claim, int attempt, CancellationToken ct)
    {
        var s = claim.Subscriber;
        var payload = new CcSubmit(
            ExternalRef: claim.ClaimNumber,
            ClaimType: claim.FrequencyCode switch { "7" => "REPLACEMENT", "8" => "VOID", _ => "ORIGINAL" },
            ReplacesRef: claim.OriginalPayerClaimId,
            MemberNumber: s.MemberId, LastName: s.LastName, FirstName: s.FirstName,
            BirthDate: s.DateOfBirth.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture), Gender: s.Gender,
            ProviderNpi: claim.Billing.Npi, ProviderTin: claim.Billing.TaxId, AttendingNpi: claim.RenderingNpi,
            PreauthNumber: claim.PriorAuthNumber,
            DiagnosisList: string.Join(';', claim.Diagnoses),
            Lines: [.. claim.Lines.Select(l => new CcLine(l.ProcedureCode, string.Join(',', l.Modifiers), (int)l.Units,
                l.Charge.ToString("0.00", CultureInfo.InvariantCulture), l.ServiceDate.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
                l.PlaceOfService, string.Join(',', l.DxPointers)))]);

        using var response = await Guard(() => http.PostAsJsonAsync("payer-c/api/submit", payload, ct));
        var body = await response.Content.ReadFromJsonAsync<CcSubmitResponse>(ct)
                   ?? throw new PayerUnavailableException(PayerId, "empty response");
        return body.Result == "OK"
            ? new SubmitResult(SubmitOutcome.Accepted, body.Ref)
            : new SubmitResult(SubmitOutcome.Rejected, body.Ref, body.Messages ?? ["Rejected by Cascade"]);
    }

    public async Task<EligibilityAnswer> CheckEligibilityAsync(EligibilityQuery query, CancellationToken ct)
    {
        using var response = await Guard(() => http.GetAsync(
            $"payer-c/api/eligibility?member={Uri.EscapeDataString(query.MemberId)}&dob={query.DateOfBirth:MM/dd/yyyy}&dos={query.DateOfService:MM/dd/yyyy}", ct));
        var body = await response.Content.ReadFromJsonAsync<CcEligibility>(ct);
        if (body is null) return new EligibilityAnswer("Unknown", null, null, null, null, null);
        return new EligibilityAnswer(body.Coverage == "ACTIVE" ? "Active" : body.Coverage == "TERMED" ? "Inactive" : "Unknown",
            Date(body.From), Date(body.Thru), body.Product, Money(body.Copay), Money(body.DeductibleBalance));
    }

    public async Task<StatusAnswer> InquireStatusAsync(StatusQuery query, CancellationToken ct)
    {
        using var response = await Guard(() => http.GetAsync($"payer-c/api/status/{Uri.EscapeDataString(query.ClaimNumber)}", ct));
        var body = await response.Content.ReadFromJsonAsync<CcStatus>(ct);
        return body?.State switch
        {
            "LOGGED" => new StatusAnswer("received", null, "A1", body.Detail, body.Ref),
            "BOUNCED" => new StatusAnswer("rejected", null, "A3", body.Detail, body.Ref),
            "ON_HOLD" => new StatusAnswer("pended", null, "P1", body.Detail, body.Ref),
            "WORKING" => new StatusAnswer("in-process", null, "A2", body.Detail, body.Ref),
            "PAID" => new StatusAnswer("finalized", "paid", "F1", body.Detail, body.Ref),
            "PARTPAID" => new StatusAnswer("finalized", "partial", "F1", body.Detail, body.Ref),
            "DECLINED" => new StatusAnswer("finalized", "denied", "F2", body.Detail, body.Ref),
            _ => new StatusAnswer("not-found", null, null, body?.Detail, null)
        };
    }

    private async Task<HttpResponseMessage> Guard(Func<Task<HttpResponseMessage>> call)
    {
        try
        {
            var response = await call();
            if ((int)response.StatusCode >= 500) throw new PayerUnavailableException(PayerId, $"HTTP {(int)response.StatusCode}");
            return response;
        }
        catch (BrokenCircuitException ex)
        {
            throw new PayerUnavailableException(PayerId, "circuit open", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutRejectedException
                                       or RateLimiterRejectedException)
        {
            throw new PayerUnavailableException(PayerId, ex.GetType().Name, ex);
        }
    }

    private static DateOnly? Date(string? s) =>
        DateOnly.TryParseExact(s, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static decimal? Money(string? s) =>
        decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
}
