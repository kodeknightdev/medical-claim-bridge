using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClaimBridge.ServiceDefaults.Messaging;

public sealed class MessagingBuilder<TDb>(IServiceCollection services, KafkaOptions options)
    where TDb : DbContext, IMessagingDbContext
{
    /// <summary>Registers a handler on the main topics plus a delayed retry lane.</summary>
    public MessagingBuilder<TDb> Consume<THandler>(string group, params string[] topics) where THandler : class, IEventHandler
    {
        services.AddScoped<THandler>();
        if (!options.Enabled) return this;

        services.AddSingleton<IHostedService>(sp => ActivatorUtilities.CreateInstance<KafkaConsumerHost<THandler, TDb>>(sp,
            new ConsumerRegistration<THandler>(group, topics, RetryLane: false)));
        services.AddSingleton<IHostedService>(sp => ActivatorUtilities.CreateInstance<KafkaConsumerHost<THandler, TDb>>(sp,
            new ConsumerRegistration<THandler>(group, topics, RetryLane: true)));
        return this;
    }
}

public static class MessagingExtensions
{
    public static MessagingBuilder<TDb> AddClaimBridgeMessaging<TDb>(this IServiceCollection services, IConfiguration config, string serviceName)
        where TDb : DbContext, IMessagingDbContext
    {
        var options = config.GetSection("Kafka").Get<KafkaOptions>() ?? new KafkaOptions();
        services.Configure<KafkaOptions>(config.GetSection("Kafka"));
        services.AddSingleton(new ServiceIdentity(serviceName));
        services.AddSingleton<KafkaProducer>();
        services.AddScoped<IEventOutbox, EventOutbox<TDb>>();

        if (options.Enabled)
        {
            services.AddHostedService<OutboxDispatcher<TDb>>();
            services.AddHealthChecks().AddKafka(new Confluent.Kafka.ProducerConfig
            {
                BootstrapServers = options.BootstrapServers,
                MessageTimeoutMs = 3000
            }, topic: "healthchecks", name: "kafka", tags: [ServiceDefaultsExtensions.ReadyTag]);
        }
        return new MessagingBuilder<TDb>(services, options);
    }
}
