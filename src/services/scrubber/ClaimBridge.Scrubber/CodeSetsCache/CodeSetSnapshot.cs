using ClaimBridge.Scrubber.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace ClaimBridge.Scrubber.CodeSetsCache;

public sealed record ProcedureInfo(string Code, string Description, string Category, decimal TypicalCharge);
public sealed record ModifierInfo(string Code, string Description, string[] AllowedCategories);

/// <summary>Everything the rules need from the code tables, loaded once and cached.</summary>
public sealed record CodeSetSnapshot(
    Dictionary<string, bool> Icd10,
    Dictionary<string, ProcedureInfo> Procedures,
    Dictionary<string, ModifierInfo> Modifiers,
    Dictionary<string, string[]> MedicalNecessity,
    Dictionary<string, int> TimelyFilingDays)
{
    public static readonly HashSet<string> PlacesOfService = ["02", "10", "11", "21", "22", "23", "81"];
}

public class CodeSetProvider(ScrubberDb db, HybridCache cache)
{
    public const string CacheKey = "scrubber:codesets:v1";

    public ValueTask<CodeSetSnapshot> GetAsync(CancellationToken ct = default) =>
        cache.GetOrCreateAsync(CacheKey, async token => await LoadAsync(token),
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromHours(1) }, cancellationToken: ct);

    public ValueTask InvalidateAsync(CancellationToken ct = default) => cache.RemoveAsync(CacheKey, ct);

    private async Task<CodeSetSnapshot> LoadAsync(CancellationToken ct)
    {
        var icd = await db.IcdCodes.AsNoTracking().ToDictionaryAsync(c => c.Code, c => c.Billable, ct);
        var procedures = await db.ProcedureCodes.AsNoTracking()
            .ToDictionaryAsync(p => p.Code, p => new ProcedureInfo(p.Code, p.Description, p.Category, p.TypicalCharge), ct);
        var modifiers = (await db.Modifiers.AsNoTracking().ToListAsync(ct))
            .ToDictionary(m => m.Code, m => new ModifierInfo(m.Code, m.Description, m.AllowedCategories.Split(',')));
        var necessity = (await db.MedicalNecessity.AsNoTracking().ToListAsync(ct))
            .GroupBy(n => n.ProcedureCode)
            .ToDictionary(g => g.Key, g => g.Select(n => n.DxPrefix).ToArray());
        var filing = await db.PayerSettings.AsNoTracking().ToDictionaryAsync(p => p.PayerId, p => p.TimelyFilingDays, ct);
        return new CodeSetSnapshot(icd, procedures, modifiers, necessity, filing);
    }
}
