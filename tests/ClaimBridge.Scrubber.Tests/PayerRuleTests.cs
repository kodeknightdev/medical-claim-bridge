using ClaimBridge.Scrubber.Rules;
using ClaimBridge.Scrubber.Rules.PayerRules;

namespace ClaimBridge.Scrubber.Tests;

public class PayerRuleTests
{
    private static DataDrivenPayerRule Rule(string condition, string scope = "line", Severity severity = Severity.Error) =>
        new("PA-T01", "test rule", scope == "claim", PayerRuleCompiler.Compile(condition), severity, "test message", "test fix");

    [Fact]
    public void Prior_auth_rule_fires_only_for_listed_codes_without_auth()
    {
        var rule = Rule("""{"all":[{"procedureIn":["73721","72148"]},{"hasPriorAuth":false}]}""");

        rule.Evaluate(new ClaimBuilder().Dx("M17.11").Line("99204").Line("73721").Context()).Single().LineNumber.ShouldBe(2);
        rule.Evaluate(new ClaimBuilder().Dx("M17.11").Line("73721").PriorAuth("PA123").Context()).ShouldBeEmpty();
        rule.Evaluate(new ClaimBuilder().Line("99213").Context()).ShouldBeEmpty();
    }

    [Fact]
    public void Units_threshold_and_modifier_operators()
    {
        Rule("""{"all":[{"procedureIn":["97110"]},{"unitsAbove":4}]}""")
            .Evaluate(new ClaimBuilder().Line("97110", units: 6).Line("97110", units: 3, dos: ClaimBuilder.Today.AddDays(-1)).Context())
            .Single().LineNumber.ShouldBe(1);
        Rule("""{"modifierPresent":"59"}""").Evaluate(new ClaimBuilder().Line("20610", ["59"]).Context()).Count().ShouldBe(1);
        Rule("""{"all":[{"posIn":["02"]},{"modifierAbsent":"95"}]}""")
            .Evaluate(new ClaimBuilder().Line("99213", pos: "02").Context()).Count().ShouldBe(1);
    }

    [Fact]
    public void Any_not_and_category_compose()
    {
        var rule = Rule("""{"any":[{"procedureCategory":"Imaging"},{"not":{"chargeAbove":10}}]}""");
        rule.Evaluate(new ClaimBuilder().Line("73562").Context()).Count().ShouldBe(1);
        rule.Evaluate(new ClaimBuilder().Line("99213", charge: 5).Context()).Count().ShouldBe(1);
        rule.Evaluate(new ClaimBuilder().Line("99213", charge: 120).Context()).ShouldBeEmpty();
    }

    [Fact]
    public void Claim_scope_fires_once_however_many_lines_match()
    {
        Rule("""{"chargeAbove":10}""", scope: "claim")
            .Evaluate(new ClaimBuilder().Line("99213").Line("36415", charge: 15).Context()).Single().LineNumber.ShouldBeNull();
    }

    [Fact]
    public void Diagnosis_prefix_frequency_and_age_operators()
    {
        Rule("""{"dxPrefixAny":["M17"]}""").Evaluate(new ClaimBuilder().Dx("M17.11").Context()).ShouldNotBeEmpty();
        Rule("""{"frequencyIn":["7"]}""").Evaluate(new ClaimBuilder().Frequency("7", "CB1").Context()).ShouldNotBeEmpty();
        Rule("""{"patientAgeBelow":18}""").Evaluate(new ClaimBuilder().Dob(new DateOnly(2015, 1, 1)).Context()).ShouldNotBeEmpty();
        Rule("""{"patientAgeBelow":18}""").Evaluate(new ClaimBuilder().Context()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"procedureIn":"73721"}""")]
    [InlineData("""{"all":[]}""")]
    [InlineData("""{"unknownOp":true}""")]
    [InlineData("""{"procedureIn":["1"],"unitsAbove":2}""")]
    public void Bad_conditions_are_rejected_with_a_readable_error(string json) =>
        Should.Throw<PayerRuleSyntaxException>(() => PayerRuleCompiler.Compile(json));

    [Fact]
    public void Compiled_specs_survive_the_json_document_being_disposed()
    {
        // regression: specs used to keep JsonElements from a disposed document
        var spec = PayerRuleCompiler.Compile("""{"modifierPresent":"25"}""");
        GC.Collect();
        var context = new ClaimBuilder().Line("99214", ["25"]).Context();
        spec.IsSatisfiedBy(new LineContext(context, context.Claim.Lines[0])).ShouldBeTrue();
    }

    [Fact]
    public void Every_seeded_payer_rule_compiles()
    {
        foreach (var condition in SeededConditions())
            Should.NotThrow(() => PayerRuleCompiler.Compile(condition));
    }

    private static IEnumerable<string> SeededConditions() =>
    [
        """{"all":[{"procedureIn":["73721","72148"]},{"hasPriorAuth":false}]}""",
        """{"all":[{"modifierPresent":"25"},{"procedureCategory":"EM"}]}""",
        """{"all":[{"procedureIn":["97110","97140"]},{"unitsAbove":4}]}""",
        """{"modifierPresent":"59"}""",
        """{"chargeAbove":5000}""",
        """{"all":[{"posIn":["02","10"]},{"modifierAbsent":"95"}]}"""
    ];
}
