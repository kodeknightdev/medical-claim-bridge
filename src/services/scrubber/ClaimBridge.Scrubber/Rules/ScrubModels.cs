namespace ClaimBridge.Scrubber.Rules;

public enum Severity { Warning, Error }

public enum EligibilityStatus { Unknown, Active, Inactive }

public sealed record EligibilityInfo(EligibilityStatus Status, DateOnly? CoverageStart = null, DateOnly? CoverageEnd = null)
{
    public static readonly EligibilityInfo Unknown = new(EligibilityStatus.Unknown);
}

public sealed record ScrubLine(
    int LineNumber,
    string ProcedureCode,
    IReadOnlyList<string> Modifiers,
    decimal Units,
    decimal Charge,
    DateOnly ServiceDate,
    string PlaceOfService,
    IReadOnlyList<int> DxPointers);

public sealed record ScrubClaim(
    Guid OrgId,
    string PayerId,
    string? BillingNpi,
    string? RenderingNpi,
    string? MemberId,
    DateOnly? PatientDob,
    string? PatientGender,
    string FrequencyCode,
    string? OriginalClaimNumber,
    string? PriorAuthNumber,
    IReadOnlyList<string> Diagnoses,
    IReadOnlyList<ScrubLine> Lines,
    DateOnly SubmissionDate,
    EligibilityInfo Eligibility,
    string? PossibleDuplicateOf);

public sealed record ScrubEdit(
    string RuleCode,
    Severity Severity,
    string Message,
    string? Field = null,
    string? FixSuggestion = null,
    int? LineNumber = null);

public sealed record ScrubResult(bool Passed, IReadOnlyList<ScrubEdit> Edits, int RulesEvaluated)
{
    public int Errors => Edits.Count(e => e.Severity == Severity.Error);
    public int Warnings => Edits.Count(e => e.Severity == Severity.Warning);
}
