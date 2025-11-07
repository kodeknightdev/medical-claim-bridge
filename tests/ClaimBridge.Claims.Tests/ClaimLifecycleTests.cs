using ClaimBridge.Claims.Domain;

namespace ClaimBridge.Claims.Tests;

public class ClaimLifecycleTests
{
    public static TheoryData<ClaimStatus, LifecycleTrigger, ClaimStatus> Allowed => new()
    {
        { ClaimStatus.Received, LifecycleTrigger.StartScrub, ClaimStatus.Scrubbing },
        { ClaimStatus.Scrubbing, LifecycleTrigger.ScrubPassed, ClaimStatus.ReadyToSubmit },
        { ClaimStatus.Scrubbing, LifecycleTrigger.ScrubFailed, ClaimStatus.ScrubFailed },
        { ClaimStatus.ScrubFailed, LifecycleTrigger.Edited, ClaimStatus.Received },
        { ClaimStatus.ReadyToSubmit, LifecycleTrigger.Submit, ClaimStatus.Submitted },
        { ClaimStatus.Submitted, LifecycleTrigger.Submit, ClaimStatus.Submitted },
        { ClaimStatus.Submitted, LifecycleTrigger.PayerAccepted, ClaimStatus.AcceptedByPayer },
        { ClaimStatus.Submitted, LifecycleTrigger.PayerRejected, ClaimStatus.RejectedByPayer },
        { ClaimStatus.Submitted, LifecycleTrigger.PayerPended, ClaimStatus.Pended },
        { ClaimStatus.Submitted, LifecycleTrigger.Escalate, ClaimStatus.Escalated },
        { ClaimStatus.Escalated, LifecycleTrigger.Submit, ClaimStatus.Submitted },
        { ClaimStatus.Escalated, LifecycleTrigger.PayerAccepted, ClaimStatus.AcceptedByPayer },
        { ClaimStatus.AcceptedByPayer, LifecycleTrigger.PayerPended, ClaimStatus.Pended },
        { ClaimStatus.AcceptedByPayer, LifecycleTrigger.AdjudicatedPaid, ClaimStatus.Paid },
        { ClaimStatus.Pended, LifecycleTrigger.AdjudicatedPartial, ClaimStatus.PartiallyPaid },
        { ClaimStatus.Pended, LifecycleTrigger.AdjudicatedDenied, ClaimStatus.Denied },
        { ClaimStatus.Paid, LifecycleTrigger.RemittancePosted, ClaimStatus.Posted },
        { ClaimStatus.PartiallyPaid, LifecycleTrigger.RemittancePosted, ClaimStatus.Posted },
        { ClaimStatus.Denied, LifecycleTrigger.RemittancePosted, ClaimStatus.Posted },
        { ClaimStatus.Posted, LifecycleTrigger.Close, ClaimStatus.Closed },
        { ClaimStatus.Denied, LifecycleTrigger.Close, ClaimStatus.Closed },
        { ClaimStatus.Denied, LifecycleTrigger.Appeal, ClaimStatus.Appealed },
        { ClaimStatus.Appealed, LifecycleTrigger.AppealWon, ClaimStatus.AcceptedByPayer },
        { ClaimStatus.Appealed, LifecycleTrigger.AppealLost, ClaimStatus.Closed },
        { ClaimStatus.Closed, LifecycleTrigger.ReversalPosted, ClaimStatus.AcceptedByPayer },
        { ClaimStatus.Posted, LifecycleTrigger.ReversalPosted, ClaimStatus.AcceptedByPayer },
        { ClaimStatus.Denied, LifecycleTrigger.Supersede, ClaimStatus.Superseded },
        { ClaimStatus.RejectedByPayer, LifecycleTrigger.Supersede, ClaimStatus.Superseded },
        { ClaimStatus.Closed, LifecycleTrigger.Void, ClaimStatus.Voided },
    };

    [Theory]
    [MemberData(nameof(Allowed))]
    public void Allowed_transitions(ClaimStatus from, LifecycleTrigger trigger, ClaimStatus to)
    {
        ClaimLifecycle.CanFire(from, trigger).ShouldBeTrue();
        ClaimLifecycle.Next(from, trigger).ShouldBe(to);
    }

    [Theory]
    [InlineData(ClaimStatus.Received, LifecycleTrigger.Submit)]
    [InlineData(ClaimStatus.ScrubFailed, LifecycleTrigger.Submit)]
    [InlineData(ClaimStatus.Submitted, LifecycleTrigger.AdjudicatedPaid)]
    [InlineData(ClaimStatus.Paid, LifecycleTrigger.Appeal)]
    [InlineData(ClaimStatus.Scrubbing, LifecycleTrigger.Supersede)]
    [InlineData(ClaimStatus.Submitted, LifecycleTrigger.Supersede)]
    public void Disallowed_transitions_throw(ClaimStatus from, LifecycleTrigger trigger)
    {
        ClaimLifecycle.CanFire(from, trigger).ShouldBeFalse();
        var ex = Should.Throw<InvalidClaimTransitionException>(() => ClaimLifecycle.Next(from, trigger));
        ex.From.ShouldBe(from);
    }

    [Fact]
    public void Terminal_states_have_no_way_out()
    {
        foreach (var terminal in ClaimLifecycle.Terminal)
            ClaimLifecycle.All.ShouldNotContain(t => t.From == terminal);
    }

    [Fact]
    public void Every_status_is_reachable_from_received()
    {
        var reached = new HashSet<ClaimStatus> { ClaimStatus.Received };
        var frontier = new Queue<ClaimStatus>(reached);
        while (frontier.TryDequeue(out var s))
            foreach (var t in ClaimLifecycle.All.Where(t => t.From == s))
                if (reached.Add(t.To)) frontier.Enqueue(t.To);
        reached.ShouldBe(Enum.GetValues<ClaimStatus>(), ignoreOrder: true);
    }

    [Fact]
    public void Firing_appends_immutable_history_with_increasing_sequence()
    {
        var claim = new Claim { Id = Guid.NewGuid(), Status = ClaimStatus.Received };
        var t = DateTimeOffset.UnixEpoch;
        claim.Note("Created", t, "test");
        claim.Fire(LifecycleTrigger.StartScrub, t.AddSeconds(1), "test");
        claim.Fire(LifecycleTrigger.ScrubPassed, t.AddSeconds(2), "test", "Clean claim");

        claim.Status.ShouldBe(ClaimStatus.ReadyToSubmit);
        claim.History.Select(h => h.Sequence).ShouldBe([1, 2, 3]);
        claim.History[2].ShouldSatisfyAllConditions(h => h.FromStatus.ShouldBe(ClaimStatus.Scrubbing),
            h => h.ToStatus.ShouldBe(ClaimStatus.ReadyToSubmit), h => h.Reason.ShouldBe("Clean claim"));
    }
}
