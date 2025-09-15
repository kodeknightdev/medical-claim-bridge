using System.Text.Json;
using ClaimBridge.Contracts.Reference;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Scrubber.Data;

public static class CodeSetSeeder
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record ModifierJson(string Code, string Description, string[] AllowedCategories);
    private sealed record NecessityJson(string Procedure, string[] DxPrefixes);

    public static async Task SeedAsync(ScrubberDb db)
    {
        if (!await db.IcdCodes.AnyAsync())
            db.IcdCodes.AddRange(Load<List<IcdCode>>("icd10cm.json"));

        if (!await db.ProcedureCodes.AnyAsync())
            db.ProcedureCodes.AddRange(Load<List<ProcedureCode>>("procedures.json"));

        if (!await db.Modifiers.AnyAsync())
            db.Modifiers.AddRange(Load<List<ModifierJson>>("modifiers.json")
                .Select(m => new Modifier { Code = m.Code, Description = m.Description, AllowedCategories = string.Join(',', m.AllowedCategories) }));

        if (!await db.MedicalNecessity.AnyAsync())
            db.MedicalNecessity.AddRange(Load<List<NecessityJson>>("medical-necessity.json")
                .SelectMany(n => n.DxPrefixes.Select(p => new MedicalNecessityEntry { ProcedureCode = n.Procedure, DxPrefix = p })));

        foreach (var payer in PayerDirectory.All)
            if (!await db.PayerSettings.AnyAsync(p => p.PayerId == payer.Id))
                db.PayerSettings.Add(new PayerSetting { PayerId = payer.Id, Name = payer.Name, TimelyFilingDays = payer.TimelyFilingDays });

        await db.SaveChangesAsync();
    }

    internal static T Load<T>(string file)
    {
        using var stream = typeof(CodeSetSeeder).Assembly.GetManifestResourceStream("CodeSets." + file)
            ?? throw new InvalidOperationException($"Embedded code set {file} missing");
        return JsonSerializer.Deserialize<T>(stream, Json)!;
    }
}
