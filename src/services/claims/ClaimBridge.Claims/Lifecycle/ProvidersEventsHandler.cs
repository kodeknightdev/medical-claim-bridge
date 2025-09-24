using ClaimBridge.Claims.Application.Clients;
using ClaimBridge.Contracts.Events;
using ClaimBridge.ServiceDefaults.Messaging;

namespace ClaimBridge.Claims.Lifecycle;

/// <summary>Drops cached enrollment answers when providers changes an enrollment.</summary>
public class ProvidersEventsHandler(ProvidersClient providers) : IEventHandler
{
    public async Task HandleAsync(EventEnvelope envelope, string topic, CancellationToken ct)
    {
        if (envelope.Type != EnrollmentChanged.EventType) return;
        var e = envelope.DataAs<EnrollmentChanged>();
        await providers.InvalidateAsync(e.OrgId, e.PayerId, ct);
    }
}
