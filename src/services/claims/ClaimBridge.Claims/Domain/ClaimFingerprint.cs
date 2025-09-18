using System.Security.Cryptography;
using System.Text;

namespace ClaimBridge.Claims.Domain;

public static class ClaimFingerprint
{
    /// <summary>
    /// Business identity of a claim: same org, member, billing provider, payer, and the same set of
    /// date/procedure/modifier combinations. Charges are left out on purpose so a re-keyed amount still matches.
    /// </summary>
    public static string Compute(Guid orgId, string memberIdHash, string billingNpi, string payerId, IEnumerable<ServiceLine> lines)
    {
        var services = lines
            .Select(l => $"{l.ServiceDate:yyyyMMdd}:{l.ProcedureCode}:{string.Join('+', l.Modifiers.Order())}")
            .Order();
        var raw = $"{orgId}|{memberIdHash}|{billingNpi}|{payerId}|{string.Join(';', services)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}
