using System.Security.Cryptography;
using System.Text;

namespace ClaimBridge.Contracts.Webhooks;

/// <summary>
/// Signature header: <c>X-ClaimBridge-Signature: t=1726480000,v1=hex(hmac_sha256(secret, "{t}.{body}"))</c>.
/// The timestamp is signed too, so a captured delivery cannot be replayed outside the tolerance window.
/// </summary>
public static class WebhookSignature
{
    public const string HeaderName = "X-ClaimBridge-Signature";
    public const string EventHeader = "X-ClaimBridge-Event";
    public const string DeliveryHeader = "X-ClaimBridge-Delivery";
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    public static string Sign(string secret, long timestamp, string body)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return $"t={timestamp},v1={Convert.ToHexString(mac).ToLowerInvariant()}";
    }

    public static bool Verify(string secret, string? header, string body, DateTimeOffset now, TimeSpan? tolerance = null)
    {
        if (string.IsNullOrWhiteSpace(header)) return false;
        long? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(','))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0] == "t" && long.TryParse(kv[1], out var t)) timestamp = t;
            else if (kv[0] == "v1") signatures.Add(kv[1]);
        }
        if (timestamp is null || signatures.Count == 0) return false;

        var age = now - DateTimeOffset.FromUnixTimeSeconds(timestamp.Value);
        if (age.Duration() > (tolerance ?? DefaultTolerance)) return false;

        var expected = Encoding.UTF8.GetBytes(Sign(secret, timestamp.Value, body).Split("v1=")[1]);
        // during secret rotation more than one v1 may be present
        return signatures.Any(s => CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(s)));
    }
}
