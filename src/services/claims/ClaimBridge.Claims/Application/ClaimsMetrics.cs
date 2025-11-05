using System.Diagnostics.Metrics;
using ClaimBridge.Claims.Domain;

namespace ClaimBridge.Claims.Application;

public sealed class ClaimsMetrics
{
    private readonly Counter<long> _received;
    private readonly Counter<long> _transitions;

    public ClaimsMetrics(IMeterFactory meters)
    {
        var meter = meters.Create("ClaimBridge.Claims");
        _received = meter.CreateCounter<long>("claims.received", description: "Claims accepted at intake; rate gives claims per minute");
        _transitions = meter.CreateCounter<long>("claims.transitions", description: "Lifecycle transitions by target status");
    }

    public void Received(ClaimSource source, string payerId) =>
        _received.Add(1, new("source", source.ToString()), new("payer", payerId));

    public void Transitioned(ClaimStatus to) => _transitions.Add(1, new KeyValuePair<string, object?>("status", to.ToString()));
}
