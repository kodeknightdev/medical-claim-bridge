namespace ClaimBridge.Claims.Lifecycle;

public sealed class LifecycleOptions
{
    /// <summary>How long each payer gets to acknowledge a submission before we send a status inquiry.</summary>
    public Dictionary<string, int> AckSlaMinutes { get; set; } = new() { ["PAYER-A"] = 5, ["PAYER-B"] = 15, ["PAYER-C"] = 5 };
    public int DefaultAckSlaMinutes { get; set; } = 10;
    public int InquiryWaitMinutes { get; set; } = 5;
    public int MaxResubmissions { get; set; } = 1;

    public TimeSpan AckSla(string payerId) =>
        TimeSpan.FromMinutes(AckSlaMinutes.TryGetValue(payerId, out var m) ? m : DefaultAckSlaMinutes);
}
