using ClaimBridge.Scrubber.Data;
using ClaimBridge.Scrubber.Rules.PayerRules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClaimBridge.Scrubber.Tests;

public class PayerRuleProviderTests
{
    private static (ScrubberDb Db, HybridCache Cache) Setup()
    {
        var db = new ScrubberDb(new DbContextOptionsBuilder<ScrubberDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var cache = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider().GetRequiredService<HybridCache>();
        return (db, cache);
    }

    [Fact]
    public async Task Loads_only_enabled_rules_for_the_payer()
    {
        var (db, cache) = Setup();
        await PayerRuleSeeder.SeedAsync(db);
        var provider = new PayerRuleProvider(db, cache, NullLogger<PayerRuleProvider>.Instance);

        (await provider.GetRulesAsync("PAYER-A", default)).Select(r => r.Code).ShouldBe(["PA-A01", "PA-A02"], ignoreOrder: true);
        // PA-C02 is seeded disabled
        (await provider.GetRulesAsync("PAYER-C", default)).Select(r => r.Code).ShouldBe(["PA-C01"]);
    }

    [Fact]
    public async Task Broken_rule_rows_are_skipped_not_fatal_and_cache_invalidation_picks_up_changes()
    {
        var (db, cache) = Setup();
        db.PayerRules.Add(new PayerRule { PayerId = "PAYER-B", RuleCode = "BAD", Name = "bad", ConditionJson = "{oops", Message = "m" });
        db.PayerRules.Add(new PayerRule { PayerId = "PAYER-B", RuleCode = "OK", Name = "ok", ConditionJson = """{"chargeAbove":1}""", Message = "m" });
        await db.SaveChangesAsync();
        var provider = new PayerRuleProvider(db, cache, NullLogger<PayerRuleProvider>.Instance);

        (await provider.GetRulesAsync("PAYER-B", default)).Single().Code.ShouldBe("OK");

        (await db.PayerRules.SingleAsync(r => r.RuleCode == "OK")).Enabled = false;
        await db.SaveChangesAsync();
        (await provider.GetRulesAsync("PAYER-B", default)).ShouldNotBeEmpty();
        await provider.InvalidateAsync("PAYER-B");
        (await provider.GetRulesAsync("PAYER-B", default)).ShouldBeEmpty();
    }
}
