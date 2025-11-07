using ClaimBridge.Claims.Application;
using ClaimBridge.Claims.Data;
using ClaimBridge.Claims.Domain;
using ClaimBridge.Contracts.Events;
using ClaimBridge.ServiceDefaults.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace ClaimBridge.Claims.Tests;

public sealed class RecordingOutbox : IEventOutbox
{
    public List<(string Topic, string Key, object Event)> Messages { get; } = [];

    public EventEnvelope Enqueue<T>(string topic, string key, T @event, string? correlationId = null) where T : IIntegrationEvent
    {
        Messages.Add((topic, key, @event));
        return EventEnvelope.Create(@event, "test");
    }

    public IEnumerable<T> Of<T>() => Messages.Select(m => m.Event).OfType<T>();
}

public sealed class TestHarness
{
    public ClaimsDb Db { get; }
    public RecordingOutbox Outbox { get; } = new();
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2025, 11, 10, 9, 0, 0, TimeSpan.Zero));
    public ClaimTransitions Transitions { get; }

    public TestHarness()
    {
        Db = new ClaimsDb(new DbContextOptionsBuilder<ClaimsDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        var metrics = new ClaimsMetrics(new TestMeterFactory());
        Transitions = new ClaimTransitions(Outbox, new ClaimReadModelProjector(Db), metrics, Clock);
    }

    public async Task<Claim> AddClaimAsync(ClaimStatus status, string payer = "PAYER-A", decimal charge = 300)
    {
        var claim = new Claim
        {
            Id = Guid.NewGuid(), ClaimNumber = "CB25" + Random.Shared.Next(1000000, 9999999), OrgId = Guid.NewGuid(), PayerId = payer,
            Status = status, TotalCharge = charge, CreatedAt = Clock.GetUtcNow(), StatusChangedAt = Clock.GetUtcNow(),
            Lines = [new ServiceLine { LineNumber = 1, ProcedureCode = "99214", Units = 1, Charge = charge, ServiceDate = new DateOnly(2025, 11, 1) }]
        };
        Db.Claims.Add(claim);
        Db.Lifecycles.Add(new ClaimLifecycleState { ClaimId = claim.Id });
        await Db.SaveChangesAsync();
        return claim;
    }
}

public sealed class TestMeterFactory : System.Diagnostics.Metrics.IMeterFactory
{
    public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) => new(options);
    public void Dispose() { }
}
