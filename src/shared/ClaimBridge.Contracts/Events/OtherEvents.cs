namespace ClaimBridge.Contracts.Events;

public sealed record EnrollmentChanged(Guid OrgId, string PayerId, string Status) : IIntegrationEvent
{
    public static string EventType => "providers.enrollment-changed";
    public static int SchemaVersion => 1;
}

public sealed record WebhookSubscriptionChanged(Guid SubscriptionId, Guid OrgId, string Change) : IIntegrationEvent
{
    public static string EventType => "providers.webhook-subscription-changed";
    public static int SchemaVersion => 1;
}
