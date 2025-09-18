namespace ClaimBridge.Contracts.Events;

public static class Topics
{
    public const string BatchUploaded = "claims.batch-uploaded";
    public const string ClaimReceived = "claims.claim-received";
    public const string ClaimScrubbed = "claims.claim-scrubbed";
    public const string ClaimReady = "claims.claim-ready";
    public const string ClaimStatusChanged = "claims.status-changed";

    public const string ClaimSubmitted = "payers.claim-submitted";
    public const string AckReceived = "payers.ack-received";
    public const string PayerStatusUpdated = "payers.status-updated";
    public const string RemittanceReceived = "payers.remittance-received";

    public const string PaymentPosted = "remittance.payment-posted";
    public const string ClaimDenied = "remittance.claim-denied";
    public const string ReversalPosted = "remittance.reversal-posted";

    public const string EnrollmentChanged = "providers.enrollment-changed";
    public const string WebhookSubscriptionChanged = "providers.webhook-subscription-changed";

    public const string ModelTrained = "denial-predictor.model-trained";

    public const string RetrySuffix = ".retry";
    public const string DeadLetterSuffix = ".dlq";

    public static readonly string[] All =
    [
        BatchUploaded, ClaimReceived, ClaimScrubbed, ClaimReady, ClaimStatusChanged,
        ClaimSubmitted, AckReceived, PayerStatusUpdated, RemittanceReceived,
        PaymentPosted, ClaimDenied, ReversalPosted,
        EnrollmentChanged, WebhookSubscriptionChanged, ModelTrained
    ];
}
