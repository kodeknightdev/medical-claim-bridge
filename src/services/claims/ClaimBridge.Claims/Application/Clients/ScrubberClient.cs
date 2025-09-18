using System.Globalization;
using ClaimBridge.Scrubber.Grpc;

namespace ClaimBridge.Claims.Application.Clients;

public sealed record EditDto(string RuleCode, string Severity, string Message, string? Field, string? FixSuggestion, int? LineNumber);

public sealed record ScrubOutcome(bool Passed, IReadOnlyList<EditDto> Edits, int RulesEvaluated);

public class ScrubberClient(ClaimScrubber.ClaimScrubberClient grpc)
{
    public async Task<ScrubOutcome> ScrubAsync(Guid claimId, int version, Guid orgId, ClaimRequest claim, EligibilityResult eligibility,
        string? possibleDuplicateOf, DateOnly submissionDate, bool persist, CancellationToken ct)
    {
        var request = new ScrubRequest
        {
            ClaimId = claimId.ToString(),
            ClaimVersion = version,
            Persist = persist,
            Claim = ToProto(orgId, claim, eligibility, possibleDuplicateOf, submissionDate)
        };
        var response = await grpc.ScrubAsync(request, deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: ct);
        return new ScrubOutcome(response.Passed,
            response.Edits.Select(e => new EditDto(e.RuleCode, e.Severity, e.Message, NullIfEmpty(e.Field), NullIfEmpty(e.FixSuggestion),
                e.LineNumber == 0 ? null : e.LineNumber)).ToList(),
            response.RulesEvaluated);
    }

    private static ScrubClaim ToProto(Guid orgId, ClaimRequest c, EligibilityResult eligibility, string? dupOf, DateOnly submissionDate)
    {
        var proto = new ScrubClaim
        {
            OrgId = orgId.ToString(),
            PayerId = c.PayerId,
            BillingNpi = c.BillingNpi,
            RenderingNpi = c.RenderingNpi ?? "",
            MemberId = c.Patient?.MemberId ?? "",
            PatientDob = c.Patient?.DateOfBirth.ToString("yyyy-MM-dd") ?? "",
            PatientGender = c.Patient?.Gender ?? "",
            FrequencyCode = c.FrequencyCode,
            OriginalClaimNumber = c.OriginalClaimNumber ?? "",
            PriorAuthNumber = c.PriorAuthNumber ?? "",
            SubmissionDate = submissionDate.ToString("yyyy-MM-dd"),
            PossibleDuplicateOf = dupOf ?? "",
            Eligibility = new Eligibility
            {
                Status = eligibility.Status,
                CoverageStart = eligibility.CoverageStart?.ToString("yyyy-MM-dd") ?? "",
                CoverageEnd = eligibility.CoverageEnd?.ToString("yyyy-MM-dd") ?? ""
            }
        };
        proto.Diagnoses.AddRange(c.Diagnoses ?? []);
        var lineNumber = 1;
        foreach (var l in c.Lines ?? [])
        {
            var line = new ServiceLine
            {
                LineNumber = lineNumber++,
                ProcedureCode = l.ProcedureCode,
                Units = l.Units.ToString(CultureInfo.InvariantCulture),
                Charge = l.Charge.ToString(CultureInfo.InvariantCulture),
                ServiceDate = l.ServiceDate.ToString("yyyy-MM-dd"),
                PlaceOfService = l.PlaceOfService
            };
            line.Modifiers.AddRange(l.Modifiers ?? []);
            line.DxPointers.AddRange(l.DxPointers ?? []);
            proto.Lines.Add(line);
        }
        return proto;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;
}
