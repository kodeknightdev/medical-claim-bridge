using ClaimBridge.Contracts.ReferenceData;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Remittance.Data;

public static class ReasonCodeSeeder
{
    public static async Task SeedAsync(RemittanceDb db)
    {
        if (await db.ReasonCodes.AnyAsync()) return;
        db.ReasonCodes.AddRange(AdjustmentReasons.GroupCodes.Select(g => new ReasonCode { Kind = "GROUP", Code = g.Code, Explanation = $"{g.Name}: {g.Explanation}" }));
        db.ReasonCodes.AddRange(AdjustmentReasons.CarcCodes.Select(c => new ReasonCode { Kind = "CARC", Code = c.Code, Category = c.Category, Explanation = c.Explanation }));
        db.ReasonCodes.AddRange(AdjustmentReasons.RarcCodes.Select(r => new ReasonCode { Kind = "RARC", Code = r.Code, Explanation = r.Explanation }));
        await db.SaveChangesAsync();
    }
}
