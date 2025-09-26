using Microsoft.Extensions.Caching.Hybrid;

namespace ClaimBridge.Claims.Application.Clients;

public class ProvidersClient(HttpClient http, HybridCache cache)
{
    private sealed record EnrollmentCheck(Guid OrgId, string PayerId, bool Active, string? Status);

    public static string EnrollmentKey(Guid orgId, string payerId) => $"claims:enrollment:{orgId}:{payerId}";

    public async Task<bool> IsEnrolledAsync(Guid orgId, string payerId, CancellationToken ct) =>
        await cache.GetOrCreateAsync(EnrollmentKey(orgId, payerId), async token =>
            {
                var check = await http.GetFromJsonAsync<EnrollmentCheck>(
                    $"internal/enrollments/check?orgId={orgId}&payerId={Uri.EscapeDataString(payerId)}", token);
                return check?.Active ?? false;
            },
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(5), LocalCacheExpiration = TimeSpan.FromMinutes(1) },
            cancellationToken: ct);

    public sealed record OrgInfo(Guid Id, string Name, string GroupNpi, string TaxId, string AddressLine1, string City,
        string State, string Zip, string ContactEmail);

    public async Task<OrgInfo> GetOrgAsync(Guid orgId, CancellationToken ct) =>
        await cache.GetOrCreateAsync($"claims:org:{orgId}", async token =>
                await http.GetFromJsonAsync<OrgInfo>($"internal/orgs/{orgId}", token)
                ?? throw new InvalidOperationException($"Organisation {orgId} not found"),
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(30) }, cancellationToken: ct);

    public ValueTask InvalidateAsync(Guid orgId, string payerId, CancellationToken ct = default) =>
        cache.RemoveAsync(EnrollmentKey(orgId, payerId), ct);
}
