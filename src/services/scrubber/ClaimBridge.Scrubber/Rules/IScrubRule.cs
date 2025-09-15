using ClaimBridge.Scrubber.CodeSetsCache;

namespace ClaimBridge.Scrubber.Rules;

public sealed record ScrubContext(ScrubClaim Claim, CodeSetSnapshot CodeSets, DateOnly Today)
{
    public ProcedureInfo? Procedure(string code) => CodeSets.Procedures.GetValueOrDefault(code);
}

public interface IScrubRule
{
    string Code { get; }
    string Name { get; }
    IEnumerable<ScrubEdit> Evaluate(ScrubContext context);
}
