using ClaimBridge.Claims.Application;
using ClaimBridge.X12.Core;
using ClaimBridge.X12.Transactions;

namespace ClaimBridge.Claims.Batches;

/// <summary>Maps an inbound simplified 837P from a provider system onto claim requests.</summary>
public static class X12BatchParser
{
    public static IReadOnlyList<ParsedClaim> Parse(string text)
    {
        IReadOnlyList<Parsed837Claim> claims;
        try
        {
            claims = Claim837PReader.Read(text);
        }
        catch (X12ParseException ex)
        {
            return [new ParsedClaim("file", null, "X12 file rejected: " + ex.Message)];
        }

        // the provider's own CLM01 becomes the batch reference; we always assign our own claim number
        return claims.Select(c => new ParsedClaim(c.ClaimNumber, new ClaimRequest(
            c.PayerId,
            c.BillingNpi,
            c.RenderingNpi,
            new PatientRequest(c.Subscriber.MemberId, c.Subscriber.FirstName, c.Subscriber.LastName, c.Subscriber.DateOfBirth,
                c.Subscriber.Gender),
            [.. c.Diagnoses],
            [.. c.Lines.Select(l => new ServiceLineRequest(l.ProcedureCode, [.. l.Modifiers], l.Units, l.Charge, l.ServiceDate,
                l.PlaceOfService, [.. l.DxPointers]))],
            c.PriorAuthNumber,
            c.FrequencyCode,
            c.FrequencyCode == "1" ? null : c.OriginalReference), null)).ToList();
    }
}
