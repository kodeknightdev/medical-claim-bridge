using ClaimBridge.Contracts.Reference;
using ClaimBridge.Contracts.Submission;
using ClaimBridge.PayersGateway.Adapters;
using ClaimBridge.PayersGateway.Adapters.PayerA;
using ClaimBridge.PayersGateway.Adapters.PayerB;
using ClaimBridge.PayersGateway.Adapters.PayerC;
using ClaimBridge.PayersGateway.Resilience;
using Microsoft.Extensions.DependencyInjection;
using WireMock.Server;

namespace ClaimBridge.PayersGateway.Tests;

/// <summary>Adapters wired exactly as in Program.cs (including the per-payer pipelines) against a WireMock payer.</summary>
public sealed class AdapterHost : IDisposable
{
    public WireMockServer Payer { get; } = WireMockServer.Start();
    public ServiceProvider Services { get; }

    public AdapterHost(Action<PayerOptions>? tweak = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<PayerHealthTracker>();
        foreach (var payer in SeedIds.Payers)
            services.Configure<PayerOptions>(payer, o =>
            {
                o.BaseUrl = Payer.Url + "/";
                o.AttemptTimeoutSeconds = 1;
                o.BreakSeconds = 30;
                o.CallbackUrl = "http://gateway/callback";
                tweak?.Invoke(o);
            });
        services.AddHttpClient<PayerAAdapter>(c => c.BaseAddress = new Uri(Payer.Url + "/")).AddPayerPipeline(SeedIds.PayerA);
        services.AddHttpClient<PayerBAdapter>(c => c.BaseAddress = new Uri(Payer.Url + "/")).AddPayerPipeline(SeedIds.PayerB);
        services.AddHttpClient<PayerCAdapter>(c => c.BaseAddress = new Uri(Payer.Url + "/")).AddPayerPipeline(SeedIds.PayerC);
        Services = services.BuildServiceProvider();
    }

    public T Adapter<T>() where T : IPayerAdapter => Services.GetRequiredService<T>();

    public PayerHealthTracker Health => Services.GetRequiredService<PayerHealthTracker>();

    public static SubmissionDocument Claim(string payer = "PAYER-A", string frequency = "1") => new(
        Guid.NewGuid(), "CB250100200", 1, frequency, frequency == "1" ? null : "CB250100100", frequency == "1" ? null : "NW100100",
        Guid.NewGuid(), payer,
        new BillingProvider("Sunrise Family Clinic", "1999000015", "00-0001001", "100 Demo Way", "Springfield", "IL", "62701"), "1999000114",
        new SubscriberInfo("NWH1000002", "Jamie", "Example", new DateOnly(1958, 2, 12), "F"), null, ["I10"],
        [new SubmissionLine(1, "99213", [], 1, 120, new DateOnly(2025, 11, 3), "11", [1])], 120);

    public void Dispose()
    {
        Services.Dispose();
        Payer.Stop();
    }
}
