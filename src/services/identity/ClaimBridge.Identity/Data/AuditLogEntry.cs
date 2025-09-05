namespace ClaimBridge.Identity.Data;

public class AuditLogEntry
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Actor { get; set; } = "";
    public Guid? ActorOrgId { get; set; }
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string? Detail { get; set; }
}
