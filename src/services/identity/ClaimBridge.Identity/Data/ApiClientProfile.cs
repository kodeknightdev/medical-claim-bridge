namespace ClaimBridge.Identity.Data;

/// <summary>Clearinghouse-side metadata for an OpenIddict client used by a provider system.</summary>
public class ApiClientProfile
{
    public string ClientId { get; set; } = "";
    public Guid OrgId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Scopes { get; set; } = "";
    public int RateLimitPerMinute { get; set; } = 60;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset? SecretRotatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public string[] ScopeList => Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
