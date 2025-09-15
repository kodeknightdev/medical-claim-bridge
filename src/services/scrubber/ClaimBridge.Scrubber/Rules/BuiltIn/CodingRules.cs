using System.Text.RegularExpressions;

namespace ClaimBridge.Scrubber.Rules.BuiltIn;

public sealed partial class DiagnosisCodeRule : IScrubRule
{
    public const int MaxDiagnoses = 12;
    public string Code => "CB003";
    public string Name => "ICD-10-CM diagnosis codes";

    public IEnumerable<ScrubEdit> Evaluate(ScrubContext ctx)
    {
        var dx = ctx.Claim.Diagnoses;
        if (dx.Count > MaxDiagnoses)
            yield return new(Code, Severity.Error, $"At most {MaxDiagnoses} diagnoses fit on a professional claim.", "diagnoses",
                "Keep the diagnoses that support the billed services.");

        for (var i = 0; i < dx.Count; i++)
        {
            var code = dx[i].Trim().ToUpperInvariant();
            var field = $"diagnoses[{i}]";
            if (!IcdFormat().IsMatch(code))
            {
                yield return new(Code, Severity.Error, $"'{dx[i]}' is not a valid ICD-10-CM format.", field,
                    "Codes look like a letter, two characters, then an optional dot and up to four more, e.g. E11.9.");
                continue;
            }
            if (!ctx.CodeSets.Icd10.TryGetValue(code, out var billable))
                yield return new(Code, Severity.Error, $"{code} is not in the ICD-10-CM code set.", field, "Search the code list for the closest match.");
            else if (!billable)
                yield return new(Code, Severity.Error, $"{code} is a category header, not a billable code.", field,
                    $"Pick a more specific code under {code}, for example {SuggestChild(ctx, code)}.");
        }

        var dupes = dx.GroupBy(d => d.Trim().ToUpperInvariant()).Where(g => g.Count() > 1).Select(g => g.Key);
        foreach (var d in dupes)
            yield return new(Code, Severity.Warning, $"Diagnosis {d} is listed more than once.", "diagnoses", "Remove the repeat.");
    }

    private static string SuggestChild(ScrubContext ctx, string header) =>
        ctx.CodeSets.Icd10.Where(kv => kv.Value && kv.Key.StartsWith(header + ".")).Select(kv => kv.Key).Order().FirstOrDefault()
        ?? header + ".9";

    [GeneratedRegex(@"^[A-TV-Z][0-9][0-9A-Z](\.[0-9A-Z]{1,4})?$")]
    internal static partial Regex IcdFormat();
}

public sealed class DiagnosisPointerRule : IScrubRule
{
    public string Code => "CB004";
    public string Name => "Diagnosis pointers";

    public IEnumerable<ScrubEdit> Evaluate(ScrubContext ctx)
    {
        var count = ctx.Claim.Diagnoses.Count;
        foreach (var line in ctx.Claim.Lines)
        {
            if (line.DxPointers.Count == 0)
            {
                yield return new(Code, Severity.Error, "Line has no diagnosis pointer.", "dxPointers",
                    "Point the line at the diagnosis that supports it (1 = first diagnosis).", line.LineNumber);
                continue;
            }
            if (line.DxPointers.Count > 4)
                yield return new(Code, Severity.Error, "A line can point at no more than 4 diagnoses.", "dxPointers",
                    "Keep the four most relevant pointers.", line.LineNumber);
            foreach (var p in line.DxPointers.Where(p => p < 1 || p > count))
                yield return new(Code, Severity.Error, $"Pointer {p} does not match a diagnosis on the claim.", "dxPointers",
                    $"Use a number between 1 and {count}.", line.LineNumber);
            if (line.DxPointers.Distinct().Count() != line.DxPointers.Count)
                yield return new(Code, Severity.Warning, "Line repeats the same diagnosis pointer.", "dxPointers", null, line.LineNumber);
        }
    }
}

public sealed class ProcedureLineRule : IScrubRule
{
    public string Code => "CB005";
    public string Name => "Procedure code, units, charge and place of service";

    public IEnumerable<ScrubEdit> Evaluate(ScrubContext ctx)
    {
        foreach (var line in ctx.Claim.Lines)
        {
            if (ctx.Procedure(line.ProcedureCode) is null)
                yield return new(Code, Severity.Error, $"Procedure code {line.ProcedureCode} is not recognised.", "procedureCode",
                    "Check the code against the fee schedule.", line.LineNumber);
            if (line.Units <= 0)
                yield return new(Code, Severity.Error, "Units must be greater than zero.", "units", null, line.LineNumber);
            if (line.Units != Math.Floor(line.Units))
                yield return new(Code, Severity.Error, "Units must be a whole number for professional claims.", "units", null, line.LineNumber);
            if (line.Charge <= 0)
                yield return new(Code, Severity.Error, "Line charge must be greater than zero.", "charge", null, line.LineNumber);
            if (!CodeSetsCache.CodeSetSnapshot.PlacesOfService.Contains(line.PlaceOfService))
                yield return new(Code, Severity.Error, $"Place of service {line.PlaceOfService} is not supported.", "placeOfService",
                    "Use 11 for office, 02/10 for telehealth, 22 for outpatient hospital.", line.LineNumber);
        }
    }
}

public sealed class ModifierRule : IScrubRule
{
    public string Code => "CB006";
    public string Name => "Procedure and modifier combinations";

    public IEnumerable<ScrubEdit> Evaluate(ScrubContext ctx)
    {
        foreach (var line in ctx.Claim.Lines)
        {
            var proc = ctx.Procedure(line.ProcedureCode);
            if (line.Modifiers.Count > 4)
                yield return new(Code, Severity.Error, "A line can carry at most 4 modifiers.", "modifiers", null, line.LineNumber);

            foreach (var mod in line.Modifiers)
            {
                if (!ctx.CodeSets.Modifiers.TryGetValue(mod, out var info))
                {
                    yield return new(Code, Severity.Error, $"Modifier {mod} is not recognised.", "modifiers", null, line.LineNumber);
                    continue;
                }
                if (proc is not null && !info.AllowedCategories.Contains(proc.Category))
                    yield return new(Code, Severity.Error,
                        $"Modifier {mod} ({info.Description.ToLowerInvariant()}) does not apply to {proc.Category} code {proc.Code}.",
                        "modifiers", Fix(mod, proc.Category), line.LineNumber);
            }

            if (line.Modifiers.Contains("LT") && line.Modifiers.Contains("RT"))
                yield return new(Code, Severity.Error, "LT and RT on one line; bill both sides with modifier 50 or two lines.",
                    "modifiers", "Use 50 for a bilateral procedure.", line.LineNumber);
            if (line.Modifiers.Contains("50") && (line.Modifiers.Contains("LT") || line.Modifiers.Contains("RT")))
                yield return new(Code, Severity.Warning, "Modifier 50 already means both sides; LT/RT is redundant.", "modifiers",
                    null, line.LineNumber);
        }

        // 25 says the visit was separate from a procedure, so there has to be one
        var hasMod25 = ctx.Claim.Lines.Any(l => l.Modifiers.Contains("25"));
        var hasProcedure = ctx.Claim.Lines.Any(l => ctx.Procedure(l.ProcedureCode)?.Category is "Procedure" or "Vaccine" or "Drug" or "Imaging");
        if (hasMod25 && !hasProcedure)
            yield return new(Code, Severity.Warning, "Modifier 25 used but no separate procedure is billed on this claim.",
                "modifiers", "Drop modifier 25 unless a procedure was done the same day.");
    }

    private static string? Fix(string mod, string category) => (mod, category) switch
    {
        ("25", _) => "Modifier 25 belongs on the E/M line, not the procedure line.",
        ("59", "EM") => "For a separate visit use 25 on the E/M line instead.",
        _ => null
    };
}

public sealed class MedicalNecessityRule : IScrubRule
{
    public string Code => "CB007";
    public string Name => "Diagnosis supports procedure";

    public IEnumerable<ScrubEdit> Evaluate(ScrubContext ctx)
    {
        var dx = ctx.Claim.Diagnoses;
        foreach (var line in ctx.Claim.Lines)
        {
            if (!ctx.CodeSets.MedicalNecessity.TryGetValue(line.ProcedureCode, out var prefixes)) continue;
            var pointed = line.DxPointers.Where(p => p >= 1 && p <= dx.Count).Select(p => dx[p - 1].ToUpperInvariant());
            if (!pointed.Any(code => prefixes.Any(code.StartsWith)))
                yield return new(Code, Severity.Error,
                    $"None of the diagnoses pointed to by line {line.LineNumber} support {line.ProcedureCode}.", "dxPointers",
                    $"Point at a supporting diagnosis (expected one starting with {string.Join(", ", prefixes.Take(3))}).",
                    line.LineNumber);
        }
    }
}

public sealed class DuplicateLineRule : IScrubRule
{
    public string Code => "CB012";
    public string Name => "Duplicate service lines";

    public IEnumerable<ScrubEdit> Evaluate(ScrubContext ctx)
    {
        var groups = ctx.Claim.Lines
            .GroupBy(l => (l.ProcedureCode, l.ServiceDate, Mods: string.Join(',', l.Modifiers.Order())))
            .Where(g => g.Count() > 1);
        foreach (var g in groups)
            yield return new(Code, Severity.Warning,
                $"Procedure {g.Key.ProcedureCode} appears {g.Count()} times on {g.Key.ServiceDate:yyyy-MM-dd} with the same modifiers.",
                "lines", "Combine into one line with more units, or add a distinguishing modifier.", g.Skip(1).First().LineNumber);
    }
}
