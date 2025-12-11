using Confluent.Kafka;
using Microsoft.Extensions.Configuration;

namespace ClaimBridge.ServiceDefaults.Messaging;

public sealed class KafkaOptions
{
    public const string Kafka = "kafka";
    public const string EventHubs = "eventhubs";

    public string BootstrapServers { get; set; } = "";
    public int MaxDeliveryAttempts { get; set; } = 3;
    public int RetryBaseDelaySeconds { get; set; } = 5;
    public int OutboxPollMilliseconds { get; set; } = 500;

    /// <summary><c>Messaging:Provider</c>: kafka (default) or eventhubs.</summary>
    public string Provider { get; set; } = Kafka;

    /// <summary>Event Hubs namespace connection string (<c>ConnectionStrings:EventHubs</c>); eventhubs provider only.</summary>
    public string? EventHubsConnectionString { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(BootstrapServers);

    public static void Bind(IConfiguration config, KafkaOptions options)
    {
        config.GetSection("Kafka").Bind(options);
        options.Provider = (config["Messaging:Provider"] ?? Kafka).ToLowerInvariant();
        options.EventHubsConnectionString = config.GetConnectionString("EventHubs");
    }

    /// <summary>Fills in the broker address and, for Event Hubs, the SASL settings its Kafka endpoint expects.</summary>
    public T Apply<T>(T client) where T : ClientConfig
    {
        client.BootstrapServers = BootstrapServers;
        switch (Provider)
        {
            case Kafka:
                break;
            case EventHubs:
                if (string.IsNullOrWhiteSpace(EventHubsConnectionString))
                    throw new InvalidOperationException("Messaging:Provider is eventhubs but ConnectionStrings:EventHubs is empty.");
                client.SecurityProtocol = SecurityProtocol.SaslSsl;
                client.SaslMechanism = SaslMechanism.Plain;
                client.SaslUsername = "$ConnectionString";
                client.SaslPassword = EventHubsConnectionString;
                break;
            default:
                throw new InvalidOperationException($"Unknown Messaging:Provider '{Provider}'. Use kafka or eventhubs.");
        }
        return client;
    }
}

public static class MessagingTelemetry
{
    public const string SourceName = "ClaimBridge.Messaging";
    public static readonly System.Diagnostics.ActivitySource Source = new(SourceName);
}
