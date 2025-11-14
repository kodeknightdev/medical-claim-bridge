using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using ClaimBridge.Contracts.Reference;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ClaimBridge.Testing;

/// <summary>Real signed JWTs for integration tests, validated by the services' own JwtBearer setup.</summary>
public static class TestTokens
{
    public const string Issuer = "http://localhost:5080/";
    private static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "test-key" };

    public static string ProviderUser(Guid orgId, string role = Roles.ProviderBiller, string scope = "api claims.read claims.submit") =>
        Create("user-" + orgId.ToString()[..8], scope, ("role", role), (ClaimTypesEx.OrgId, orgId.ToString()), ("client_id", "claim-bridge-portal"));

    public static string Staff(string role = Roles.OpsAnalyst) =>
        Create("staff-user", "api claims.read claims.submit", ("role", role), ("client_id", "claim-bridge-portal"));

    public static string ApiClient(Guid orgId, string scope, string clientId = "sunrise-pms", int rateLimit = 120) =>
        Create(clientId, scope, (ClaimTypesEx.OrgId, orgId.ToString()), ("client_id", clientId), (ClaimTypesEx.RateLimit, rateLimit.ToString()));

    public static string Create(string subject, string scope, params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity([new Claim("sub", subject), new Claim("scope", scope), new Claim("name", subject)]);
        foreach (var (type, value) in claims) identity.AddClaim(new Claim(type, value));
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        handler.OutboundClaimTypeMap.Clear();
        return handler.CreateEncodedJwt(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Scopes.Audience,
            Subject = identity,
            Expires = DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256)
        });
    }

    /// <summary>Points the service's JwtBearer handler at the test key instead of the identity service.</summary>
    public static IServiceCollection UseTestTokens(this IServiceCollection services) =>
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
        {
            o.ConfigurationManager = null;
            o.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            o.TokenValidationParameters.IssuerSigningKey = Key;
            o.TokenValidationParameters.ValidIssuer = Issuer;
        });
}
