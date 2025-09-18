using System.Diagnostics;
using ClaimBridge.Contracts.Events;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.ServiceDefaults.Messaging;

public interface IEventOutbox
{
    /// <summary>Stages an event in the current DbContext; it is published only if SaveChanges commits.</summary>
    EventEnvelope Enqueue<T>(string topic, string key, T @event, string? correlationId = null) where T : IIntegrationEvent;
}

public sealed class EventOutbox<TDb>(TDb db, TimeProvider clock, ServiceIdentity service) : IEventOutbox
    where TDb : DbContext, IMessagingDbContext
{
    public EventEnvelope Enqueue<T>(string topic, string key, T @event, string? correlationId = null) where T : IIntegrationEvent
    {
        var envelope = EventEnvelope.Create(@event, service.Name, correlationId ?? Activity.Current?.TraceId.ToString(), clock.GetUtcNow());
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = envelope.Id,
            Topic = topic,
            Key = key,
            Type = envelope.Type,
            Payload = envelope.Serialize(),
            TraceParent = Activity.Current?.Id,
            OccurredAt = envelope.OccurredAt
        });
        return envelope;
    }
}

public sealed record ServiceIdentity(string Name);
