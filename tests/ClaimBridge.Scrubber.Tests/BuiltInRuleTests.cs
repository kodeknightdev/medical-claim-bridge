using ClaimBridge.Contracts.Validation;
using ClaimBridge.Scrubber.Rules;
using ClaimBridge.Scrubber.Rules.BuiltIn;

namespace ClaimBridge.Scrubber.Tests;

public class BuiltInRuleTests
{
    private static List<ScrubEdit> Run(IScrubRule rule, ClaimBuilder claim) => rule.Evaluate(claim.Context()).ToList();

    [Fact]
    public void Clean_claim_passes_every_built_in_rule()
    {
        var engine = new ScrubEngine([new RequiredFieldsRule(), new NpiCheckDigitRule(), new DiagnosisCodeRule(), new DiagnosisPointerRule(),
            new ProcedureLineRule(), new ModifierRule(), new MedicalNecessityRule(), new DateLogicRule(), new TimelyFilingRule(),
            new EligibilityRule(), new DuplicateClaimRule(), new DuplicateLineRule(), new FrequencyCodeRule()]);

        var result = engine.Run(new ClaimBuilder().Context());

        result.Passed.ShouldBeTrue();
        result.Edits.ShouldBeEmpty();
        result.RulesEvaluated.ShouldBe(13);
    }

    [Fact]
    public void Required_fields_flag_each_missing_value()
    {
        var edits = Run(new RequiredFieldsRule(), new ClaimBuilder().Member(null).BillingNpi(null).Dob(null).Gender("X").Dx());
        edits.Select(e => e.Field).ShouldBe(["billingNpi", "memberId", "patientDob", "patientGender", "diagnoses"], ignoreOrder: true);
        edits.ShouldAllBe(e => e.Severity == Severity.Error);
    }

    [Theory]
    [InlineData("1999000015", true)]
    [InlineData("1999000114", true)]
    [InlineData("1234567893", true)]
    [InlineData("1999000016", false)]
    [InlineData("3999000015", false)]
    [InlineData("199900001", false)]
    [InlineData("19990000AB", false)]
    public void Npi_check_digit_uses_luhn_with_80840_prefix(string npi, bool valid) => Npi.IsValid(npi).ShouldBe(valid);

    [Fact]
    public void Npi_rule_reports_billing_and_rendering_separately()
    {
        var edits = Run(new NpiCheckDigitRule(), new ClaimBuilder().BillingNpi("1999000016").RenderingNpi("1999000115"));
        edits.Select(e => e.Field).ShouldBe(["billingNpi", "renderingNpi"]);
    }

    [Theory]
    [InlineData("E11.9", true)]
    [InlineData("S83.241A", true)]
    [InlineData("F32.A", true)]
    [InlineData("I10", true)]
    [InlineData("E119", false)]
    [InlineData("U07.1", false)]
    [InlineData("11.9", false)]
    [InlineData("E11.12345", false)]
    public void Icd10_format(string code, bool valid) => DiagnosisCodeRule.IcdFormat().IsMatch(code).ShouldBe(valid);

    [Fact]
    public void Diagnosis_rule_rejects_unknown_header_and_too_many_codes()
    {
        Run(new DiagnosisCodeRule(), new ClaimBuilder().Dx("Z99.89")).Single().Message.ShouldContain("not in the ICD-10-CM code set");

        var header = Run(new DiagnosisCodeRule(), new ClaimBuilder().Dx("E11")).Single();
        header.Message.ShouldContain("category header");
        header.FixSuggestion!.ShouldContain("E11.");

        Run(new DiagnosisCodeRule(), new ClaimBuilder().Dx(Enumerable.Repeat("I10", 13).ToArray()))
            .ShouldContain(e => e.Message.Contains("At most 12"));
    }

    [Fact]
    public void Diagnosis_pointers_must_point_at_existing_diagnoses()
    {
        Run(new DiagnosisPointerRule(), new ClaimBuilder().Dx("I10").Line("99213", ptr: [3])).Single().Message.ShouldContain("Pointer 3");
        Run(new DiagnosisPointerRule(), new ClaimBuilder().Line("99213", ptr: [])).Single().Message.ShouldContain("no diagnosis pointer");
        Run(new DiagnosisPointerRule(), new ClaimBuilder().Dx("I10", "E78.5").Line("99213", ptr: [1, 2])).ShouldBeEmpty();
    }

    [Fact]
    public void Procedure_lines_check_code_units_charge_and_place_of_service()
    {
        var edits = Run(new ProcedureLineRule(), new ClaimBuilder().Line("99999", units: 0, charge: 0, pos: "99"));
        edits.Count.ShouldBe(4);
        Run(new ProcedureLineRule(), new ClaimBuilder().Line("97110", units: 1.5m)).Single().Message.ShouldContain("whole number");
    }

    [Fact]
    public void Modifier_25_on_a_procedure_line_is_an_error()
    {
        var edit = Run(new ModifierRule(), new ClaimBuilder().Line("99214").Line("20610", mods: ["25"])).Single(e => e.Severity == Severity.Error);
        edit.LineNumber.ShouldBe(2);
        edit.FixSuggestion!.ShouldContain("E/M line");
    }

    [Fact]
    public void Modifier_25_without_a_procedure_is_a_warning()
    {
        Run(new ModifierRule(), new ClaimBuilder().Line("99214", mods: ["25"])).Single().Severity.ShouldBe(Severity.Warning);
        Run(new ModifierRule(), new ClaimBuilder().Line("99214", mods: ["25"]).Line("20610")).ShouldBeEmpty();
    }

    [Fact]
    public void Left_and_right_on_one_line_is_rejected()
    {
        Run(new ModifierRule(), new ClaimBuilder().Line("73562", mods: ["LT", "RT"])).ShouldContain(e => e.Message.Contains("LT and RT"));
    }

    [Fact]
    public void Medical_necessity_requires_a_supporting_diagnosis()
    {
        var edit = Run(new MedicalNecessityRule(), new ClaimBuilder().Dx("I10").Line("73721")).Single();
        edit.FixSuggestion!.ShouldContain("M17");
        Run(new MedicalNecessityRule(), new ClaimBuilder().Dx("M17.11").Line("73721")).ShouldBeEmpty();
        // codes without a necessity entry are not checked
        Run(new MedicalNecessityRule(), new ClaimBuilder().Dx("I10").Line("99213")).ShouldBeEmpty();
    }

    [Fact]
    public void Date_logic_rejects_future_service_and_service_before_birth()
    {
        var edits = Run(new DateLogicRule(), new ClaimBuilder().Dob(new DateOnly(2025, 11, 1)).Line("99213", dos: new DateOnly(2025, 12, 1))
            .Line("99212", dos: new DateOnly(2025, 10, 1)));
        edits.ShouldContain(e => e.Message.Contains("in the future"));
        edits.ShouldContain(e => e.Message.Contains("before the patient's date of birth"));
    }

    [Theory]
    [InlineData("PAYER-A", 91, Severity.Error)]
    [InlineData("PAYER-A", 85, Severity.Warning)]
    [InlineData("PAYER-B", 91, null)]
    [InlineData("PAYER-B", 181, Severity.Error)]
    [InlineData("PAYER-C", 300, null)]
    public void Timely_filing_follows_each_payers_limit(string payer, int daysAfterService, Severity? expected)
    {
        var dos = ClaimBuilder.Today.AddDays(-daysAfterService);
        var edits = Run(new TimelyFilingRule(), new ClaimBuilder().Payer(payer).Line("99213", dos: dos));
        if (expected is null) edits.ShouldBeEmpty();
        else edits.Single().Severity.ShouldBe(expected.Value);
    }

    [Fact]
    public void Eligibility_unknown_warns_inactive_errors_and_coverage_window_is_checked()
    {
        Run(new EligibilityRule(), new ClaimBuilder().Eligibility(EligibilityInfo.Unknown)).Single().Severity.ShouldBe(Severity.Warning);
        Run(new EligibilityRule(), new ClaimBuilder().Eligibility(new(EligibilityStatus.Inactive))).Single().Severity.ShouldBe(Severity.Error);
        Run(new EligibilityRule(), new ClaimBuilder()
                .Eligibility(new(EligibilityStatus.Active, new DateOnly(2025, 1, 1), new DateOnly(2025, 10, 31)))
                .Line("99213", dos: new DateOnly(2025, 11, 3)))
            .Single().Message.ShouldContain("not active on 2025-11-03");
    }

    [Fact]
    public void Possible_duplicate_blocks_an_original_but_not_a_replacement()
    {
        Run(new DuplicateClaimRule(), new ClaimBuilder().DuplicateOf("CB250000123")).Single().Severity.ShouldBe(Severity.Error);
        Run(new DuplicateClaimRule(), new ClaimBuilder().DuplicateOf("CB250000123").Frequency("7", "CB250000123")).ShouldBeEmpty();
    }

    [Fact]
    public void Repeated_lines_with_the_same_code_date_and_modifiers_warn()
    {
        var dos = ClaimBuilder.Today.AddDays(-2);
        Run(new DuplicateLineRule(), new ClaimBuilder().Line("97110", dos: dos).Line("97110", dos: dos)).Single().LineNumber.ShouldBe(2);
        Run(new DuplicateLineRule(), new ClaimBuilder().Line("73562", ["RT"], dos: dos).Line("73562", ["LT"], dos: dos)).ShouldBeEmpty();
    }

    [Fact]
    public void Replacement_and_void_need_the_original_claim_number()
    {
        Run(new FrequencyCodeRule(), new ClaimBuilder().Frequency("7")).Single().Field.ShouldBe("originalClaimNumber");
        Run(new FrequencyCodeRule(), new ClaimBuilder().Frequency("8", "CB250000001")).ShouldBeEmpty();
        Run(new FrequencyCodeRule(), new ClaimBuilder().Frequency("2")).Single().Message.ShouldContain("not supported");
    }

    [Fact]
    public void Errors_sort_before_warnings_and_fail_the_claim()
    {
        var engine = new ScrubEngine([new EligibilityRule(), new NpiCheckDigitRule()]);
        var result = engine.Run(new ClaimBuilder().Eligibility(EligibilityInfo.Unknown).BillingNpi("1999000016").Context());
        result.Passed.ShouldBeFalse();
        result.Edits[0].Severity.ShouldBe(Severity.Error);
        result.Errors.ShouldBe(1);
        result.Warnings.ShouldBe(1);
    }
}
