using System.Diagnostics;
using System.Text;
using ClaimBridge.Contracts.Events;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClaimBridge.ServiceDefaults.Messaging;

public interface IEventHandler
{
    Task HandleAsync(EventEnvelope envelope, string topic, CancellationToken ct);
}

public sealed record ConsumerRegistration<THandler>(string Group, string[] Topics, bool RetryLane);

/// <summary>
/// Consumes the main topics (or their .retry lane), dedupes on event id inside the handler's transaction,
/// and routes failures to .retry and finally .dlq.
/// </summary>
public sealed class KafkaConsumerHost<THandler, TDb>(
    ConsumerRegistration<THandler> registration,
    IServiceScopeFactory scopes,
    KafkaProducer producer,
    IOptions<KafkaOptions> options,
    TimeProvider clock,
    ILogger<KafkaConsumerHost<THandler, TDb>> logger) : BackgroundService
    where THandler : class, IEventHandler
    where TDb : DbContext, IMessagingDbContext
{
    private readonly string _consumerName = typeof(THandler).Name;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Factory.StartNew(() => RunAsync(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task RunAsync(CancellationToken ct)
    {
        var topics = registration.RetryLane
            ? registration.Topics.Select(t => t + Topics.RetrySuffix).ToArray()
            : registration.Topics;
        var group = registration.RetryLane ? registration.Group + "-retry" : registration.Group;

        using var consumer = new ConsumerBuilder<string, string>(options.Value.Apply(new ConsumerConfig
            {
                GroupId = group,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false,
                AllowAutoCreateTopics = false
            }))
            .SetErrorHandler((_, e) => logger.LogWarning("Kafka consumer {Group} error: {Reason}", group, e.Reason))
            .Build();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                consumer.Subscribe(topics);
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Subscribe to {Topics} failed: {Error}", string.Join(",", topics), ex.Message);
                await Task.Delay(5000, ct);
            }
        }

        logger.LogInformation("Consumer {Group} listening on {Topics}", group, string.Join(",", topics));
        while (!ct.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result;
            try
            {
                result = consumer.Consume(TimeSpan.FromSeconds(1));
            }
            catch (ConsumeException ex)
            {
                logger.LogWarning("Consume error on {Group}: {Reason}", group, ex.Error.Reason);
                await Task.Delay(2000, ct);
                continue;
            }
            if (result is null) continue;

            await HandleWithRetryRoutingAsync(result, ct);
            consumer.Commit(result);
        }
        consumer.Close();
    }

    private async Task HandleWithRetryRoutingAsync(ConsumeResult<string, string> result, CancellationToken ct)
    {
        var headers = result.Message.Headers?.ToDictionary(h => h.Key, h => Encoding.UTF8.GetString(h.GetValueBytes()))
                      ?? new Dictionary<string, string>();
        var baseTopic = headers.GetValueOrDefault("x-original-topic") ?? result.Topic;
        var attempt = int.TryParse(headers.GetValueOrDefault("x-attempt"), out var a) ? a : 0;

        if (registration.RetryLane && DateTimeOffset.TryParse(headers.GetValueOrDefault("x-not-before"), out var notBefore))
        {
            var wait = notBefore - clock.GetUtcNow();
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
        }

        var parent = headers.TryGetValue("traceparent", out var tp) && ActivityContext.TryParse(tp, null, out var ctx) ? ctx : default;
        using var activity = MessagingTelemetry.Source.StartActivity($"{baseTopic} process", ActivityKind.Consumer, parent);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", baseTopic);
        activity?.SetTag("messaging.consumer.group.name", registration.Group);

        EventEnvelope envelope;
        try
        {
            envelope = EventEnvelope.Deserialize(result.Message.Value);
        }
        catch (Exception ex)
        {
            logger.LogError("Unreadable message on {Topic} offset {Offset}: {Error}", result.Topic, result.Offset.Value, ex.Message);
            await ForwardAsync(baseTopic + Topics.DeadLetterSuffix, result, headers, attempt, ex.Message, ct);
            return;
        }

        try
        {
            await HandleOnceAsync(envelope, baseTopic, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var next = attempt + 1;
            var target = next >= options.Value.MaxDeliveryAttempts ? baseTopic + Topics.DeadLetterSuffix : baseTopic + Topics.RetrySuffix;
            logger.LogWarning("Handler {Handler} failed for {EventType} {EventId} (attempt {Attempt}), routing to {Target}: {Error}",
                _consumerName, envelope.Type, envelope.Id, next, target, ex.Message);
            await ForwardAsync(target, result, headers, next, ex.Message, ct);
        }
    }

    private async Task HandleOnceAsync(EventEnvelope envelope, string topic, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDb>();
        if (await db.ProcessedEvents.AnyAsync(p => p.EventId == envelope.Id && p.Consumer == _consumerName, ct))
        {
            logger.LogDebug("Skipping duplicate {EventId} for {Consumer}", envelope.Id, _consumerName);
            return;
        }

        var handler = scope.ServiceProvider.GetRequiredService<THandler>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await handler.HandleAsync(envelope, topic, ct);
        db.ProcessedEvents.Add(new ProcessedEvent { EventId = envelope.Id, Consumer = _consumerName, ProcessedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private Task ForwardAsync(string topic, ConsumeResult<string, string> result, Dictionary<string, string> headers,
        int attempt, string error, CancellationToken ct)
    {
        var baseTopic = headers.GetValueOrDefault("x-original-topic") ?? result.Topic;
        headers["x-original-topic"] = baseTopic;
        headers["x-attempt"] = attempt.ToString();
        headers["x-error"] = error.Length > 500 ? error[..500] : error;
        var backoff = TimeSpan.FromSeconds(options.Value.RetryBaseDelaySeconds * Math.Pow(2, Math.Max(0, attempt - 1)));
        headers["x-not-before"] = clock.GetUtcNow().Add(backoff).ToString("O");
        return producer.ProduceAsync(topic, result.Message.Key, result.Message.Value, headers, ct);
    }
}
