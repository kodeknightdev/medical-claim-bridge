using ClaimBridge.Contracts.Validation;

namespace ClaimBridge.Scrubber.Rules.BuiltIn;

public sealed class RequiredFieldsRule : IScrubRule
{
    public string Code => "CB001";
    public string Name => "Required fields";

    public IEnumerable<ScrubEdit> Evaluate(ScrubContext ctx)
    {
        var c = ctx.Claim;
        if (string.IsNullOrWhiteSpace(c.PayerId))
            yield return Error("Payer is required.", "payerId", "Pick the payer from the patient's insurance card.");
        if (string.IsNullOrWhiteSpace(c.BillingNpi))
            yield return Error("Billing provider NPI is required.", "billingNpi", "Use the organisation's group NPI.");
        if (string.IsNullOrWhiteSpace(c.MemberId))
            yield return Error("Member ID is required.", "memberId", "Copy the member ID exactly as printed on the card.");
        if (c.PatientDob is null)
            yield return Error("Patient date of birth is required.", "patientDob", null);
        if (c.PatientGender is not ("M" or "F" or "U"))
            yield return Error("Patient gender must be M, F or U.", "patientGender", "Use U when unknown.");
        if (c.Diagnoses.Count == 0)
            yield return Error("At least one diagnosis is required.", "diagnoses", "Add the primary ICD-10-CM code.");
        if (c.Lines.Count == 0)
            yield return Error("At least one service line is required.", "lines", null);
        else if (c.Lines.Count > 50)
            yield return Error("A professional claim can carry at most 50 service lines.", "lines", "Split the encounter into two claims.");
    }

    private ScrubEdit Error(string message, string field, string? fix) => new(Code, Severity.Error, message, field, fix);
}

public sealed class NpiCheckDigitRule : IScrubRule
{
    public string Code => "CB002";
    public string Name => "NPI check digit";

    public IEnumerable<ScrubEdit> Evaluate(ScrubContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(ctx.Claim.BillingNpi) && !Npi.IsValid(ctx.Claim.BillingNpi))
            yield return new(Code, Severity.Error, $"Billing NPI {ctx.Claim.BillingNpi} fails the check digit.", "billingNpi",
                "Re-key the NPI; a single transposed digit usually causes this.");
        if (!string.IsNullOrWhiteSpace(ctx.Claim.RenderingNpi) && !Npi.IsValid(ctx.Claim.RenderingNpi))
            yield return new(Code, Severity.Error, $"Rendering NPI {ctx.Claim.RenderingNpi} fails the check digit.", "renderingNpi",
                "Re-key the NPI or pick the provider from the list.");
    }
}

public sealed class FrequencyCodeRule : IScrubRule
{
    public string Code => "CB013";
    public string Name => "Claim frequency and original reference";

    public IEnumerable<ScrubEdit> Evaluate(ScrubContext ctx)
    {
        var c = ctx.Claim;
        if (c.FrequencyCode is not ("1" or "7" or "8"))
        {
            yield return new(Code, Severity.Error, $"Frequency code {c.FrequencyCode} is not supported.", "frequencyCode",
                "Use 1 for an original claim, 7 for a replacement or 8 for a void.");
            yield break;
        }
        if (c.FrequencyCode is "7" or "8" && string.IsNullOrWhiteSpace(c.OriginalClaimNumber))
            yield return new(Code, Severity.Error, "Replacement and void claims must reference the original claim number.",
                "originalClaimNumber", "Start the correction from the original claim so the reference is filled in.");
    }
}

public sealed class DuplicateClaimRule : IScrubRule
{
    public string Code => "CB011";
    public string Name => "Duplicate claim";

    public IEnumerable<ScrubEdit> Evaluate(ScrubContext ctx)
    {
        if (ctx.Claim.PossibleDuplicateOf is { Length: > 0 } original && ctx.Claim.FrequencyCode == "1")
            yield return new(Code, Severity.Error,
                $"Looks like a duplicate of claim {original} (same member, provider, dates and procedures).", null,
                "If this corrects the earlier claim, submit it as a replacement (frequency 7) instead.");
    }
}

public sealed class EligibilityRule : IScrubRule
{
    public string Code => "CB010";
    public string Name => "Eligibility on date of service";

    public IEnumerable<ScrubEdit> Evaluate(ScrubContext ctx)
    {
        var e = ctx.Claim.Eligibility;
        switch (e.Status)
        {
            case EligibilityStatus.Unknown:
                yield return new(Code, Severity.Warning, "Eligibility has not been verified for this member.", "memberId",
                    "Run an eligibility check before submitting.");
                break;
            case EligibilityStatus.Inactive:
                yield return new(Code, Severity.Error, "Coverage is not active for this member.", "memberId",
                    "Confirm the payer and member ID with the patient; bill the patient or the right payer.");
                break;
            case EligibilityStatus.Active:
                foreach (var line in ctx.Claim.Lines)
                {
                    var outside = (e.CoverageStart is { } s && line.ServiceDate < s) || (e.CoverageEnd is { } end && line.ServiceDate > end);
                    if (outside)
                        yield return new(Code, Severity.Error, $"Coverage was not active on {line.ServiceDate:yyyy-MM-dd}.",
                            "serviceDate", "Check the date of service or the coverage period.", line.LineNumber);
                }
                break;
        }
    }
}
