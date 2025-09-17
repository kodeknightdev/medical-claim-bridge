using System.Globalization;
using ClaimBridge.Scrubber.Rules;
using Grpc.Core;
using RuleModels = ClaimBridge.Scrubber.Rules;

namespace ClaimBridge.Scrubber.Grpc;

public class ScrubberGrpcService(ScrubService scrubber) : ClaimScrubber.ClaimScrubberBase
{
    public override async Task<ScrubResponse> Scrub(ScrubRequest request, ServerCallContext context)
    {
        if (request.Claim is null)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "claim is required"));
        if (!Guid.TryParse(request.ClaimId, out var claimId))
            claimId = Guid.Empty;

        var outcome = await scrubber.ScrubAsync(claimId, request.ClaimVersion, ToDomain(request.Claim), request.Persist,
            context.CancellationToken);

        var response = new ScrubResponse
        {
            Passed = outcome.Result.Passed,
            RulesEvaluated = outcome.Result.RulesEvaluated,
            RunId = outcome.RunId?.ToString() ?? ""
        };
        response.Edits.AddRange(outcome.Result.Edits.Select(e => new Edit
        {
            RuleCode = e.RuleCode,
            Severity = e.Severity.ToString(),
            Message = e.Message,
            Field = e.Field ?? "",
            FixSuggestion = e.FixSuggestion ?? "",
            LineNumber = e.LineNumber ?? 0
        }));
        return response;
    }

    internal static RuleModels.ScrubClaim ToDomain(ScrubClaim c) => new(
        OrgId: Guid.TryParse(c.OrgId, out var org) ? org : Guid.Empty,
        PayerId: c.PayerId,
        BillingNpi: NullIfEmpty(c.BillingNpi),
        RenderingNpi: NullIfEmpty(c.RenderingNpi),
        MemberId: NullIfEmpty(c.MemberId),
        PatientDob: Date(c.PatientDob),
        PatientGender: NullIfEmpty(c.PatientGender),
        FrequencyCode: string.IsNullOrEmpty(c.FrequencyCode) ? "1" : c.FrequencyCode,
        OriginalClaimNumber: NullIfEmpty(c.OriginalClaimNumber),
        PriorAuthNumber: NullIfEmpty(c.PriorAuthNumber),
        Diagnoses: [.. c.Diagnoses.Where(d => !string.IsNullOrWhiteSpace(d))],
        Lines: [.. c.Lines.Select(l => new ScrubLine(
            l.LineNumber, l.ProcedureCode.Trim().ToUpperInvariant(), [.. l.Modifiers.Where(m => !string.IsNullOrWhiteSpace(m))],
            Dec(l.Units), Dec(l.Charge), Date(l.ServiceDate) ?? DateOnly.MinValue, l.PlaceOfService, [.. l.DxPointers]))],
        SubmissionDate: Date(c.SubmissionDate) ?? DateOnly.FromDateTime(DateTime.UtcNow),
        Eligibility: c.Eligibility is null
            ? EligibilityInfo.Unknown
            : new EligibilityInfo(Enum.TryParse<EligibilityStatus>(c.Eligibility.Status, out var s) ? s : EligibilityStatus.Unknown,
                Date(c.Eligibility.CoverageStart), Date(c.Eligibility.CoverageEnd)),
        PossibleDuplicateOf: NullIfEmpty(c.PossibleDuplicateOf));

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static DateOnly? Date(string s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static decimal Dec(string s) =>
        decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;
}
