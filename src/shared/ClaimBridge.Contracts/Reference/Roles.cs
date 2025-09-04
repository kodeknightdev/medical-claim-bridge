namespace ClaimBridge.Contracts.Reference;

public static class Roles
{
    public const string Admin = "Admin";
    public const string OpsAnalyst = "OpsAnalyst";
    public const string ProviderAdmin = "ProviderAdmin";
    public const string ProviderBiller = "ProviderBiller";

    public static readonly string[] All = [Admin, OpsAnalyst, ProviderAdmin, ProviderBiller];
    public static readonly string[] Staff = [Admin, OpsAnalyst];
}

public static class Scopes
{
    public const string Api = "api";
    public const string ClaimsSubmit = "claims.submit";
    public const string ClaimsRead = "claims.read";

    public const string Audience = "claim-bridge-api";
}

public static class ClaimTypesEx
{
    public const string OrgId = "org_id";
    public const string Role = "role";
    public const string Scope = "scope";
    public const string ClientId = "client_id";
    public const string RateLimit = "cb_rate_limit";
}
