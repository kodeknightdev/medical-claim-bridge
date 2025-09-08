using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ClaimBridge.Identity.Data;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;
using CbScopes = ClaimBridge.Contracts.Reference.Scopes;

namespace ClaimBridge.Identity.Auth;

public sealed partial class ApiClientService(
    IOpenIddictApplicationManager applications,
    IdentityDb db,
    TimeProvider clock)
{
    public static readonly string[] AllowedScopes = [CbScopes.ClaimsSubmit, CbScopes.ClaimsRead];

    public sealed record CreatedClient(ApiClientProfile Profile, string ClientSecret);

    public async Task<CreatedClient> CreateAsync(Guid orgId, string displayName, IReadOnlyCollection<string> scopes,
        int rateLimitPerMinute, string actor, string? fixedClientId = null, string? fixedSecret = null, CancellationToken ct = default)
    {
        var invalid = scopes.Except(AllowedScopes).ToArray();
        if (invalid.Length > 0)
            throw new ArgumentException($"Scopes not allowed for API clients: {string.Join(", ", invalid)}");

        var clientId = fixedClientId ?? $"{Slug(displayName)}-{RandomNumberGenerator.GetHexString(6, lowercase: true)}";
        var secret = fixedSecret ?? NewSecret();

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = secret,
            ClientType = ClientTypes.Confidential,
            DisplayName = displayName,
            Permissions = { Permissions.Endpoints.Token, Permissions.GrantTypes.ClientCredentials }
        };
        foreach (var scope in scopes)
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);

        await applications.CreateAsync(descriptor, ct);

        var profile = new ApiClientProfile
        {
            ClientId = clientId,
            OrgId = orgId,
            DisplayName = displayName,
            Scopes = string.Join(' ', scopes),
            RateLimitPerMinute = rateLimitPerMinute,
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = actor
        };
        db.ApiClients.Add(profile);
        Audit(actor, orgId, "api-client.created", clientId, $"scopes={profile.Scopes}");
        await db.SaveChangesAsync(ct);
        return new CreatedClient(profile, secret);
    }

    public async Task<string> RotateSecretAsync(ApiClientProfile profile, string actor, CancellationToken ct = default)
    {
        var application = await applications.FindByClientIdAsync(profile.ClientId, ct)
            ?? throw new InvalidOperationException("OpenIddict application missing for " + profile.ClientId);
        var secret = NewSecret();
        await applications.UpdateAsync(application, secret, ct);

        profile.SecretRotatedAt = clock.GetUtcNow();
        Audit(actor, profile.OrgId, "api-client.secret-rotated", profile.ClientId, null);
        await db.SaveChangesAsync(ct);
        return secret;
    }

    public async Task RevokeAsync(ApiClientProfile profile, string actor, CancellationToken ct = default)
    {
        if (profile.RevokedAt is not null) return;
        var application = await applications.FindByClientIdAsync(profile.ClientId, ct);
        // swap in a throwaway secret so the old one is dead even if the profile check is bypassed
        if (application is not null)
            await applications.UpdateAsync(application, NewSecret(), ct);

        profile.RevokedAt = clock.GetUtcNow();
        Audit(actor, profile.OrgId, "api-client.revoked", profile.ClientId, null);
        await db.SaveChangesAsync(ct);
    }

    public Task<ApiClientProfile?> FindAsync(string clientId, CancellationToken ct = default) =>
        db.ApiClients.FirstOrDefaultAsync(c => c.ClientId == clientId, ct);

    private void Audit(string actor, Guid orgId, string action, string target, string? detail) =>
        db.AuditLog.Add(new AuditLogEntry
        {
            At = clock.GetUtcNow(), Actor = actor, ActorOrgId = orgId, Action = action, Target = target, Detail = detail
        });

    private static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Slug(string s)
    {
        var slug = NonAlnum().Replace(s.ToLowerInvariant(), "-").Trim('-');
        return slug.Length > 24 ? slug[..24].TrimEnd('-') : slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlnum();
}
