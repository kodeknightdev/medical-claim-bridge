using System.Diagnostics.Metrics;
using ClaimBridge.Scrubber.CodeSetsCache;
using ClaimBridge.Scrubber.Data;

namespace ClaimBridge.Scrubber.Rules;

public sealed record ScrubOutcome(Guid? RunId, ScrubResult Result);

public class ScrubService(
    ScrubEngine engine,
    CodeSetProvider codeSets,
    ScrubberDb db,
    ScrubberMetrics metrics,
    TimeProvider clock)
{
    public async Task<ScrubOutcome> ScrubAsync(Guid claimId, int claimVersion, ScrubClaim claim, bool persist, CancellationToken ct)
    {
        var snapshot = await codeSets.GetAsync(ct);
        var context = new ScrubContext(claim, snapshot, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        var result = engine.Run(context);

        if (!persist)
            return new ScrubOutcome(null, result);

        metrics.Record(claim.PayerId, result);
        var run = new ScrubRun
        {
            Id = Guid.NewGuid(), ClaimId = claimId, ClaimVersion = claimVersion, OrgId = claim.OrgId, PayerId = claim.PayerId,
            RanAt = clock.GetUtcNow(), Passed = result.Passed, ErrorCount = result.Errors, WarningCount = result.Warnings,
            RulesEvaluated = result.RulesEvaluated,
            Edits = result.Edits.Select(e => new ScrubEditRow
            {
                RuleCode = e.RuleCode, Severity = e.Severity.ToString(), Message = e.Message, Field = e.Field,
                FixSuggestion = e.FixSuggestion, LineNumber = e.LineNumber
            }).ToList()
        };
        db.ScrubRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return new ScrubOutcome(run.Id, result);
    }
}

public sealed class ScrubberMetrics
{
    public const string MeterName = "ClaimBridge.Scrubber";
    private readonly Counter<long> _runs;
    private readonly Counter<long> _edits;

    public ScrubberMetrics(IMeterFactory meters)
    {
        var meter = meters.Create(MeterName);
        _runs = meter.CreateCounter<long>("scrubber.runs", description: "Final scrub runs by outcome");
        _edits = meter.CreateCounter<long>("scrubber.edits", description: "Scrub edits raised, by rule and severity");
    }

    public void Record(string payerId, ScrubResult result)
    {
        _runs.Add(1, new("payer", payerId), new("outcome", result.Passed ? "passed" : "failed"));
        foreach (var edit in result.Edits)
            _edits.Add(1, new("rule", edit.RuleCode), new("severity", edit.Severity.ToString()));
    }
}
