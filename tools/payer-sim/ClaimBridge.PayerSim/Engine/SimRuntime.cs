using System.Collections.Concurrent;
using Azure.Storage.Blobs;
using ClaimBridge.Contracts.Reference;
using ClaimBridge.X12.Core;
using ClaimBridge.X12.Transactions;

namespace ClaimBridge.PayerSim.Engine;

/// <summary>In-memory claim store shared by the three simulated payers.</summary>
public sealed class ClaimStore
{
    private readonly ConcurrentDictionary<string, SimClaimRecord> _byNumber = new();
    private readonly ConcurrentDictionary<string, SimClaimRecord> _byPayerId = new();
    private int _sequence = 100000;

    public SimClaimRecord Add(SimClaim claim, string prefix)
    {
        var record = new SimClaimRecord(claim, $"{prefix}{Interlocked.Increment(ref _sequence)}");
        _byNumber[$"{claim.PayerId}|{claim.ClaimNumber}"] = record;
        _byPayerId[record.PayerClaimId] = record;
        return record;
    }

    public SimClaimRecord? Find(string payerId, string? claimNumber, string? payerClaimId = null)
    {
        if (payerClaimId is not null && _byPayerId.TryGetValue(payerClaimId, out var byId)) return byId;
        return claimNumber is not null ? _byNumber.GetValueOrDefault($"{payerId}|{claimNumber}") : null;
    }

    public IEnumerable<SimClaimRecord> All => _byNumber.Values;
}

/// <summary>Runs delayed payer actions (callbacks, acknowledgements, remittances) without blocking requests.</summary>
public sealed class SimScheduler(ILogger<SimScheduler> logger) : BackgroundService
{
    private readonly PriorityQueue<Func<CancellationToken, Task>, DateTimeOffset> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly object _lock = new();

    public void Schedule(TimeSpan delay, Func<CancellationToken, Task> action)
    {
        lock (_lock) _queue.Enqueue(action, DateTimeOffset.UtcNow + delay);
        _signal.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Func<CancellationToken, Task>? next = null;
            TimeSpan wait = TimeSpan.FromSeconds(1);
            lock (_lock)
            {
                if (_queue.TryPeek(out var action, out var due))
                {
                    if (due <= DateTimeOffset.UtcNow) next = _queue.Dequeue();
                    else wait = due - DateTimeOffset.UtcNow;
                }
            }

            if (next is null)
            {
                await _signal.WaitAsync(wait < TimeSpan.FromSeconds(1) ? wait : TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }
            try
            {
                await next(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Scheduled sim action failed");
            }
        }
    }
}

/// <summary>Writes 835 remittance files into each payer's outbound folder, where payers-gateway polls them.</summary>
public sealed class RemittanceWriter(BlobServiceClient blobs, ILogger<RemittanceWriter> logger)
{
    private int _control = 5000;
    private int _eft;

    public static string Container(string payerId) => payerId switch
    {
        SeedIds.PayerA => "payer-a",
        SeedIds.PayerB => "payer-b",
        _ => "payer-c"
    };

    public async Task WriteAsync(string payerId, string payeeNpi, string payeeName, IReadOnlyList<ClaimPayment> payments, CancellationToken ct)
    {
        if (payments.Count == 0) return;
        var now = DateTimeOffset.UtcNow;
        var control = Interlocked.Increment(ref _control);
        // time-based so trace numbers stay unique across simulator restarts
        var trace = $"EFT{now:yyyyMMddHHmmss}{Interlocked.Increment(ref _eft) % 1000:D3}";
        var payer = PayerDirectory.Find(payerId)!;
        var advice = new RemittanceAdvice(payer.Name, payeeNpi, trace, DateOnly.FromDateTime(now.UtcDateTime),
            payments.Sum(p => p.PaidAmount), "ACH", payments);
        var text = advice.Write(new InterchangeOptions(payerId.Replace("-", ""), "CLAIMBRIDGE", control, now), payeeName);

        var container = blobs.GetBlobContainerClient(Container(payerId));
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        var name = $"outbound/{payerId}_835_{control:D9}.x12";
        await container.GetBlobClient(name).UploadAsync(BinaryData.FromString(text), overwrite: true, ct);
        logger.LogInformation("Wrote 835 {Name} with {Count} claim(s), EFT {Trace}", name, payments.Count, trace);
    }
}
