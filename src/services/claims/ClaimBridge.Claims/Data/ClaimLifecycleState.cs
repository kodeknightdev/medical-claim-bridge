namespace ClaimBridge.Claims.Data;

public enum DeadlineKind { None, Acknowledgement, StatusInquiry, Resubmission }

/// <summary>Persisted process-manager state: the durable timeout for whatever the claim is waiting on.</summary>
public class ClaimLifecycleState
{
    public Guid ClaimId { get; set; }
    public DeadlineKind DeadlineKind { get; set; }
    public DateTimeOffset? DeadlineAt { get; set; }
    public int InquiriesSent { get; set; }
    public int Resubmissions { get; set; }
    public DateTimeOffset? EscalatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void SetDeadline(DeadlineKind kind, DateTimeOffset at, DateTimeOffset now)
    {
        DeadlineKind = kind;
        DeadlineAt = at;
        UpdatedAt = now;
    }

    public void ClearDeadline(DateTimeOffset now)
    {
        DeadlineKind = DeadlineKind.None;
        DeadlineAt = null;
        UpdatedAt = now;
    }
}

public class AuditLogEntry
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Actor { get; set; } = "";
    public Guid? OrgId { get; set; }
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string? Detail { get; set; }
}
