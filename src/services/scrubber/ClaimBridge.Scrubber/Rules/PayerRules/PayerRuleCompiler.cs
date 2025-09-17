using System.Text.Json;
using ClaimBridge.Scrubber.Rules.Specifications;

namespace ClaimBridge.Scrubber.Rules.PayerRules;

public sealed record LineContext(ScrubContext Scrub, ScrubLine Line)
{
    public ScrubClaim Claim => Scrub.Claim;
}

public sealed class PayerRuleSyntaxException(string message) : Exception(message);

/// <summary>
/// Turns a condition such as <c>{"all":[{"procedureIn":["73721"]},{"not":{"hasPriorAuth":true}}]}</c>
/// into a specification evaluated per service line.
/// </summary>
public static class PayerRuleCompiler
{
    public static readonly string[] Operators =
    [
        "all", "any", "not", "procedureIn", "procedureCategory", "modifierPresent", "modifierAbsent",
        "unitsAbove", "chargeAbove", "posIn", "hasPriorAuth", "dxPrefixAny", "frequencyIn", "patientAgeBelow"
    ];

    public static ISpecification<LineContext> Compile(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Compile(doc.RootElement);
        }
        catch (JsonException ex)
        {
            throw new PayerRuleSyntaxException("Condition is not valid JSON: " + ex.Message);
        }
    }

    private static ISpecification<LineContext> Compile(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object || node.EnumerateObject().Count() != 1)
            throw new PayerRuleSyntaxException("Each condition node must be an object with exactly one operator.");

        var op = node.EnumerateObject().Single();
        var v = op.Value;
        return op.Name switch
        {
            "all" => Spec.All(Children(v)),
            "any" => Spec.Any(Children(v)),
            "not" => Compile(v).Not(),
            "procedureIn" => In(v, op.Name, c => [c.Line.ProcedureCode]),
            "procedureCategory" => Spec.Where<LineContext>(c => c.Scrub.Procedure(c.Line.ProcedureCode)?.Category == v.GetString(), $"category = {v.GetString()}"),
            "modifierPresent" => Spec.Where<LineContext>(c => c.Line.Modifiers.Contains(v.GetString()!), $"has modifier {v.GetString()}"),
            "modifierAbsent" => Spec.Where<LineContext>(c => !c.Line.Modifiers.Contains(v.GetString()!), $"lacks modifier {v.GetString()}"),
            "unitsAbove" => Spec.Where<LineContext>(c => c.Line.Units > v.GetDecimal(), $"units > {v.GetDecimal()}"),
            "chargeAbove" => Spec.Where<LineContext>(c => c.Line.Charge > v.GetDecimal(), $"charge > {v.GetDecimal()}"),
            "posIn" => In(v, op.Name, c => [c.Line.PlaceOfService]),
            "hasPriorAuth" => Spec.Where<LineContext>(c => !string.IsNullOrWhiteSpace(c.Claim.PriorAuthNumber) == v.GetBoolean(), $"prior auth present = {v.GetBoolean()}"),
            "dxPrefixAny" => DxPrefix(v),
            "frequencyIn" => In(v, op.Name, c => [c.Claim.FrequencyCode]),
            "patientAgeBelow" => Spec.Where<LineContext>(c => c.Claim.PatientDob is { } dob && Age(dob, c.Line.ServiceDate) < v.GetInt32(), $"age < {v.GetInt32()}"),
            _ => throw new PayerRuleSyntaxException($"Unknown operator '{op.Name}'. Supported: {string.Join(", ", Operators)}")
        };
    }

    private static IEnumerable<ISpecification<LineContext>> Children(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() == 0)
            throw new PayerRuleSyntaxException("all/any need a non-empty array.");
        return v.EnumerateArray().Select(Compile).ToList();
    }

    private static ISpecification<LineContext> In(JsonElement v, string op, Func<LineContext, string[]> selector)
    {
        var values = Strings(v, op);
        return Spec.Where<LineContext>(c => selector(c).Any(values.Contains), $"{op} [{string.Join(',', values)}]");
    }

    private static ISpecification<LineContext> DxPrefix(JsonElement v)
    {
        var prefixes = Strings(v, "dxPrefixAny");
        return Spec.Where<LineContext>(c => c.Claim.Diagnoses.Any(d => prefixes.Any(p => d.StartsWith(p, StringComparison.OrdinalIgnoreCase))),
            $"dx starts with any [{string.Join(',', prefixes)}]");
    }

    private static HashSet<string> Strings(JsonElement v, string op)
    {
        if (v.ValueKind != JsonValueKind.Array || v.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.String))
            throw new PayerRuleSyntaxException($"{op} needs an array of strings.");
        return v.EnumerateArray().Select(e => e.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static int Age(DateOnly dob, DateOnly on) =>
        on.Year - dob.Year - (on < dob.AddYears(on.Year - dob.Year) ? 1 : 0);
}
