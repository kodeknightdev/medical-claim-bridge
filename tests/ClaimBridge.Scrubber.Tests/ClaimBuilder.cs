using ClaimBridge.Scrubber.CodeSetsCache;
using ClaimBridge.Scrubber.Data;
using ClaimBridge.Scrubber.Rules;

namespace ClaimBridge.Scrubber.Tests;

/// <summary>A clean claim by default; each test bends one thing.</summary>
public sealed class ClaimBuilder
{
    public static readonly DateOnly Today = new(2025, 11, 10);

    private string _payer = "PAYER-A";
    private string? _billingNpi = "1999000015";
    private string? _renderingNpi = "1999000114";
    private string? _memberId = "NWH1000002";
    private DateOnly? _dob = new(1958, 2, 12);
    private string? _gender = "F";
    private string _frequency = "1";
    private string? _original;
    private string? _priorAuth;
    private List<string> _dx = ["I10"];
    private readonly List<ScrubLine> _lines = [];
    private DateOnly _submitted = Today;
    private EligibilityInfo _eligibility = new(EligibilityStatus.Active, new DateOnly(2020, 1, 1));
    private string? _duplicateOf;

    public ClaimBuilder Payer(string payer) { _payer = payer; return this; }
    public ClaimBuilder BillingNpi(string? npi) { _billingNpi = npi; return this; }
    public ClaimBuilder RenderingNpi(string? npi) { _renderingNpi = npi; return this; }
    public ClaimBuilder Member(string? id) { _memberId = id; return this; }
    public ClaimBuilder Dob(DateOnly? dob) { _dob = dob; return this; }
    public ClaimBuilder Gender(string? g) { _gender = g; return this; }
    public ClaimBuilder Frequency(string f, string? original = null) { _frequency = f; _original = original; return this; }
    public ClaimBuilder PriorAuth(string? pa) { _priorAuth = pa; return this; }
    public ClaimBuilder Dx(params string[] dx) { _dx = [.. dx]; return this; }
    public ClaimBuilder SubmittedOn(DateOnly d) { _submitted = d; return this; }
    public ClaimBuilder Eligibility(EligibilityInfo e) { _eligibility = e; return this; }
    public ClaimBuilder DuplicateOf(string? claimNumber) { _duplicateOf = claimNumber; return this; }

    public ClaimBuilder Line(string code, string[]? mods = null, decimal units = 1, decimal charge = 120, DateOnly? dos = null,
        string pos = "11", int[]? ptr = null)
    {
        _lines.Add(new ScrubLine(_lines.Count + 1, code, mods ?? [], units, charge, dos ?? Today.AddDays(-3), pos, ptr ?? [1]));
        return this;
    }

    public ScrubClaim Build()
    {
        var lines = _lines.Count == 0 ? [new ScrubLine(1, "99213", [], 1, 120, Today.AddDays(-3), "11", [1])] : _lines;
        return new ScrubClaim(Guid.NewGuid(), _payer, _billingNpi, _renderingNpi, _memberId, _dob, _gender, _frequency, _original,
            _priorAuth, _dx, lines, _submitted, _eligibility, _duplicateOf);
    }

    public ScrubContext Context() => new(Build(), CodeSets.Snapshot, Today);
}

public static class CodeSets
{
    /// <summary>The real seeded code sets, loaded from the scrubber's embedded JSON.</summary>
    public static readonly CodeSetSnapshot Snapshot = Load();

    private sealed record ModifierJson(string Code, string Description, string[] AllowedCategories);
    private sealed record NecessityJson(string Procedure, string[] DxPrefixes);

    private static CodeSetSnapshot Load()
    {
        var icd = CodeSetSeeder.Load<List<IcdCode>>("icd10cm.json").ToDictionary(c => c.Code, c => c.Billable);
        var procs = CodeSetSeeder.Load<List<ProcedureCode>>("procedures.json")
            .ToDictionary(p => p.Code, p => new ProcedureInfo(p.Code, p.Description, p.Category, p.TypicalCharge));
        var mods = CodeSetSeeder.Load<List<ModifierJson>>("modifiers.json")
            .ToDictionary(m => m.Code, m => new ModifierInfo(m.Code, m.Description, m.AllowedCategories));
        var necessity = CodeSetSeeder.Load<List<NecessityJson>>("medical-necessity.json").ToDictionary(n => n.Procedure, n => n.DxPrefixes);
        var filing = new Dictionary<string, int> { ["PAYER-A"] = 90, ["PAYER-B"] = 180, ["PAYER-C"] = 365 };
        return new CodeSetSnapshot(icd, procs, mods, necessity, filing);
    }
}
