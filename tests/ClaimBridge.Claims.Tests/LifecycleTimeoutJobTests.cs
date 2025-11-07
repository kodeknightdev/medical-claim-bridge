using ClaimBridge.Claims.Application.Clients;
using ClaimBridge.Claims.Data;
using ClaimBridge.Claims.Domain;
using ClaimBridge.Claims.Lifecycle;
using ClaimBridge.Contracts.Events;
using ClaimBridge.Claims.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Quartz;

namespace ClaimBridge.Claims.Tests;

public class LifecycleTimeoutJobTests
{
    private readonly TestHarness _h = new();

    private async Task RunJobAsync()
    {
        var job = new LifecycleTimeoutJob(_h.Db, _h.Transitions, new StubInquiry(), _h.Outbox, Options.Create(new LifecycleOptions()),
            _h.Clock, NullLogger<LifecycleTimeoutJob>.Instance);
        var context = Substitute.For<IJobExecutionContext>();
        await job.Execute(context);
    }

    [Fact]
    public async Task Ack_timeout_inquires_then_resubmits_once_then_escalates()
    {
        var claim = await _h.AddClaimAsync(ClaimStatus.Submitted);
        var state = await _h.Db.Lifecycles.SingleAsync();
        state.SetDeadline(DeadlineKind.Acknowledgement, _h.Clock.GetUtcNow(), _h.Clock.GetUtcNow());
        await _h.Db.SaveChangesAsync();

        await RunJobAsync();
        state.DeadlineKind.ShouldBe(DeadlineKind.StatusInquiry);
        state.InquiriesSent.ShouldBe(1);

        _h.Clock.Advance(TimeSpan.FromMinutes(6));
        await RunJobAsync();
        state.DeadlineKind.ShouldBe(DeadlineKind.Resubmission);
        _h.Outbox.Of<ClaimReady>().Single().Attempt.ShouldBe(2);

        _h.Clock.Advance(TimeSpan.FromMinutes(6));
        await RunJobAsync();
        var escalated = await _h.Db.Claims.SingleAsync(c => c.Id == claim.Id);
        escalated.Status.ShouldBe(ClaimStatus.Escalated);
        state.EscalatedAt.ShouldNotBeNull();
        state.DeadlineAt.ShouldBeNull();
    }

    [Fact]
    public async Task Deadlines_in_the_future_are_left_alone()
    {
        await _h.AddClaimAsync(ClaimStatus.Submitted);
        var state = await _h.Db.Lifecycles.SingleAsync();
        state.SetDeadline(DeadlineKind.Acknowledgement, _h.Clock.GetUtcNow().AddMinutes(3), _h.Clock.GetUtcNow());
        await _h.Db.SaveChangesAsync();

        await RunJobAsync();
        state.InquiriesSent.ShouldBe(0);
    }

    private sealed class StubInquiry() : StatusInquiryClient(new HttpClient(), NullLogger<StatusInquiryClient>.Instance)
    {
        public override Task<bool> RequestAsync(Guid claimId, string claimNumber, string payerId, CancellationToken ct) => Task.FromResult(true);
    }
}
