using ClaimBridge.Claims.Data;
using ClaimBridge.Claims.Domain;
using ClaimBridge.Claims.Lifecycle;

namespace ClaimBridge.Claims.Tests;

public class LifecycleTimeoutPolicyTests
{
    private static ClaimLifecycleState State(DeadlineKind kind, int resubmissions = 0) => new()
    {
        ClaimId = Guid.NewGuid(), DeadlineKind = kind, DeadlineAt = DateTimeOffset.UtcNow, Resubmissions = resubmissions
    };

    [Fact]
    public void Missing_ack_sends_a_status_inquiry_first() =>
        LifecycleTimeoutPolicy.Decide(State(DeadlineKind.Acknowledgement), ClaimStatus.Submitted, 1).ShouldBe(TimeoutAction.SendStatusInquiry);

    [Fact]
    public void Silence_after_the_inquiry_resubmits_once() =>
        LifecycleTimeoutPolicy.Decide(State(DeadlineKind.StatusInquiry), ClaimStatus.Submitted, 1).ShouldBe(TimeoutAction.Resubmit);

    [Fact]
    public void Silence_after_the_resubmission_escalates_to_ops()
    {
        LifecycleTimeoutPolicy.Decide(State(DeadlineKind.Resubmission, 1), ClaimStatus.Submitted, 1).ShouldBe(TimeoutAction.Escalate);
        LifecycleTimeoutPolicy.Decide(State(DeadlineKind.StatusInquiry, 1), ClaimStatus.Submitted, 1).ShouldBe(TimeoutAction.Escalate);
    }

    [Theory]
    [InlineData(ClaimStatus.AcceptedByPayer)]
    [InlineData(ClaimStatus.RejectedByPayer)]
    [InlineData(ClaimStatus.Pended)]
    public void Any_payer_answer_clears_the_deadline(ClaimStatus status) =>
        LifecycleTimeoutPolicy.Decide(State(DeadlineKind.Acknowledgement), status, 1).ShouldBe(TimeoutAction.Clear);

    [Fact]
    public void No_deadline_means_nothing_to_do() =>
        LifecycleTimeoutPolicy.Decide(new ClaimLifecycleState(), ClaimStatus.Submitted, 1).ShouldBe(TimeoutAction.None);
}
