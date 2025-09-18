namespace ClaimBridge.Claims.Domain;

public class Claim
{
    public Guid Id { get; set; }
    public string ClaimNumber { get; set; } = "";
    public Guid OrgId { get; set; }
    public int Version { get; set; } = 1;
    public Guid? PreviousClaimId { get; set; }
    public Guid? SupersededByClaimId { get; set; }
    public string FrequencyCode { get; set; } = "1";
    public string? OriginalClaimNumber { get; set; }

    public string PayerId { get; set; } = "";
    public string BillingNpi { get; set; } = "";
    public string? RenderingNpi { get; set; }

    // PHI: encrypted with IPhiProtector, hash/last4 kept for search and duplicate checks
    public string MemberIdProtected { get; set; } = "";
    public string MemberIdHash { get; set; } = "";
    public string MemberIdLast4 { get; set; } = "";
    public string PatientFirstNameProtected { get; set; } = "";
    public string PatientLastNameProtected { get; set; } = "";
    public string PatientDobProtected { get; set; } = "";
    public string PatientGender { get; set; } = "U";
    public int PatientBirthYear { get; set; }

    public string? PriorAuthNumber { get; set; }
    public List<string> Diagnoses { get; set; } = [];
    public List<ServiceLine> Lines { get; set; } = [];

    public ClaimStatus Status { get; set; }
    public DateTimeOffset StatusChangedAt { get; set; }
    public ClaimSource Source { get; set; }
    public Guid? BatchId { get; set; }
    public string SubmittedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    public decimal TotalCharge { get; set; }
    public decimal? PaidAmount { get; set; }
    public decimal? AllowedAmount { get; set; }
    public decimal? PatientResponsibility { get; set; }
    public decimal? AdjustmentAmount { get; set; }
    public string? DenialCategory { get; set; }
    public string? DenialCarc { get; set; }

    public decimal? DenialRisk { get; set; }
    public string? DenialRiskModel { get; set; }
    public string? DenialRiskFactorsJson { get; set; }

    public string Fingerprint { get; set; } = "";
    public string? PossibleDuplicateOf { get; set; }
    public int ScrubAttempts { get; set; }
    public string? PayerClaimId { get; set; }
    public DateTimeOffset? FirstSubmittedAt { get; set; }
    public DateTimeOffset? LastSubmittedAt { get; set; }

    public List<ClaimStatusEvent> History { get; set; } = [];

    public byte[] RowVersion { get; set; } = [];

    public DateOnly FirstServiceDate => Lines.Count == 0 ? DateOnly.MinValue : Lines.Min(l => l.ServiceDate);

    public void RecalculateTotal() => TotalCharge = Lines.Sum(l => l.Charge);

    /// <summary>Moves the claim through the lifecycle and appends an immutable history entry.</summary>
    public ClaimStatusEvent Fire(LifecycleTrigger trigger, DateTimeOffset at, string source, string? reason = null, string? detail = null)
    {
        var from = Status;
        var to = ClaimLifecycle.Next(from, trigger);
        Status = to;
        StatusChangedAt = at;
        return Append(from, to, trigger.ToString(), at, source, reason, detail);
    }

    /// <summary>History note without a status change, e.g. a status inquiry was sent.</summary>
    public ClaimStatusEvent Note(string what, DateTimeOffset at, string source, string? reason = null, string? detail = null) =>
        Append(Status, Status, what, at, source, reason, detail);

    private ClaimStatusEvent Append(ClaimStatus from, ClaimStatus to, string trigger, DateTimeOffset at, string source, string? reason, string? detail)
    {
        var entry = new ClaimStatusEvent
        {
            ClaimId = Id,
            Sequence = History.Count == 0 ? 1 : History.Max(h => h.Sequence) + 1,
            FromStatus = from,
            ToStatus = to,
            Trigger = trigger,
            Reason = reason is { Length: > 500 } ? reason[..500] : reason,
            Source = source,
            OccurredAt = at,
            DetailJson = detail
        };
        History.Add(entry);
        return entry;
    }
}

public class ServiceLine
{
    public int LineNumber { get; set; }
    public string ProcedureCode { get; set; } = "";
    public List<string> Modifiers { get; set; } = [];
    public decimal Units { get; set; }
    public decimal Charge { get; set; }
    public DateOnly ServiceDate { get; set; }
    public string PlaceOfService { get; set; } = "11";
    public List<int> DxPointers { get; set; } = [];
}

/// <summary>Append-only status history; corrections create new claims rather than rewriting these rows.</summary>
public class ClaimStatusEvent
{
    public long Id { get; set; }
    public Guid ClaimId { get; set; }
    public int Sequence { get; set; }
    public ClaimStatus FromStatus { get; set; }
    public ClaimStatus ToStatus { get; set; }
    public string Trigger { get; set; } = "";
    public string? Reason { get; set; }
    public string Source { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public string? DetailJson { get; set; }
}
