using System.Globalization;
using System.Threading.RateLimiting;
using ClaimBridge.Contracts.Reference;
using Microsoft.AspNetCore.RateLimiting;

namespace ClaimBridge.Gateway.RateLimiting;

public static class PerClientRateLimit
{
    public const string PolicyName = "per-client";

    /// <summary>
    /// One fixed window per caller. API clients carry their own limit in the token (cb_rate_limit claim, set per client
    /// in identity); portal users share a generous default; anonymous calls are limited per IP.
    /// </summary>
    public static IServiceCollection AddPerClientRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        var userDefault = config.GetValue("RateLimiting:UserPerMinute", 600);
        var anonymous = config.GetValue("RateLimiting:AnonymousPerMinute", 120);

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                await TypedResults.Problem(title: "Too many requests", statusCode: 429,
                        detail: "This client exceeded its request quota. Retry after the time in the Retry-After header.")
                    .ExecuteAsync(ctx.HttpContext);
            };
            o.AddPolicy(PolicyName, http => Partition(http, userDefault, anonymous));
        });
        return services;
    }

    public static RateLimitPartition<string> Partition(HttpContext http, int userDefault, int anonymous)
    {
        var user = http.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return Window($"anon:{ip}", anonymous);
        }

        var subject = user.FindFirst("sub")?.Value ?? "unknown";
        var limit = int.TryParse(user.FindFirst(ClaimTypesEx.RateLimit)?.Value, out var l) && l > 0 ? l : userDefault;
        // the limit is part of the key so a client whose quota changes gets a fresh window with the new size
        return Window($"{subject}:{limit}", limit);
    }

    private static RateLimitPartition<string> Window(string key, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
}
