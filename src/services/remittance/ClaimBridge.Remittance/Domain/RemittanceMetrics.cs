using System.Diagnostics.Metrics;

namespace ClaimBridge.Remittance.Domain;

public sealed class RemittanceMetrics
{
    private readonly Counter<long> _postings;
    private readonly Counter<double> _paid;
    private readonly Counter<long> _unmatched;

    public RemittanceMetrics(IMeterFactory meters)
    {
        var meter = meters.Create("ClaimBridge.Remittance");
        _postings = meter.CreateCounter<long>("remittance.postings", description: "Claim postings by kind");
        _paid = meter.CreateCounter<double>("remittance.paid.amount", "USD", "Amount posted as paid");
        _unmatched = meter.CreateCounter<long>("remittance.unmatched", description: "Lines sent to the reconciliation queue");
    }

    public void Posted(string payerId, PostingKind kind, decimal paid)
    {
        _postings.Add(1, new("payer", payerId), new("kind", kind.ToString()));
        _paid.Add((double)paid, new KeyValuePair<string, object?>("payer", payerId));
    }

    public void Unmatched(string payerId) => _unmatched.Add(1, new KeyValuePair<string, object?>("payer", payerId));
}
