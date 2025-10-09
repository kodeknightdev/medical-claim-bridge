using System.Text.Json;
using ClaimBridge.Contracts.Events;

namespace ClaimBridge.Notifications.Webhooks;

/// <summary>Maps internal events to the public webhook vocabulary. Payloads never include patient names or DOB.</summary>
public static class WebhookEvents
{
    public const string ClaimAccepted = "claim.accepted";
    public const string ClaimRejected = "claim.rejected";
    public const string ClaimPaid = "claim.paid";
    public const string ClaimDenied = "claim.denied";
    public const string RemittancePosted = "remittance.posted";

    public static string? ForStatus(string toStatus) => toStatus switch
    {
        "AcceptedByPayer" => ClaimAccepted,
        "RejectedByPayer" => ClaimRejected,
        "Paid" or "PartiallyPaid" => ClaimPaid,
        "Denied" => ClaimDenied,
        _ => null
    };

    public static string Payload(Guid deliveryId, string type, DateTimeOffset at, object data) =>
        JsonSerializer.Serialize(new { id = deliveryId, type, createdAt = at, data }, EventEnvelope.Json);

    public static object ClaimData(ClaimStatusChanged e) => new
    {
        claimId = e.ClaimId,
        claimNumber = e.ClaimNumber,
        payerId = e.PayerId,
        status = e.ToStatus,
        reason = e.Reason,
        totalCharge = e.TotalCharge,
        paidAmount = e.PaidAmount,
        patientResponsibility = e.PatientResponsibility,
        denialCategory = e.DenialCategory,
        carc = e.CarcCode
    };

    public static object RemittanceData(PaymentPosted e) => new
    {
        claimId = e.ClaimId,
        claimNumber = e.ClaimNumber,
        payerId = e.PayerId,
        remittanceId = e.RemittanceId,
        chargeAmount = e.ChargeAmount,
        paidAmount = e.PaidAmount,
        allowedAmount = e.AllowedAmount,
        patientResponsibility = e.PatientResponsibility,
        partiallyPaid = e.PartiallyPaid,
        eftTraceNumber = e.EftTraceNumber,
        paymentDate = e.PaymentDate,
        adjustments = e.Adjustments
    };
}
