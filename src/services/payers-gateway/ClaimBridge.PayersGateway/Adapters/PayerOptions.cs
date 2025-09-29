namespace ClaimBridge.PayersGateway.Adapters;

/// <summary>Per-payer connection settings, bound from Payers:{PayerId}.</summary>
public sealed class PayerOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5109/";
    public string SubmitterId { get; set; } = "CLAIMBRIDGE";
    public string CallbackUrl { get; set; } = "";
    public string CallbackToken { get; set; } = "";
    public string FileContainer { get; set; } = "";
    public int RequestsPerSecond { get; set; } = 20;
    public int MaxConcurrency { get; set; } = 4;
    public int AttemptTimeoutSeconds { get; set; } = 3;
    public int RetryCount { get; set; } = 2;
    public int BreakSeconds { get; set; } = 20;
}
