namespace ClaimBridge.Scrubber.Data;

public class ScrubRun
{
    public Guid Id { get; set; }
    public Guid ClaimId { get; set; }
    public int ClaimVersion { get; set; }
    public Guid OrgId { get; set; }
    public string PayerId { get; set; } = "";
    public DateTimeOffset RanAt { get; set; }
    public bool Passed { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int RulesEvaluated { get; set; }

    public List<ScrubEditRow> Edits { get; set; } = [];
}

public class ScrubEditRow
{
    public long Id { get; set; }
    public Guid RunId { get; set; }
    public string RuleCode { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Field { get; set; }
    public string? FixSuggestion { get; set; }
    public int? LineNumber { get; set; }
}
