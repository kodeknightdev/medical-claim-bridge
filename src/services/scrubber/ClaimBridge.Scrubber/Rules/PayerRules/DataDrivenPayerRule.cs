using ClaimBridge.Scrubber.Rules.Specifications;

namespace ClaimBridge.Scrubber.Rules.PayerRules;

public sealed class DataDrivenPayerRule(
    string code,
    string name,
    bool claimScope,
    ISpecification<LineContext> condition,
    Severity severity,
    string message,
    string? fix) : IScrubRule
{
    public string Code => code;
    public string Name => name;
    public ISpecification<LineContext> Condition => condition;

    public IEnumerable<ScrubEdit> Evaluate(ScrubContext context)
    {
        var matches = context.Claim.Lines.Where(l => condition.IsSatisfiedBy(new LineContext(context, l)));
        if (claimScope)
        {
            if (matches.Any())
                yield return new ScrubEdit(code, severity, message, null, fix);
            yield break;
        }
        foreach (var line in matches)
            yield return new ScrubEdit(code, severity, message, null, fix, line.LineNumber);
    }
}
