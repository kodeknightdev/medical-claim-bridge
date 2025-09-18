using ClaimBridge.Claims.Domain;
using ClaimBridge.ServiceDefaults.Phi;

namespace ClaimBridge.Claims.Application;

public sealed record PatientPhi(string MemberId, string FirstName, string LastName, DateOnly DateOfBirth, string Gender);

public class ClaimPhi(IPhiProtector protector)
{
    public void Protect(Claim claim, PatientRequest patient)
    {
        var memberId = patient.MemberId.Trim().ToUpperInvariant();
        claim.MemberIdProtected = protector.Protect(memberId);
        claim.MemberIdHash = protector.Hash(memberId);
        claim.MemberIdLast4 = memberId.Length <= 4 ? memberId : memberId[^4..];
        claim.PatientFirstNameProtected = protector.Protect(patient.FirstName.Trim());
        claim.PatientLastNameProtected = protector.Protect(patient.LastName.Trim());
        claim.PatientDobProtected = protector.Protect(patient.DateOfBirth.ToString("yyyy-MM-dd"));
        claim.PatientBirthYear = patient.DateOfBirth.Year;
        claim.PatientGender = patient.Gender;
    }

    public PatientPhi Reveal(Claim claim) => new(
        protector.Unprotect(claim.MemberIdProtected),
        protector.Unprotect(claim.PatientFirstNameProtected),
        protector.Unprotect(claim.PatientLastNameProtected),
        DateOnly.Parse(protector.Unprotect(claim.PatientDobProtected)),
        claim.PatientGender);

    public string HashMemberId(string memberId) => protector.Hash(memberId.Trim().ToUpperInvariant());

    public ClaimRequest ToRequest(Claim claim)
    {
        var p = Reveal(claim);
        return new ClaimRequest(claim.PayerId, claim.BillingNpi, claim.RenderingNpi,
            new PatientRequest(p.MemberId, p.FirstName, p.LastName, p.DateOfBirth, p.Gender),
            [.. claim.Diagnoses],
            [.. claim.Lines.OrderBy(l => l.LineNumber).Select(l => new ServiceLineRequest(l.ProcedureCode, [.. l.Modifiers], l.Units,
                l.Charge, l.ServiceDate, l.PlaceOfService, [.. l.DxPointers]))],
            claim.PriorAuthNumber, claim.FrequencyCode, claim.OriginalClaimNumber, claim.OrgId);
    }
}
