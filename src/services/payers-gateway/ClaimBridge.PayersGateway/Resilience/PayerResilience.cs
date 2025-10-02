using System.Diagnostics;
using System.Threading.RateLimiting;
using ClaimBridge.PayersGateway.Adapters;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace ClaimBridge.PayersGateway.Resilience;

public static class PayerResilience
{
    /// <summary>
    /// Per-payer pipeline, outermost first: throttle (token bucket), bulkhead (concurrency limiter), total timeout,
    /// retry with jitter, circuit breaker, per-attempt timeout. Each payer gets its own instances, so a slow payer
    /// can only exhaust its own permits.
    /// </summary>
    public static IHttpClientBuilder AddPayerPipeline(this IHttpClientBuilder builder, string payerId)
    {
        builder.AddHttpMessageHandler(sp => new PayerTelemetryHandler(payerId, sp.GetRequiredService<PayerHealthTracker>()));
        builder.AddResilienceHandler(payerId, (pipeline, context) =>
        {
            var o = context.ServiceProvider.GetRequiredService<IOptionsMonitor<PayerOptions>>().Get(payerId);
            var tracker = context.ServiceProvider.GetRequiredService<PayerHealthTracker>();
            Configure(pipeline, payerId, o, tracker);
        });
        return builder;
    }

    public static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> pipeline, string payerId, PayerOptions o, PayerHealthTracker tracker)
    {
        pipeline.AddRateLimiter(new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = o.RequestsPerSecond,
            TokensPerPeriod = o.RequestsPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = o.RequestsPerSecond * 5,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        }));

        pipeline.AddConcurrencyLimiter(o.MaxConcurrency, queueLimit: o.MaxConcurrency * 4);

        pipeline.AddTimeout(TimeSpan.FromSeconds(o.AttemptTimeoutSeconds * (o.RetryCount + 1) + 3));

        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = o.RetryCount,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromMilliseconds(300)
        });

        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 4,
            SamplingDuration = TimeSpan.FromSeconds(20),
            BreakDuration = TimeSpan.FromSeconds(o.BreakSeconds),
            StateProvider = tracker.StateProvider(payerId),
            OnOpened = _ =>
            {
                tracker.CircuitOpened(payerId);
                return default;
            },
            OnClosed = _ =>
            {
                tracker.CircuitClosed(payerId);
                return default;
            }
        });

        pipeline.AddTimeout(TimeSpan.FromSeconds(o.AttemptTimeoutSeconds));
    }
}

/// <summary>Outermost handler: measures what the caller experienced, retries included.</summary>
internal sealed class PayerTelemetryHandler(string payerId, PayerHealthTracker tracker) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            tracker.Record(payerId, watch.Elapsed, (int)response.StatusCode < 500);
            return response;
        }
        catch
        {
            tracker.Record(payerId, watch.Elapsed, false);
            throw;
        }
    }
}
