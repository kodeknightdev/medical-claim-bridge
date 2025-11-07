using ClaimBridge.Claims.Data;
using ClaimBridge.Claims.Denials;
using ClaimBridge.Claims.Domain;
using ClaimBridge.Claims.Lifecycle;
using ClaimBridge.Contracts.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClaimBridge.Claims.Tests;

public class ClaimProcessManagerTests
{
    private readonly TestHarness _h = new();

    private ClaimProcessManager Manager() => new(_h.Db, _h.Transitions, _h.Outbox, Options.Create(new LifecycleOptions()),
        new DenialWorklist(_h.Db, Options.Create(new AppealOptions()), _h.Clock), _h.Clock, NullLogger<ClaimProcessManager>.Instance);

    private async Task<Claim> Handle<T>(Claim claim, T e) where T : IIntegrationEvent
    {
        await Manager().HandleAsync(EventEnvelope.Create(e, "test"), "topic", default);
        await _h.Db.SaveChangesAsync();
        return await _h.Db.Claims.Include(c => c.History).SingleAsync(c => c.Id == claim.Id);
    }

    private static PaymentPosted Payment(Claim c, decimal paid, decimal pr, bool partial = false, decimal? co = null) => new(
        c.Id, c.ClaimNumber, c.OrgId, c.PayerId, Guid.NewGuid(), c.TotalCharge, paid, paid + pr, pr,
        [new AdjustmentSummary("CO", partial ? "97" : "45", co ?? c.TotalCharge - paid - pr), new AdjustmentSummary("PR", "2", pr)],
        partial, "EFT1", new DateOnly(2025, 11, 10));

    [Fact]
    public async Task Passed_scrub_moves_to_ready_and_hands_off_to_payers_gateway()
    {
        var claim = await _h.AddClaimAsync(ClaimStatus.Scrubbing);
        claim = await Handle(claim, new ClaimScrubbed(claim.Id, claim.ClaimNumber, claim.OrgId, claim.PayerId, true, [], 0.1m, true));

        claim.Status.ShouldBe(ClaimStatus.ReadyToSubmit);
        _h.Outbox.Of<ClaimReady>().Single().Attempt.ShouldBe(1);
        _h.Outbox.Of<ClaimStatusChanged>().Last().ToStatus.ShouldBe("ReadyToSubmit");
    }

    [Fact]
    public async Task Failed_scrub_lists_the_error_rules()
    {
        var claim = await _h.AddClaimAsync(ClaimStatus.Scrubbing);
        claim = await Handle(claim, new ClaimScrubbed(claim.Id, claim.ClaimNumber, claim.OrgId, claim.PayerId, false,
            [new ScrubEditSummary("CB004", "Error", 1), new ScrubEditSummary("CB010", "Warning", null)], null, true));

        claim.Status.ShouldBe(ClaimStatus.ScrubFailed);
        claim.History.Last().Reason.ShouldBe("Scrub errors: CB004");
        _h.Outbox.Of<ClaimReady>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Submission_starts_the_ack_deadline_and_ack_clears_it()
    {
        var claim = await _h.AddClaimAsync(ClaimStatus.ReadyToSubmit);
        claim = await Handle(claim, new ClaimSubmitted(claim.Id, claim.ClaimNumber, claim.PayerId, Guid.NewGuid(), 1, "json-api", _h.Clock.GetUtcNow(), null));
        var state = await _h.Db.Lifecycles.SingleAsync(l => l.ClaimId == claim.Id);
        state.DeadlineKind.ShouldBe(DeadlineKind.Acknowledgement);
        state.DeadlineAt.ShouldBe(_h.Clock.GetUtcNow().AddMinutes(5));

        claim = await Handle(claim, new AckReceived(claim.Id, claim.ClaimNumber, claim.PayerId, true, "NW1", [], "sync-ack", _h.Clock.GetUtcNow()));
        claim.Status.ShouldBe(ClaimStatus.AcceptedByPayer);
        claim.PayerClaimId.ShouldBe("NW1");
        (await _h.Db.Lifecycles.SingleAsync(l => l.ClaimId == claim.Id)).DeadlineAt.ShouldBeNull();
    }

    [Fact]
    public async Task Ack_arriving_before_the_submitted_event_catches_the_claim_up()
    {
        var claim = await _h.AddClaimAsync(ClaimStatus.ReadyToSubmit);
        claim = await Handle(claim, new AckReceived(claim.Id, claim.ClaimNumber, claim.PayerId, true, "BPK1", [], "277CA", _h.Clock.GetUtcNow()));

        claim.Status.ShouldBe(ClaimStatus.AcceptedByPayer);
        claim.History.Select(h => h.ToStatus).ShouldBe([ClaimStatus.Submitted, ClaimStatus.AcceptedByPayer]);
    }

    [Fact]
    public async Task Full_payment_posts_and_closes()
    {
        var claim = await _h.AddClaimAsync(ClaimStatus.AcceptedByPayer);
        claim = await Handle(claim, Payment(claim, paid: 200, pr: 40));

        claim.Status.ShouldBe(ClaimStatus.Closed);
        claim.History.Select(h => h.ToStatus).ShouldBe([ClaimStatus.Paid, ClaimStatus.Posted, ClaimStatus.Closed]);
        (claim.PaidAmount + claim.AdjustmentAmount + claim.PatientResponsibility).ShouldBe(claim.TotalCharge);
    }

    [Fact]
    public async Task Partial_payment_stays_posted_and_opens_a_worklist_item()
    {
        var claim = await _h.AddClaimAsync(ClaimStatus.AcceptedByPayer);
        claim = await Handle(claim, Payment(claim, paid: 150, pr: 30, partial: true));

        claim.Status.ShouldBe(ClaimStatus.Posted);
        (await _h.Db.DenialWorkItems.SingleAsync()).CarcCode.ShouldBe("97");
    }

    [Fact]
    public async Task Denial_opens_the_worklist_with_an_appeal_deadline()
    {
        var claim = await _h.AddClaimAsync(ClaimStatus.Submitted, payer: "PAYER-C");
        claim = await Handle(claim, new ClaimDenied(claim.Id, claim.ClaimNumber, claim.OrgId, claim.PayerId, Guid.NewGuid(), claim.TotalCharge,
            "197", "Authorization", ["N54"], null));

        claim.Status.ShouldBe(ClaimStatus.Denied);
        claim.DenialCarc.ShouldBe("197");
        var item = await _h.Db.DenialWorkItems.SingleAsync();
        item.AppealDeadline.ShouldBe(new DateOnly(2025, 11, 10).AddDays(60));
    }

    [Fact]
    public async Task Takeback_reopens_a_closed_claim()
    {
        var claim = await _h.AddClaimAsync(ClaimStatus.Closed);
        claim.PaidAmount = 200;
        await _h.Db.SaveChangesAsync();

        claim = await Handle(claim, new ReversalPosted(claim.Id, claim.ClaimNumber, claim.OrgId, claim.PayerId, Guid.NewGuid(), 200, "EFT2"));
        claim.Status.ShouldBe(ClaimStatus.AcceptedByPayer);
        claim.PaidAmount.ShouldBe(0);
    }

    [Fact]
    public async Task Reversal_on_a_superseded_claim_is_recorded_without_reopening_it()
    {
        var claim = await _h.AddClaimAsync(ClaimStatus.Superseded);
        claim.PaidAmount = 120;
        await _h.Db.SaveChangesAsync();

        claim = await Handle(claim, new ReversalPosted(claim.Id, claim.ClaimNumber, claim.OrgId, claim.PayerId, Guid.NewGuid(), 120, null));
        claim.Status.ShouldBe(ClaimStatus.Superseded);
        claim.History.Last().Trigger.ShouldBe("Reversal");
    }

    [Fact]
    public async Task Late_payer_events_after_a_void_are_noted_and_ignored()
    {
        var claim = await _h.AddClaimAsync(ClaimStatus.Voided);
        claim = await Handle(claim, new AckReceived(claim.Id, claim.ClaimNumber, claim.PayerId, true, "X", [], "sync-ack", _h.Clock.GetUtcNow()));
        claim.Status.ShouldBe(ClaimStatus.Voided);
        claim.History.Last().Trigger.ShouldBe("IgnoredLateEvent");
    }

    [Fact]
    public async Task Payment_after_a_won_appeal_moves_through_appeal_won()
    {
        var claim = await _h.AddClaimAsync(ClaimStatus.Appealed);
        claim = await Handle(claim, Payment(claim, paid: 250, pr: 0));
        claim.History.Select(h => h.Trigger).ShouldContain("AppealWon");
        claim.Status.ShouldBe(ClaimStatus.Closed);
    }
}
