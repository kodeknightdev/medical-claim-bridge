using System.Text.RegularExpressions;
using FluentValidation;

namespace ClaimBridge.Claims.Application;

public sealed record PatientRequest(string MemberId, string FirstName, string LastName, DateOnly DateOfBirth, string Gender);

public sealed record ServiceLineRequest(
    string ProcedureCode,
    List<string>? Modifiers,
    decimal Units,
    decimal Charge,
    DateOnly ServiceDate,
    string PlaceOfService,
    List<int> DxPointers);

/// <summary>Shape shared by the portal form, batch rows and the public submission API.</summary>
public sealed record ClaimRequest(
    string PayerId,
    string BillingNpi,
    string? RenderingNpi,
    PatientRequest Patient,
    List<string> Diagnoses,
    List<ServiceLineRequest> Lines,
    string? PriorAuthNumber = null,
    string FrequencyCode = "1",
    string? OriginalClaimNumber = null,
    Guid? OrgId = null);

/// <summary>Structural checks only. Coding and payer rules belong to the scrubber so they can be shown inline.</summary>
public sealed partial class ClaimRequestValidator : AbstractValidator<ClaimRequest>
{
    public ClaimRequestValidator()
    {
        RuleFor(x => x.PayerId).NotEmpty().MaximumLength(20);
        RuleFor(x => x.BillingNpi).NotEmpty().Matches(TenDigits()).WithMessage("Billing NPI must be 10 digits.");
        RuleFor(x => x.RenderingNpi).Matches(TenDigits()).When(x => !string.IsNullOrEmpty(x.RenderingNpi))
            .WithMessage("Rendering NPI must be 10 digits.");
        RuleFor(x => x.Patient).NotNull();
        RuleFor(x => x.Patient.MemberId).NotEmpty().MaximumLength(30).When(x => x.Patient is not null);
        RuleFor(x => x.Patient.FirstName).NotEmpty().MaximumLength(60).When(x => x.Patient is not null);
        RuleFor(x => x.Patient.LastName).NotEmpty().MaximumLength(60).When(x => x.Patient is not null);
        RuleFor(x => x.Patient.Gender).Must(g => g is "M" or "F" or "U").When(x => x.Patient is not null)
            .WithMessage("Gender must be M, F or U.");
        RuleFor(x => x.Diagnoses).NotNull().Must(d => d.Count <= 12).WithMessage("No more than 12 diagnoses.");
        RuleFor(x => x.Lines).NotEmpty().Must(l => l.Count <= 50).WithMessage("Between 1 and 50 service lines.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProcedureCode).NotEmpty().Length(5);
            line.RuleFor(l => l.Units).GreaterThan(0).LessThanOrEqualTo(999);
            line.RuleFor(l => l.Charge).GreaterThan(0).LessThan(1_000_000);
            line.RuleFor(l => l.PlaceOfService).NotEmpty().Length(2);
            line.RuleFor(l => l.Modifiers).Must(m => m is null || m.Count <= 4).WithMessage("No more than 4 modifiers per line.");
            line.RuleFor(l => l.DxPointers).NotNull();
        });
        RuleFor(x => x.FrequencyCode).Must(f => f is "1" or "7" or "8").WithMessage("Frequency code must be 1, 7 or 8.");
        RuleFor(x => x.OriginalClaimNumber).NotEmpty().When(x => x.FrequencyCode is "7" or "8")
            .WithMessage("Replacement and void claims need the original claim number.");
        RuleFor(x => x.PriorAuthNumber).MaximumLength(30);
    }

    [GeneratedRegex(@"^\d{10}$")]
    private static partial Regex TenDigits();
}
