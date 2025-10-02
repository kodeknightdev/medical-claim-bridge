using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Polly.CircuitBreaker;

namespace ClaimBridge.PayersGateway.Resilience;

public sealed record PayerHealthSnapshot(
    string PayerId,
    string CircuitState,
    int Requests,
    double ErrorRate,
    double? P50Ms,
    double? P95Ms,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    DateTimeOffset? CircuitOpenedAt);

/// <summary>Rolling five-minute view of each payer: latency, error rate and circuit state for the ops dashboard.</summary>
public sealed class PayerHealthTracker
{
    public const string MeterName = "ClaimBridge.PayersGateway";
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, PayerState> _payers = new();
    private readonly TimeProvider _clock;
    private readonly Histogram<double> _latency;
    private readonly Counter<long> _errors;
    private readonly Counter<long> _circuitOpened;

    public PayerHealthTracker(IMeterFactory meters, TimeProvider clock)
    {
        _clock = clock;
        var meter = meters.Create(MeterName);
        _latency = meter.CreateHistogram<double>("payer.request.duration", "ms", "Payer call latency including retries");
        _errors = meter.CreateCounter<long>("payer.request.errors", description: "Failed payer calls");
        _circuitOpened = meter.CreateCounter<long>("payer.circuit.opened", description: "Times a payer circuit opened");
        meter.CreateObservableGauge("payer.circuit.state", () => _payers.Select(p =>
            new Measurement<int>((int)p.Value.StateProvider.CircuitState, new KeyValuePair<string, object?>("payer", p.Key))),
            description: "0 closed, 1 open, 2 half-open, 3 isolated");
    }

    public CircuitBreakerStateProvider StateProvider(string payerId) => Get(payerId).StateProvider;

    public void Record(string payerId, TimeSpan elapsed, bool success)
    {
        var state = Get(payerId);
        var now = _clock.GetUtcNow();
        state.Samples.Enqueue((now, elapsed.TotalMilliseconds, success));
        while (state.Samples.TryPeek(out var head) && now - head.At > Window) state.Samples.TryDequeue(out _);

        if (success) state.LastSuccessAt = now;
        else
        {
            state.LastFailureAt = now;
            _errors.Add(1, new KeyValuePair<string, object?>("payer", payerId));
        }
        _latency.Record(elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("payer", payerId),
            new KeyValuePair<string, object?>("outcome", success ? "ok" : "error"));
    }

    public void CircuitOpened(string payerId)
    {
        Get(payerId).OpenedAt = _clock.GetUtcNow();
        _circuitOpened.Add(1, new KeyValuePair<string, object?>("payer", payerId));
    }

    public void CircuitClosed(string payerId) => Get(payerId).OpenedAt = null;

    public PayerHealthSnapshot Snapshot(string payerId)
    {
        var state = Get(payerId);
        var samples = state.Samples.ToArray();
        var latencies = samples.Where(s => s.Ok).Select(s => s.Ms).Order().ToArray();
        return new PayerHealthSnapshot(payerId, state.StateProvider.CircuitState.ToString(), samples.Length,
            samples.Length == 0 ? 0 : Math.Round(samples.Count(s => !s.Ok) / (double)samples.Length, 3),
            Percentile(latencies, 0.50), Percentile(latencies, 0.95), state.LastSuccessAt, state.LastFailureAt, state.OpenedAt);
    }

    private PayerState Get(string payerId) => _payers.GetOrAdd(payerId, _ => new PayerState());

    private static double? Percentile(double[] sorted, double p) =>
        sorted.Length == 0 ? null : Math.Round(sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)], 1);

    private sealed class PayerState
    {
        public CircuitBreakerStateProvider StateProvider { get; } = new();
        public ConcurrentQueue<(DateTimeOffset At, double Ms, bool Ok)> Samples { get; } = new();
        public DateTimeOffset? LastSuccessAt { get; set; }
        public DateTimeOffset? LastFailureAt { get; set; }
        public DateTimeOffset? OpenedAt { get; set; }
    }
}
