using ClaimBridge.Scrubber.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace ClaimBridge.Scrubber.Rules.PayerRules;

public class PayerRuleProvider(ScrubberDb db, HybridCache cache, ILogger<PayerRuleProvider> logger)
{
    public sealed record CachedRule(string Code, string Name, string Scope, string ConditionJson, string Severity, string Message, string? Fix);

    public static string CacheKey(string payerId) => $"scrubber:payer-rules:{payerId}";

    public async Task<IReadOnlyList<IScrubRule>> GetRulesAsync(string payerId, CancellationToken ct)
    {
        var rows = await cache.GetOrCreateAsync(CacheKey(payerId), async token =>
            await db.PayerRules.AsNoTracking()
                .Where(r => r.PayerId == payerId && r.Enabled)
                .Select(r => new CachedRule(r.RuleCode, r.Name, r.Scope, r.ConditionJson, r.Severity, r.Message, r.FixSuggestion))
                .ToListAsync(token),
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(15) },
            tags: ["payer-rules"], cancellationToken: ct);

        var rules = new List<IScrubRule>(rows.Count);
        foreach (var r in rows)
        {
            try
            {
                rules.Add(new DataDrivenPayerRule(r.Code, r.Name, r.Scope == "claim", PayerRuleCompiler.Compile(r.ConditionJson),
                    Enum.Parse<Severity>(r.Severity), r.Message, r.Fix));
            }
            catch (PayerRuleSyntaxException ex)
            {
                // a broken rule row should not take the whole scrubber down
                logger.LogError("Payer rule {RuleCode} for {PayerId} skipped: {Error}", r.Code, payerId, ex.Message);
            }
        }
        return rules;
    }

    public ValueTask InvalidateAsync(string payerId, CancellationToken ct = default) => cache.RemoveAsync(CacheKey(payerId), ct);
}
