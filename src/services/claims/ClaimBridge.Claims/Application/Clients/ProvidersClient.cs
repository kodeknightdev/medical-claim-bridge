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

    public ValueTask InvalidateAsync(Guid orgId, string payerId, CancellationToken ct = default) =>
        cache.RemoveAsync(EnrollmentKey(orgId, payerId), ct);
}
