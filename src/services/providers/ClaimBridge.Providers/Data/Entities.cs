namespace ClaimBridge.Providers.Data;

public class ProviderOrganization
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string GroupNpi { get; set; } = "";
    public string TaxId { get; set; } = "";
    public string Taxonomy { get; set; } = "";
    public string AddressLine1 { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string Zip { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    public List<RenderingProvider> Providers { get; set; } = [];
}

public class RenderingProvider
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public string Npi { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Taxonomy { get; set; } = "";
    public bool Active { get; set; } = true;
}

public class Payer
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Channel { get; set; } = "";
    public int TimelyFilingDays { get; set; }
}

public enum EnrollmentStatus { Pending, Active, Rejected }

public class PayerEnrollment
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public string PayerId { get; set; } = "";
    public EnrollmentStatus Status { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public string RequestedBy { get; set; } = "";
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
    public string? Note { get; set; }
}

public class WebhookSubscription
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public string Url { get; set; } = "";
    public string Events { get; set; } = "";
    public string SigningSecretProtected { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
    public string? DisabledReason { get; set; }

    public string[] EventList => Events.Split(',', StringSplitOptions.RemoveEmptyEntries);
}

public class AuditLogEntry
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string? Detail { get; set; }
}
