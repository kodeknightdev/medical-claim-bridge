using ClaimBridge.Contracts.Submission;

namespace ClaimBridge.PayersGateway.Adapters;

public enum SubmitOutcome { Accepted, Rejected, Queued }

/// <summary>Neutral result of handing a claim to a payer. Payer-specific codes are translated before this point.</summary>
public sealed record SubmitResult(SubmitOutcome Outcome, string? PayerClaimId = null, IReadOnlyList<string>? Rejections = null)
{
    public static SubmitResult Queued() => new(SubmitOutcome.Queued);
}

public sealed record EligibilityQuery(string MemberId, DateOnly DateOfBirth, DateOnly DateOfService);

public sealed record EligibilityAnswer(
    string Status,
    DateOnly? CoverageStart,
    DateOnly? CoverageEnd,
    string? PlanName,
    decimal? Copay,
    decimal? DeductibleRemaining,
    bool FromCache = false);

public sealed record StatusQuery(string ClaimNumber, string? PayerClaimId);

/// <summary>Normalised 277-style answer: received, rejected, pended, in-process, finalized or not-found.</summary>
public sealed record StatusAnswer(string Status, string? Outcome, string? StatusCode, string? Description, string? PayerClaimId);

public sealed class PayerUnavailableException(string payerId, string reason, Exception? inner = null)
    : Exception($"{payerId} unavailable: {reason}", inner)
{
    public string PayerId { get; } = payerId;
}

/// <summary>Anti-corruption layer: one adapter per payer; nothing payer-specific escapes these classes.</summary>
public interface IPayerAdapter
{
    string PayerId { get; }
    string Channel { get; }
    Task<SubmitResult> SubmitAsync(SubmissionDocument claim, int attempt, CancellationToken ct);
    Task<EligibilityAnswer> CheckEligibilityAsync(EligibilityQuery query, CancellationToken ct);
    Task<StatusAnswer> InquireStatusAsync(StatusQuery query, CancellationToken ct);
}

public class PayerAdapterRegistry(IEnumerable<IPayerAdapter> adapters)
{
    private readonly Dictionary<string, IPayerAdapter> _byPayer = adapters.ToDictionary(a => a.PayerId);

    public IPayerAdapter For(string payerId) =>
        _byPayer.TryGetValue(payerId, out var a) ? a : throw new KeyNotFoundException($"No adapter for payer {payerId}");

    public IEnumerable<IPayerAdapter> All => _byPayer.Values;
}
