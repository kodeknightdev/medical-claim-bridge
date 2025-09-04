namespace ClaimBridge.Contracts.Reference;

/// <summary>Fixed identifiers shared by every service seeder so demo data lines up across databases.</summary>
public static class SeedIds
{
    public static readonly Guid SunriseOrg = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    public static readonly Guid LakesideOrg = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    public static readonly Guid HarborOrg = Guid.Parse("a1000000-0000-0000-0000-000000000003");

    public static readonly Guid[] Orgs = [SunriseOrg, LakesideOrg, HarborOrg];

    public const string PayerA = "PAYER-A";
    public const string PayerB = "PAYER-B";
    public const string PayerC = "PAYER-C";

    public static readonly string[] Payers = [PayerA, PayerB, PayerC];

    public const string DemoClientId = "sunrise-pms";
    public const string PortalClientId = "claim-bridge-portal";
}

public sealed record PayerInfo(string Id, string Name, string Channel, int TimelyFilingDays);

public static class PayerDirectory
{
    // all payer names are fictional
    public static readonly IReadOnlyList<PayerInfo> All =
    [
        new(SeedIds.PayerA, "Northwind Health Plan", "json-api", 90),
        new(SeedIds.PayerB, "Bluepeak Mutual", "x12-files", 180),
        new(SeedIds.PayerC, "Cascade Care Insurance", "json-api", 365),
    ];

    public static PayerInfo? Find(string id) => All.FirstOrDefault(p => p.Id == id);
}
