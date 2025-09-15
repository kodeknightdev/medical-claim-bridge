using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimBridge.ServiceDefaults.Caching;

public static class CachingExtensions
{
    /// <summary>HybridCache with an in-process L1 and Redis as L2 when a Redis connection string is configured.</summary>
    public static IServiceCollection AddClaimBridgeCaching(this IServiceCollection services, IConfiguration config)
    {
        var redis = config.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
        {
            services.AddStackExchangeRedisCache(o =>
            {
                o.Configuration = redis;
                o.InstanceName = "cb:";
            });
            services.AddHealthChecks().AddRedis(redis, name: "redis", tags: [ServiceDefaultsExtensions.ReadyTag]);
        }

        services.AddHybridCache(o =>
        {
            o.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(10),
                LocalCacheExpiration = TimeSpan.FromMinutes(2)
            };
        });
        return services;
    }
}
