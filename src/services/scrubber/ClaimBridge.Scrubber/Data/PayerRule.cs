namespace ClaimBridge.Scrubber.Data;

/// <summary>A payer edit defined as data. Condition is a small JSON specification tree, see PayerRuleCompiler.</summary>
public class PayerRule
{
    public int Id { get; set; }
    public string PayerId { get; set; } = "";
    public string RuleCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Scope { get; set; } = "line";
    public string ConditionJson { get; set; } = "{}";
    public string Severity { get; set; } = "Error";
    public bool Enabled { get; set; } = true;
    public string Message { get; set; } = "";
    public string? FixSuggestion { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}
