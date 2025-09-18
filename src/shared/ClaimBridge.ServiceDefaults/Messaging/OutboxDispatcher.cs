using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClaimBridge.ServiceDefaults.Messaging;

/// <summary>Relays committed outbox rows to Kafka. At-least-once; consumers dedupe on the envelope id.</summary>
public sealed class OutboxDispatcher<TDb>(
    IServiceScopeFactory scopes,
    KafkaProducer producer,
    IOptions<KafkaOptions> options,
    TimeProvider clock,
    ILogger<OutboxDispatcher<TDb>> logger) : BackgroundService
    where TDb : DbContext, IMessagingDbContext
{
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromMilliseconds(options.Value.OutboxPollMilliseconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            var sent = 0;
            try
            {
                sent = await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Outbox dispatch failed, will retry");
            }
            if (sent < BatchSize)
                await Task.Delay(sent == 0 ? delay : TimeSpan.FromMilliseconds(50), stoppingToken);
        }
    }

    private async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDb>();
        var entity = db.Model.FindEntityType(typeof(OutboxMessage))!;
        var table = $"[{entity.GetSchema() ?? "dbo"}].[{entity.GetTableName()}]";

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // READPAST lets a second replica skip rows another one is already relaying
        var batch = await db.OutboxMessages
            .FromSqlRaw($"SELECT TOP ({BatchSize}) * FROM {table} WITH (UPDLOCK, READPAST, ROWLOCK) WHERE ProcessedAt IS NULL AND Attempts < 20 ORDER BY OccurredAt")
            .ToListAsync(ct);

        foreach (var message in batch)
        {
            var headers = new Dictionary<string, string>
            {
                ["event-id"] = message.Id.ToString(),
                ["event-type"] = message.Type
            };
            if (message.TraceParent is not null) headers["traceparent"] = message.TraceParent;

            using var activity = MessagingTelemetry.Source.StartActivity($"{message.Topic} publish", ActivityKind.Producer,
                message.TraceParent is null ? default : ActivityContext.TryParse(message.TraceParent, null, out var parent) ? parent : default);
            activity?.SetTag("messaging.system", "kafka");
            activity?.SetTag("messaging.destination.name", message.Topic);
            if (activity is not null) headers["traceparent"] = activity.Id!;

            try
            {
                await producer.ProduceAsync(message.Topic, message.Key, message.Payload, headers, ct);
                message.ProcessedAt = clock.GetUtcNow();
            }
            catch (Exception ex)
            {
                message.Attempts++;
                message.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                logger.LogWarning("Outbox message {MessageId} to {Topic} failed (attempt {Attempt}): {Error}",
                    message.Id, message.Topic, message.Attempts, ex.Message);
                break;
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return batch.Count;
    }
}
