using System.Security.Claims;
using ClaimBridge.Contracts.Reference;

namespace ClaimBridge.Identity.Auth;

public static class PrincipalExtensions
{
    public static bool IsStaff(this ClaimsPrincipal user) =>
        user.IsInRole(Roles.Admin) || user.IsInRole(Roles.OpsAnalyst);

    public static Guid? OrgId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(ClaimTypesEx.OrgId)?.Value, out var id) ? id : null;
}
