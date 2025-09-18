namespace ClaimBridge.ServiceDefaults.Messaging;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "";
    public int MaxDeliveryAttempts { get; set; } = 3;
    public int RetryBaseDelaySeconds { get; set; } = 5;
    public int OutboxPollMilliseconds { get; set; } = 500;

    public bool Enabled => !string.IsNullOrWhiteSpace(BootstrapServers);
}

public static class MessagingTelemetry
{
    public const string SourceName = "ClaimBridge.Messaging";
    public static readonly System.Diagnostics.ActivitySource Source = new(SourceName);
}
