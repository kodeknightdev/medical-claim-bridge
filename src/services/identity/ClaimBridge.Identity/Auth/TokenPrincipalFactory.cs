using System.Collections.Immutable;
using System.Security.Claims;
using ClaimTypesEx = ClaimBridge.Contracts.Reference.ClaimTypesEx;
using ClaimBridge.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ClaimBridge.Identity.Auth;

public class TokenPrincipalFactory(UserManager<AppUser> users, IOpenIddictScopeManager scopeManager)
{
    public async Task<ClaimsPrincipal> CreateForUserAsync(AppUser user, ImmutableArray<string> scopes)
    {
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id.ToString())
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.Name, user.DisplayName)
            .SetClaim(Claims.PreferredUsername, user.UserName)
            .SetClaims(Claims.Role, [.. await users.GetRolesAsync(user)]);

        if (user.OrgId is { } orgId)
            identity.SetClaim(ClaimTypesEx.OrgId, orgId.ToString());

        identity.SetScopes(scopes);
        identity.SetResources(await ResourcesForAsync(scopes));
        identity.SetDestinations(GetDestinations);
        return new ClaimsPrincipal(identity);
    }

    public async Task<ImmutableArray<string>> ResourcesForAsync(ImmutableArray<string> scopes)
    {
        var resources = ImmutableArray.CreateBuilder<string>();
        await foreach (var resource in scopeManager.ListResourcesAsync(scopes))
            resources.Add(resource);
        return resources.Distinct().ToImmutableArray();
    }

    public static IEnumerable<string> GetDestinations(Claim claim)
    {
        var principal = claim.Subject!;
        switch (claim.Type)
        {
            case Claims.Name or Claims.PreferredUsername:
                yield return Destinations.AccessToken;
                if (principal.HasScope(Scopes.Profile)) yield return Destinations.IdentityToken;
                yield break;
            case Claims.Email:
                yield return Destinations.AccessToken;
                if (principal.HasScope(Scopes.Email)) yield return Destinations.IdentityToken;
                yield break;
            case Claims.Role:
                yield return Destinations.AccessToken;
                if (principal.HasScope(Scopes.Roles)) yield return Destinations.IdentityToken;
                yield break;
            case ClaimTypesEx.OrgId:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;
            // identity's security stamp must never leave the server
            case "AspNet.Identity.SecurityStamp":
                yield break;
            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
