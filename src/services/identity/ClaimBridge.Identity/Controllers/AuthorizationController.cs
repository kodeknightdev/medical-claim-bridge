using System.Security.Claims;
using ClaimTypesEx = ClaimBridge.Contracts.Reference.ClaimTypesEx;
using ClaimBridge.Identity.Auth;
using ClaimBridge.Identity.Data;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ClaimBridge.Identity.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
public class AuthorizationController(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    TokenPrincipalFactory principals,
    IOpenIddictApplicationManager applications,
    IdentityDb db) : Controller
{
    [HttpGet("~/connect/authorize"), HttpPost("~/connect/authorize")]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenID Connect request missing");

        var cookie = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!cookie.Succeeded || request.HasPromptValue(PromptValues.Login))
        {
            if (request.HasPromptValue(PromptValues.None))
                return ForbidWith(Errors.LoginRequired, "The user is not logged in.");

            var parameters = Request.HasFormContentType ? Request.Form.ToList() : Request.Query.ToList();
            parameters.RemoveAll(p => p.Key == Parameters.Prompt);
            return Challenge(new AuthenticationProperties
            {
                RedirectUri = Request.PathBase + Request.Path + QueryString.Create(parameters)
            }, IdentityConstants.ApplicationScheme);
        }

        var user = await users.GetUserAsync(cookie.Principal!);
        if (user is null)
            return ForbidWith(Errors.LoginRequired, "The user account no longer exists.");

        // first-party portal only, so consent is implicit
        var principal = await principals.CreateForUserAsync(user, request.GetScopes());
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpPost("~/connect/token"), Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenID Connect request missing");

        if (request.IsClientCredentialsGrantType())
            return await ClientCredentialsAsync(request);

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var user = await users.FindByIdAsync(result.Principal?.GetClaim(Claims.Subject) ?? "");
            if (user is null || !await signIn.CanSignInAsync(user))
                return ForbidWith(Errors.InvalidGrant, "The token is no longer valid.");

            // rebuild from the database so role or org changes land on the next refresh
            var principal = await principals.CreateForUserAsync(user, result.Principal!.GetScopes());
            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        return ForbidWith(Errors.UnsupportedGrantType, "The grant type is not supported.");
    }

    private async Task<IActionResult> ClientCredentialsAsync(OpenIddictRequest request)
    {
        // OpenIddict has already authenticated the client and checked the requested scopes against its permissions
        var application = await applications.FindByClientIdAsync(request.ClientId!);
        var profile = await db.ApiClients.FindAsync(request.ClientId);
        if (application is null || profile is null || profile.RevokedAt is not null)
            return ForbidWith(Errors.InvalidClient, "The client is unknown or has been revoked.");

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, request.ClientId)
            .SetClaim(Claims.Name, profile.DisplayName)
            .SetClaim(ClaimTypesEx.OrgId, profile.OrgId.ToString())
            .SetClaim(ClaimTypesEx.RateLimit, profile.RateLimitPerMinute.ToString());

        identity.SetScopes(request.GetScopes());
        identity.SetResources(await principals.ResourcesForAsync(request.GetScopes()));
        identity.SetDestinations(TokenPrincipalFactory.GetDestinations);
        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [Authorize(AuthenticationSchemes = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)]
    [HttpGet("~/connect/userinfo"), HttpPost("~/connect/userinfo")]
    public async Task<IActionResult> UserInfo()
    {
        var user = await users.FindByIdAsync(User.GetClaim(Claims.Subject) ?? "");
        if (user is null)
            return Challenge(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        var claims = new Dictionary<string, object?>
        {
            [Claims.Subject] = user.Id.ToString(),
            [Claims.Name] = user.DisplayName,
            [Claims.Email] = user.Email,
            [Claims.Role] = await users.GetRolesAsync(user),
            ["org_id"] = user.OrgId?.ToString()
        };
        return Ok(claims);
    }

    [HttpGet("~/connect/logout"), HttpPost("~/connect/logout")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return SignOut(new AuthenticationProperties { RedirectUri = "/" },
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private ForbidResult ForbidWith(string error, string description) => Forbid(
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
        }),
        OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
