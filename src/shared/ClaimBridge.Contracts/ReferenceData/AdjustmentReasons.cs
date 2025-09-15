using System.Text.Json;

namespace ClaimBridge.Contracts.ReferenceData;

public sealed record GroupCode(string Code, string Name, string Explanation);
public sealed record Carc(string Code, string Category, string Explanation);
public sealed record Rarc(string Code, string Explanation);

/// <summary>Seeded CARC/RARC and group code reference data. Explanations are our own wording.</summary>
public static class AdjustmentReasons
{
    private sealed record Catalog(List<GroupCode> GroupCodes, List<Carc> Carc, List<Rarc> Rarc);

    private static readonly Lazy<Catalog> Data = new(() =>
    {
        using var stream = typeof(AdjustmentReasons).Assembly
            .GetManifestResourceStream("ClaimBridge.Contracts.ReferenceData.adjustment-reasons.json")!;
        return JsonSerializer.Deserialize<Catalog>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    });

    public static IReadOnlyList<GroupCode> GroupCodes => Data.Value.GroupCodes;
    public static IReadOnlyList<Carc> CarcCodes => Data.Value.Carc;
    public static IReadOnlyList<Rarc> RarcCodes => Data.Value.Rarc;

    public static Carc? FindCarc(string code) => Data.Value.Carc.FirstOrDefault(c => c.Code == code);

    public static string CategoryFor(string carcCode) => FindCarc(carcCode)?.Category ?? "Other";
}
