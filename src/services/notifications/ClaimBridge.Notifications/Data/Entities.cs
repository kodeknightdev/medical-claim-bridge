namespace ClaimBridge.Notifications.Data;

public enum DeliveryStatus { Pending, Succeeded, Abandoned }

public class WebhookDelivery
{
    public Guid Id { get; set; }
    public Guid SubscriptionId { get; set; }
    public Guid OrgId { get; set; }
    public Guid SourceEventId { get; set; }
    public string EventType { get; set; } = "";
    public string Url { get; set; } = "";
    public string Payload { get; set; } = "";
    public DeliveryStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public int? LastStatusCode { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public List<DeliveryAttempt> AttemptLog { get; set; } = [];
}

public class DeliveryAttempt
{
    public long Id { get; set; }
    public Guid DeliveryId { get; set; }
    public int Number { get; set; }
    public DateTimeOffset At { get; set; }
    public int? StatusCode { get; set; }
    public int DurationMs { get; set; }
    public string? Error { get; set; }
    public bool Manual { get; set; }
}

public class SubscriptionHealth
{
    public Guid SubscriptionId { get; set; }
    public Guid OrgId { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public DateTimeOffset? LastFailureAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
}

public class EmailLog
{
    public long Id { get; set; }
    public Guid OrgId { get; set; }
    public string To { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Template { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public bool Sent { get; set; }
    public string? Error { get; set; }
}

public class AuditLogEntry
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
}
