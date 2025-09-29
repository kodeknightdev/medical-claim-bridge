using System.Globalization;
using ClaimBridge.Contracts.Reference;
using ClaimBridge.Contracts.Submission;
using Microsoft.Extensions.Options;

namespace ClaimBridge.PayersGateway.Adapters.PayerA;

/// <summary>Northwind Health Plan: JSON API with a synchronous ack and an asynchronous adjudication callback.</summary>
public class PayerAAdapter(HttpClient http, IOptionsMonitor<PayerOptions> options) : IPayerAdapter
{
    public string PayerId => SeedIds.PayerA;
    public string Channel => "json-api";

    public async Task<SubmitResult> SubmitAsync(SubmissionDocument claim, int attempt, CancellationToken ct)
    {
        var payload = new NwClaim(
            SubmitterId: options.Get(PayerId).SubmitterId,
            ClaimRef: claim.ClaimNumber,
            Frequency: claim.FrequencyCode,
            OriginalTrackingId: claim.OriginalPayerClaimId,
            Member: new NwMember(claim.Subscriber.MemberId, claim.Subscriber.FirstName, claim.Subscriber.LastName,
                claim.Subscriber.DateOfBirth.ToString("yyyy-MM-dd"), claim.Subscriber.Gender),
            Billing: new NwProvider(claim.Billing.Npi, claim.Billing.TaxId, claim.Billing.Name),
            RenderingNpi: claim.RenderingNpi,
            AuthNumber: claim.PriorAuthNumber,
            Dx: [.. claim.Diagnoses],
            Services: [.. claim.Lines.Select(l => new NwService(l.ProcedureCode, [.. l.Modifiers], l.Units, l.Charge,
                l.ServiceDate.ToString("yyyy-MM-dd"), l.PlaceOfService, [.. l.DxPointers]))],
            Total: claim.TotalCharge,
            CallbackUrl: options.Get(PayerId).CallbackUrl);

        using var response = await Send(() => http.PostAsJsonAsync("payer-a/v1/claims", payload, ct));
        var body = await response.Content.ReadFromJsonAsync<NwSubmitResponse>(ct)
                   ?? throw new PayerUnavailableException(PayerId, "empty response");

        return body.Status switch
        {
            "ACCEPTED" => new SubmitResult(SubmitOutcome.Accepted, body.TrackingId),
            "REJECTED" => new SubmitResult(SubmitOutcome.Rejected, body.TrackingId,
                (body.Errors ?? []).Select(e => $"{e.Code}: {e.Text}").ToList()),
            _ => throw new PayerUnavailableException(PayerId, $"unexpected status {body.Status}")
        };
    }

    public async Task<EligibilityAnswer> CheckEligibilityAsync(EligibilityQuery query, CancellationToken ct)
    {
        using var response = await Send(() => http.PostAsJsonAsync("payer-a/v1/eligibility",
            new NwEligibilityRequest(query.MemberId, query.DateOfBirth.ToString("yyyy-MM-dd"), query.DateOfService.ToString("yyyy-MM-dd")), ct));
        var body = await response.Content.ReadFromJsonAsync<NwEligibilityResponse>(ct);
        return body is null
            ? new EligibilityAnswer("Unknown", null, null, null, null, null)
            : new EligibilityAnswer(body.Active ? "Active" : "Inactive", Date(body.CoverageStart), Date(body.CoverageEnd), body.Plan,
                body.Copay, body.DeductibleRemaining);
    }

    public async Task<StatusAnswer> InquireStatusAsync(StatusQuery query, CancellationToken ct)
    {
        using var response = await Send(() => http.PostAsJsonAsync("payer-a/v1/claim-status",
            new NwStatusRequest(query.ClaimNumber, query.PayerClaimId), ct));
        var body = await response.Content.ReadFromJsonAsync<NwStatusResponse>(ct);
        return body is null ? new StatusAnswer("not-found", null, null, null, null) : Translate(body.Status, body.Outcome, body.Note, body.TrackingId);
    }

    /// <summary>Northwind status words mapped onto our 277-style vocabulary.</summary>
    public static StatusAnswer Translate(string status, string? outcome, string? note, string? trackingId) => status switch
    {
        "RECEIVED" => new("received", null, "A1", note, trackingId),
        "REJECTED" => new("rejected", null, "A3", note, trackingId),
        "PENDED" => new("pended", null, "P1", note ?? "Pended for review", trackingId),
        "IN_REVIEW" => new("in-process", null, "A2", note, trackingId),
        "FINALIZED" => new("finalized", outcome switch { "PAID" => "paid", "PARTIAL" => "partial", "DENIED" => "denied", _ => null },
            "F1", note, trackingId),
        "UNKNOWN" => new("not-found", null, "A4", note, trackingId),
        _ => new("in-process", null, null, $"{status}: {note}", trackingId)
    };

    private async Task<HttpResponseMessage> Send(Func<Task<HttpResponseMessage>> call)
    {
        try
        {
            var response = await call();
            if ((int)response.StatusCode >= 500)
                throw new PayerUnavailableException(PayerId, $"HTTP {(int)response.StatusCode}");
            return response;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or Polly.CircuitBreaker.BrokenCircuitException
                                       or Polly.Timeout.TimeoutRejectedException or Polly.RateLimiting.RateLimiterRejectedException)
        {
            throw new PayerUnavailableException(PayerId, ex.GetType().Name + ": " + ex.Message, ex);
        }
    }

    private static DateOnly? Date(string? s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
