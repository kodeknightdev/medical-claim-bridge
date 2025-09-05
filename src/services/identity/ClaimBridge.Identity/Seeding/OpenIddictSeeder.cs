using ClaimBridge.Contracts.Reference;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;
using CbScopes = ClaimBridge.Contracts.Reference.Scopes;

namespace ClaimBridge.Identity.Seeding;

public static class OpenIddictSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        var scopes = services.GetRequiredService<IOpenIddictScopeManager>();
        foreach (var name in new[] { CbScopes.Api, CbScopes.ClaimsRead, CbScopes.ClaimsSubmit })
        {
            if (await scopes.FindByNameAsync(name) is not null) continue;
            await scopes.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = name,
                DisplayName = name,
                Resources = { CbScopes.Audience }
            });
        }

        var apps = services.GetRequiredService<IOpenIddictApplicationManager>();
        var portal = new OpenIddictApplicationDescriptor
        {
            ClientId = SeedIds.PortalClientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "claim-bridge portal",
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Roles,
                Permissions.Prefixes.Scope + CbScopes.Api,
                Permissions.Prefixes.Scope + CbScopes.ClaimsRead,
                Permissions.Prefixes.Scope + CbScopes.ClaimsSubmit
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange }
        };
        foreach (var uri in config.GetSection("Auth:PortalRedirectUris").Get<string[]>() ?? [])
            portal.RedirectUris.Add(new Uri(uri));
        foreach (var uri in config.GetSection("Auth:PortalPostLogoutUris").Get<string[]>() ?? [])
            portal.PostLogoutRedirectUris.Add(new Uri(uri));

        var existing = await apps.FindByClientIdAsync(SeedIds.PortalClientId);
        if (existing is null)
            await apps.CreateAsync(portal);
        else
            await apps.UpdateAsync(existing, portal);
    }
}
