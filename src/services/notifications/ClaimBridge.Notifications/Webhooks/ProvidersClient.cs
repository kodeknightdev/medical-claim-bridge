using Microsoft.Extensions.Caching.Hybrid;

namespace ClaimBridge.Notifications.Webhooks;

public sealed record WebhookTarget(Guid Id, Guid OrgId, string Url, string[] Events, bool Active, string SigningSecret);

public sealed record OrgContact(Guid Id, string Name, string ContactEmail);

public class ProvidersClient(HttpClient http, HybridCache cache)
{
    public static string Key(Guid orgId) => $"notifications:webhooks:{orgId}";

    // signing secrets sit only in the in-process L1, never in Redis
    private static readonly HybridCacheEntryOptions SecretsOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(10),
        LocalCacheExpiration = TimeSpan.FromMinutes(10),
        Flags = HybridCacheEntryFlags.DisableDistributedCache
    };

    public async Task<IReadOnlyList<WebhookTarget>> SubscriptionsAsync(Guid orgId, CancellationToken ct) =>
        await cache.GetOrCreateAsync(Key(orgId), async token =>
                await http.GetFromJsonAsync<List<WebhookTarget>>($"internal/webhooks/by-org/{orgId}", token) ?? [],
            SecretsOptions, cancellationToken: ct);

    public ValueTask InvalidateAsync(Guid orgId, CancellationToken ct = default) => cache.RemoveAsync(Key(orgId), ct);

    public async Task<OrgContact?> OrgAsync(Guid orgId, CancellationToken ct) =>
        await cache.GetOrCreateAsync($"notifications:org:{orgId}", async token =>
            await http.GetFromJsonAsync<OrgContact>($"internal/orgs/{orgId}", token),
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromHours(1) }, cancellationToken: ct);

    public async Task DisableAsync(Guid subscriptionId, string reason, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync($"internal/webhooks/{subscriptionId}/disable", new { reason }, ct);
        response.EnsureSuccessStatusCode();
    }
}
