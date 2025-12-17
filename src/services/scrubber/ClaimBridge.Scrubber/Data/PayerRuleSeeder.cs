using ClaimBridge.Contracts.Reference;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Scrubber.Data;

public static class PayerRuleSeeder
{
    public static async Task SeedAsync(ScrubberDb db)
    {
        if (await db.PayerRules.AnyAsync()) return;
        var now = DateTimeOffset.UtcNow;
        db.PayerRules.AddRange(
            Rule(SeedIds.PayerA, "PA-A01", "Prior auth for advanced imaging", "line",
                """{"all":[{"procedureIn":["73721","72148"]},{"hasPriorAuth":false}]}""", "Error",
                "Northwind requires a prior authorisation number for MRI.",
                "Request the authorisation in Northwind's portal and enter the number on the claim."),
            Rule(SeedIds.PayerA, "PA-A02", "Modifier 25 on E/M is reviewed", "line",
                """{"all":[{"modifierPresent":"25"},{"procedureCategory":"EM"}]}""", "Warning",
                "Northwind reviews E/M lines billed with modifier 25.",
                "Make sure the note documents a separate, significant visit."),
            Rule(SeedIds.PayerB, "PA-B01", "Therapy units cap", "line",
                """{"all":[{"procedureIn":["97110","97140"]},{"unitsAbove":4}]}""", "Error",
                "Bluepeak pays at most 4 units per therapy code per day.",
                "Reduce units to 4; extra time is not separately payable."),
            Rule(SeedIds.PayerB, "PA-B02", "No modifier 59", "line",
                """{"modifierPresent":"59"}""", "Error",
                "Bluepeak no longer accepts modifier 59.",
                "Use the more specific XE, XS, XP or XU modifier."),
            Rule(SeedIds.PayerC, "PA-C01", "High charge review", "line",
                """{"chargeAbove":5000}""", "Warning",
                "Cascade pends lines over 5,000 for manual review.",
                "Attach the operative note when the payer asks for records."),
            Rule(SeedIds.PayerC, "PA-C02", "Telehealth needs modifier 95", "line",
                """{"all":[{"posIn":["02","10"]},{"modifierAbsent":"95"}]}""", "Error",
                "Cascade requires modifier 95 on telehealth lines.",
                "Add modifier 95 to each telehealth line.", enabled: false));
        await db.SaveChangesAsync();

        PayerRule Rule(string payer, string code, string name, string scope, string condition, string severity,
            string message, string fix, bool enabled = true) => new()
            {
                PayerId = payer, RuleCode = code, Name = name, Scope = scope, ConditionJson = condition, Severity = severity,
                Message = message, FixSuggestion = fix, Enabled = enabled, UpdatedAt = now, UpdatedBy = "seed"
            };
    }
}
