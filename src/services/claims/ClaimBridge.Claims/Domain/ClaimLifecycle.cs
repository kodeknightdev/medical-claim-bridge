namespace ClaimBridge.Claims.Domain;

public sealed class InvalidClaimTransitionException(ClaimStatus from, LifecycleTrigger trigger)
    : InvalidOperationException($"Trigger {trigger} is not allowed while the claim is {from}.")
{
    public ClaimStatus From { get; } = from;
    public LifecycleTrigger Trigger { get; } = trigger;
}

/// <summary>
/// The claim lifecycle state machine. Pure and table driven so every transition can be tested on its own;
/// the process manager decides when to fire triggers.
/// </summary>
public static class ClaimLifecycle
{
    private static readonly Dictionary<(ClaimStatus, LifecycleTrigger), ClaimStatus> Transitions = Build();

    public static readonly IReadOnlySet<ClaimStatus> Terminal = new HashSet<ClaimStatus> { ClaimStatus.Superseded, ClaimStatus.Voided };

    /// <summary>States where the payer has the claim and an acknowledgement or decision is outstanding.</summary>
    public static readonly IReadOnlySet<ClaimStatus> AwaitingPayer = new HashSet<ClaimStatus>
    {
        ClaimStatus.Submitted, ClaimStatus.Escalated, ClaimStatus.AcceptedByPayer, ClaimStatus.Pended
    };

    public static readonly IReadOnlySet<ClaimStatus> Adjudicated = new HashSet<ClaimStatus>
    {
        ClaimStatus.Paid, ClaimStatus.PartiallyPaid, ClaimStatus.Denied
    };

    public static bool CanFire(ClaimStatus from, LifecycleTrigger trigger) => Transitions.ContainsKey((from, trigger));

    public static ClaimStatus Next(ClaimStatus from, LifecycleTrigger trigger) =>
        Transitions.TryGetValue((from, trigger), out var to) ? to : throw new InvalidClaimTransitionException(from, trigger);

    public static IEnumerable<(ClaimStatus From, LifecycleTrigger Trigger, ClaimStatus To)> All =>
        Transitions.Select(t => (t.Key.Item1, t.Key.Item2, t.Value));

    private static Dictionary<(ClaimStatus, LifecycleTrigger), ClaimStatus> Build()
    {
        var t = new Dictionary<(ClaimStatus, LifecycleTrigger), ClaimStatus>();
        void Add(ClaimStatus from, LifecycleTrigger trigger, ClaimStatus to) => t[(from, trigger)] = to;
        void AddMany(IEnumerable<ClaimStatus> from, LifecycleTrigger trigger, ClaimStatus to)
        {
            foreach (var f in from) Add(f, trigger, to);
        }

        Add(ClaimStatus.Received, LifecycleTrigger.StartScrub, ClaimStatus.Scrubbing);
        Add(ClaimStatus.Scrubbing, LifecycleTrigger.ScrubPassed, ClaimStatus.ReadyToSubmit);
        Add(ClaimStatus.Scrubbing, LifecycleTrigger.ScrubFailed, ClaimStatus.ScrubFailed);
        Add(ClaimStatus.ScrubFailed, LifecycleTrigger.Edited, ClaimStatus.Received);
        Add(ClaimStatus.Received, LifecycleTrigger.Edited, ClaimStatus.Received);

        Add(ClaimStatus.ReadyToSubmit, LifecycleTrigger.Submit, ClaimStatus.Submitted);
        // resubmission after an ack timeout, or ops pushing an escalated claim again
        Add(ClaimStatus.Submitted, LifecycleTrigger.Submit, ClaimStatus.Submitted);
        Add(ClaimStatus.Escalated, LifecycleTrigger.Submit, ClaimStatus.Submitted);
        Add(ClaimStatus.Submitted, LifecycleTrigger.Escalate, ClaimStatus.Escalated);

        AddMany([ClaimStatus.Submitted, ClaimStatus.Escalated], LifecycleTrigger.PayerAccepted, ClaimStatus.AcceptedByPayer);
        AddMany([ClaimStatus.Submitted, ClaimStatus.Escalated], LifecycleTrigger.PayerRejected, ClaimStatus.RejectedByPayer);
        AddMany([ClaimStatus.Submitted, ClaimStatus.Escalated, ClaimStatus.AcceptedByPayer], LifecycleTrigger.PayerPended, ClaimStatus.Pended);

        var decidable = new[] { ClaimStatus.AcceptedByPayer, ClaimStatus.Pended };
        AddMany(decidable, LifecycleTrigger.AdjudicatedPaid, ClaimStatus.Paid);
        AddMany(decidable, LifecycleTrigger.AdjudicatedPartial, ClaimStatus.PartiallyPaid);
        AddMany(decidable, LifecycleTrigger.AdjudicatedDenied, ClaimStatus.Denied);

        AddMany([ClaimStatus.Paid, ClaimStatus.PartiallyPaid, ClaimStatus.Denied], LifecycleTrigger.RemittancePosted, ClaimStatus.Posted);
        AddMany([ClaimStatus.Posted, ClaimStatus.Denied], LifecycleTrigger.Close, ClaimStatus.Closed);

        Add(ClaimStatus.Denied, LifecycleTrigger.Appeal, ClaimStatus.Appealed);
        Add(ClaimStatus.Posted, LifecycleTrigger.Appeal, ClaimStatus.Appealed);
        Add(ClaimStatus.Appealed, LifecycleTrigger.AppealWon, ClaimStatus.AcceptedByPayer);
        Add(ClaimStatus.Appealed, LifecycleTrigger.AppealLost, ClaimStatus.Closed);

        // a takeback reopens the claim and waits for the payer to decide again
        AddMany([ClaimStatus.Paid, ClaimStatus.PartiallyPaid, ClaimStatus.Posted, ClaimStatus.Closed], LifecycleTrigger.ReversalPosted,
            ClaimStatus.AcceptedByPayer);

        AddMany([ClaimStatus.ScrubFailed, ClaimStatus.RejectedByPayer, ClaimStatus.Denied, ClaimStatus.Posted, ClaimStatus.Closed,
            ClaimStatus.PartiallyPaid, ClaimStatus.Paid, ClaimStatus.Appealed], LifecycleTrigger.Supersede, ClaimStatus.Superseded);
        AddMany([ClaimStatus.Received, ClaimStatus.ScrubFailed, ClaimStatus.ReadyToSubmit, ClaimStatus.RejectedByPayer,
            ClaimStatus.AcceptedByPayer, ClaimStatus.Pended, ClaimStatus.Paid, ClaimStatus.PartiallyPaid, ClaimStatus.Denied,
            ClaimStatus.Posted, ClaimStatus.Closed], LifecycleTrigger.Void, ClaimStatus.Voided);

        return t;
    }
}
