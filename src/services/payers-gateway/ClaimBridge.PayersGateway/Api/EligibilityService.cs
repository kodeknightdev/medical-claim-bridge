using System.Diagnostics.Metrics;
using ClaimBridge.PayersGateway.Adapters;
using ClaimBridge.ServiceDefaults.Phi;
using Microsoft.Extensions.Caching.Hybrid;

namespace ClaimBridge.PayersGateway.Api;

public sealed record EligibilityRequest(string PayerId, string MemberId, DateOnly DateOfBirth, DateOnly DateOfService);

public class EligibilityService(PayerAdapterRegistry adapters, HybridCache cache, IPhiProtector phi, ILogger<EligibilityService> logger)
{
    public async Task<EligibilityAnswer> CheckAsync(EligibilityRequest request, CancellationToken ct)
    {
        // the cache key is a keyed hash so member ids never show up in Redis
        var key = $"payers:elig:{request.PayerId}:{phi.Hash(request.MemberId + "|" + request.DateOfBirth)}:{request.DateOfService:yyyyMMdd}";
        var fromCache = true;
        var answer = await cache.GetOrCreateAsync(key, async token =>
        {
            fromCache = false;
            try
            {
                return await adapters.For(request.PayerId).CheckEligibilityAsync(
                    new EligibilityQuery(request.MemberId.Trim().ToUpperInvariant(), request.DateOfBirth, request.DateOfService), token);
            }
            catch (PayerUnavailableException ex)
            {
                logger.LogWarning("Eligibility for {PayerId} unavailable: {Error}", request.PayerId, ex.Message);
                return new EligibilityAnswer("Unknown", null, null, null, null, null);
            }
        }, new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(5), LocalCacheExpiration = TimeSpan.FromMinutes(1) },
        cancellationToken: ct);

        if (answer.Status == "Unknown" && !fromCache)
            await cache.RemoveAsync(key, ct);
        return answer with { FromCache = fromCache };
    }
}
