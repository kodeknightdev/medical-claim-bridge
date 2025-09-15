namespace ClaimBridge.Scrubber.Rules.BuiltIn;

public sealed class DateLogicRule : IScrubRule
{
    public string Code => "CB008";
    public string Name => "Date logic";

    public IEnumerable<ScrubEdit> Evaluate(ScrubContext ctx)
    {
        var dob = ctx.Claim.PatientDob;
        if (dob > ctx.Today)
            yield return new(Code, Severity.Error, "Date of birth is in the future.", "patientDob", null);
        if (dob is { } birth && ctx.Today.Year - birth.Year > 120)
            yield return new(Code, Severity.Error, "Date of birth makes the patient older than 120.", "patientDob", "Check the year.");

        foreach (var line in ctx.Claim.Lines)
        {
            if (line.ServiceDate > ctx.Today)
                yield return new(Code, Severity.Error, "Date of service is in the future.", "serviceDate",
                    "Claims can only be filed after the service is provided.", line.LineNumber);
            if (dob is { } b && line.ServiceDate < b)
                yield return new(Code, Severity.Error, "Date of service is before the patient's date of birth.", "serviceDate",
                    null, line.LineNumber);
        }
    }
}

public sealed class TimelyFilingRule : IScrubRule
{
    public const int WarningWindowDays = 10;
    public string Code => "CB009";
    public string Name => "Timely filing limit";

    public IEnumerable<ScrubEdit> Evaluate(ScrubContext ctx)
    {
        if (ctx.Claim.Lines.Count == 0) yield break;
        if (!ctx.CodeSets.TimelyFilingDays.TryGetValue(ctx.Claim.PayerId, out var limit)) yield break;

        // payers count from the earliest date of service on the claim
        var earliest = ctx.Claim.Lines.Min(l => l.ServiceDate);
        var age = ctx.Claim.SubmissionDate.DayNumber - earliest.DayNumber;
        if (age > limit)
            yield return new(Code, Severity.Error,
                $"Filed {age} days after service; {ctx.Claim.PayerId} allows {limit} days.", "serviceDate",
                "Only an appeal with proof of timely filing can recover this claim.");
        else if (age > limit - WarningWindowDays)
            yield return new(Code, Severity.Warning,
                $"{limit - age} day(s) left in {ctx.Claim.PayerId}'s {limit}-day filing window.", "serviceDate",
                "Submit today to stay inside the limit.");
    }
}
