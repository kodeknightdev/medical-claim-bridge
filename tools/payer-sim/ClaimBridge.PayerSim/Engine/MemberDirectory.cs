using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Storage.Blobs;
using ClaimBridge.Contracts.Reference;

namespace ClaimBridge.PayerSim.Engine;

/// <summary>Synthetic members: a handful built in for quick demos, plus the seeder's ~800 loaded from blob storage.</summary>
public sealed class MemberDirectory
{
    private readonly ConcurrentDictionary<string, SimMember> _members = new(StringComparer.OrdinalIgnoreCase);

    public MemberDirectory()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        string[] first = ["Alex", "Jamie", "Taylor", "Morgan", "Casey", "Riley", "Drew", "Quinn", "Sky", "Rowan"];
        string[] last = ["Sample", "Example", "Testwell", "Demoson", "Mockford", "Placeholder", "Fakeley", "Synthwood", "Dummer", "Notreal"];
        (string Payer, string Prefix, string Plan)[] payers =
            [(SeedIds.PayerA, "NWH", "Northwind Silver PPO"), (SeedIds.PayerB, "BPK", "Bluepeak Standard HMO"), (SeedIds.PayerC, "CCI", "Cascade Basic EPO")];
        foreach (var (payer, prefix, plan) in payers)
            for (var i = 1; i <= 10; i++)
            {
                var termed = i == 9;
                Add(new SimMember($"{prefix}{1000000 + i}", payer, first[i - 1], last[i - 1], new DateOnly(1950 + i * 4, i, 10 + i),
                    i % 2 == 0 ? "F" : "M", plan, today.AddYears(-3), termed ? today.AddDays(-60) : null,
                    payer == SeedIds.PayerA ? 25 : 30, 1500, i % 3 == 0 ? 1500 : 200));
            }
    }

    public int Count => _members.Count;

    public void Add(SimMember member) => _members[member.MemberId] = member;

    public SimMember? Find(string memberId) => _members.GetValueOrDefault(memberId.Trim());

    public bool IsCovered(SimMember member, DateOnly on) =>
        on >= member.CoverageStart && (member.CoverageEnd is null || on <= member.CoverageEnd);

    public async Task<int> LoadFromBlobAsync(BlobServiceClient blobs, CancellationToken ct)
    {
        var blob = blobs.GetBlobContainerClient("seed").GetBlobClient("members.json");
        if (!await blob.ExistsAsync(ct)) return 0;
        var content = await blob.DownloadContentAsync(ct);
        var members = JsonSerializer.Deserialize<List<SimMember>>(content.Value.Content.ToString(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        foreach (var m in members) Add(m);
        return members.Count;
    }
}

public sealed class MemberLoader(MemberDirectory members, BlobServiceClient blobs, ILogger<MemberLoader> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var loaded = await members.LoadFromBlobAsync(blobs, stoppingToken);
                if (loaded > 0)
                {
                    logger.LogInformation("Loaded {Count} seeded members", loaded);
                    return;
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogDebug("Seed members not available yet: {Error}", ex.Message);
            }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
