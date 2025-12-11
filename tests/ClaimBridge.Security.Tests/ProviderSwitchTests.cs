using ClaimBridge.ServiceDefaults.Configuration;
using ClaimBridge.ServiceDefaults.Messaging;
using ClaimBridge.ServiceDefaults.Storage;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace ClaimBridge.Security.Tests;

public class ProviderSwitchTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("cb-secrets-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ConfigurationManager Config(params (string Key, string Value)[] values)
    {
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)));
        return config;
    }

    private static KafkaOptions Kafka(ConfigurationManager config)
    {
        var options = new KafkaOptions();
        KafkaOptions.Bind(config, options);
        return options;
    }

    [Fact]
    public void Kafka_is_the_default_and_adds_no_sasl()
    {
        var client = Kafka(Config(("Kafka:BootstrapServers", "kafka:9092"))).Apply(new ProducerConfig());

        client.BootstrapServers.ShouldBe("kafka:9092");
        client.SecurityProtocol.ShouldBeNull();
    }

    [Fact]
    public void Event_hubs_uses_sasl_plain_with_the_namespace_connection_string()
    {
        var client = Kafka(Config(("Kafka:BootstrapServers", "cb.servicebus.windows.net:9093"), ("Messaging:Provider", "EventHubs"),
            ("ConnectionStrings:EventHubs", "Endpoint=sb://cb.servicebus.windows.net/;SharedAccessKeyName=app;SharedAccessKey=x")))
            .Apply(new ConsumerConfig());

        client.SecurityProtocol.ShouldBe(SecurityProtocol.SaslSsl);
        client.SaslMechanism.ShouldBe(SaslMechanism.Plain);
        client.SaslUsername.ShouldBe("$ConnectionString");
        client.SaslPassword.ShouldStartWith("Endpoint=sb://");
    }

    [Fact]
    public void Event_hubs_without_a_connection_string_or_an_unknown_provider_fails_fast()
    {
        Should.Throw<InvalidOperationException>(() => Kafka(Config(("Messaging:Provider", "eventhubs"))).Apply(new ProducerConfig()));
        Should.Throw<InvalidOperationException>(() => Kafka(Config(("Messaging:Provider", "rabbit"))).Apply(new ProducerConfig()));
    }

    [Fact]
    public void Local_storage_falls_back_to_azurite_and_azure_storage_refuses_it()
    {
        BlobStoreExtensions.ConnectionString(Config()).ShouldBe("UseDevelopmentStorage=true");
        Should.Throw<InvalidOperationException>(() => BlobStoreExtensions.ConnectionString(Config(("Storage:Provider", "azure"))));
        Should.Throw<InvalidOperationException>(() => BlobStoreExtensions.ConnectionString(Config(("Storage:Provider", "azure"),
            ("ConnectionStrings:Blob", "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=a;"))));

        const string real = "DefaultEndpointsProtocol=https;AccountName=stclaimbridge;AccountKey=a;EndpointSuffix=core.windows.net";
        BlobStoreExtensions.ConnectionString(Config(("Storage:Provider", "azure"), ("ConnectionStrings:Blob", real))).ShouldBe(real);
    }

    [Fact]
    public void Key_vault_secrets_are_read_from_mounted_files()
    {
        File.WriteAllText(Path.Combine(_dir, "ConnectionStrings--ClaimsDb"), "Server=sql;Database=cb_claims\n");
        File.WriteAllText(Path.Combine(_dir, "Phi--Key"), "a2V5");
        File.WriteAllText(Path.Combine(_dir, "..data"), "csi bookkeeping");
        var config = Config(("Secrets:Provider", "keyvault"), ("Secrets:MountPath", _dir), ("Phi:Key", "from-env"));

        config.AddClaimBridgeSecrets();

        config.GetConnectionString("ClaimsDb").ShouldBe("Server=sql;Database=cb_claims");
        config["Phi:Key"].ShouldBe("a2V5");
        config.AsEnumerable().ShouldNotContain(kv => kv.Key.StartsWith(".."));
    }

    [Fact]
    public void Local_secrets_can_take_the_phi_key_from_a_file()
    {
        var keyFile = Path.Combine(_dir, "phi.key");
        File.WriteAllText(keyFile, "bG9jYWw=\n");
        var config = Config(("Phi:KeyFile", keyFile));

        config.AddClaimBridgeSecrets();

        config["Phi:Key"].ShouldBe("bG9jYWw=");
    }

    [Fact]
    public void Missing_mount_or_unknown_secrets_provider_fails_fast()
    {
        Should.Throw<InvalidOperationException>(() =>
            Config(("Secrets:Provider", "keyvault"), ("Secrets:MountPath", Path.Combine(_dir, "nope"))).AddClaimBridgeSecrets());
        Should.Throw<InvalidOperationException>(() => Config(("Secrets:Provider", "vault")).AddClaimBridgeSecrets());
    }
}
