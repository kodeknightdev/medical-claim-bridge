namespace ClaimBridge.Notifications.Webhooks;

public sealed class WebhookOptions
{
    /// <summary>Delay before attempt n+1 after n failures. The schedule length is the attempt budget.</summary>
    public int[] BackoffSeconds { get; set; } = [10, 30, 120, 600, 1800, 3600, 10800, 21600];
    public int DisableAfterConsecutiveFailures { get; set; } = 15;
    public int TimeoutSeconds { get; set; } = 10;
    public int DispatchIntervalMilliseconds { get; set; } = 2000;
}

public static class WebhookBackoff
{
    public static int MaxAttempts(WebhookOptions o) => o.BackoffSeconds.Length + 1;

    /// <summary>Exponential-ish schedule with up to 10% jitter so a recovering receiver isn't hit by a thundering herd.</summary>
    public static TimeSpan NextDelay(int failedAttempts, WebhookOptions o, Random? random = null)
    {
        var index = Math.Clamp(failedAttempts - 1, 0, o.BackoffSeconds.Length - 1);
        var baseSeconds = o.BackoffSeconds[index];
        var jitter = (random ?? Random.Shared).NextDouble() * 0.1 * baseSeconds;
        return TimeSpan.FromSeconds(baseSeconds + jitter);
    }

    public static bool ShouldDisable(int consecutiveFailures, WebhookOptions o) => consecutiveFailures >= o.DisableAfterConsecutiveFailures;
}
