using System.Security.Claims;
using ClaimBridge.Contracts.Reference;

namespace ClaimBridge.ServiceDefaults.Auth;

public static class PrincipalExtensions
{
    public static bool IsStaff(this ClaimsPrincipal user) =>
        user.IsInRole(Roles.Admin) || user.IsInRole(Roles.OpsAnalyst);

    public static Guid? OrgId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(ClaimTypesEx.OrgId)?.Value, out var id) ? id : null;

    /// <summary>Staff see every organisation; provider users and API clients only their own.</summary>
    public static bool CanAccessOrg(this ClaimsPrincipal user, Guid orgId) =>
        user.IsStaff() || user.OrgId() == orgId;

    /// <summary>Org filter to apply to queries: null means unrestricted (staff).</summary>
    public static Guid? OrgFilter(this ClaimsPrincipal user, Guid? requested = null) =>
        user.IsStaff() ? requested : user.OrgId() ?? Guid.Empty;

    public static string? ClientId(this ClaimsPrincipal user) => user.FindFirst(ClaimTypesEx.ClientId)?.Value;

    public static string Actor(this ClaimsPrincipal user) =>
        user.FindFirst("preferred_username")?.Value ?? user.FindFirst("sub")?.Value ?? "anonymous";

    public static bool HasScope(this ClaimsPrincipal user, string scope) =>
        user.FindAll(ClaimTypesEx.Scope).Any(c => c.Value.Split(' ').Contains(scope));
}
