using ClaimBridge.Contracts.Reference;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace ClaimBridge.ServiceDefaults.Auth;

public static class Policies
{
    public const string ClaimsSubmit = "claims.submit";
    public const string ClaimsRead = "claims.read";
    public const string Staff = "staff";
    public const string Admin = "admin";
    public const string ProviderAdmin = "provider-admin";
}

public static class AuthExtensions
{
    /// <summary>
    /// JWT bearer validation against the identity service. Tokens carry the public issuer (the gateway URL),
    /// while keys are fetched from the internal identity address.
    /// </summary>
    public static WebApplicationBuilder AddClaimBridgeAuth(this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection("Auth");
        var issuer = section["Issuer"] ?? "http://localhost:5000/";
        var authority = section["Authority"] ?? issuer;
        var audience = section["Audience"] ?? Scopes.Audience;

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.RequireHttpsMetadata = false;
                o.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    new Uri(new Uri(authority), ".well-known/openid-configuration").ToString(),
                    new InternalOidcConfigurationRetriever(authority),
                    new HttpDocumentRetriever { RequireHttps = false });
                o.TokenValidationParameters.ValidIssuer = issuer;
                o.TokenValidationParameters.ValidAudience = audience;
                o.TokenValidationParameters.NameClaimType = "name";
                o.TokenValidationParameters.RoleClaimType = ClaimTypesEx.Role;
                o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
            });

        builder.Services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.ClaimsSubmit, p => p.RequireAuthenticatedUser()
                .RequireAssertion(ctx => ctx.User.HasScope(Scopes.ClaimsSubmit)))
            .AddPolicy(Policies.ClaimsRead, p => p.RequireAuthenticatedUser()
                .RequireAssertion(ctx => ctx.User.HasScope(Scopes.ClaimsRead) || ctx.User.HasScope(Scopes.Api)))
            .AddPolicy(Policies.Staff, p => p.RequireRole(Roles.Admin, Roles.OpsAnalyst))
            .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin))
            .AddPolicy(Policies.ProviderAdmin, p => p.RequireRole(Roles.Admin, Roles.ProviderAdmin));

        return builder;
    }
}
