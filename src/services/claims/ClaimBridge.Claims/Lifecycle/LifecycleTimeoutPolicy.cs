using ClaimBridge.Claims.Data;
using ClaimBridge.Claims.Domain;

namespace ClaimBridge.Claims.Lifecycle;

public enum TimeoutAction { None, SendStatusInquiry, Resubmit, Escalate, Clear }

/// <summary>What to do when a durable deadline expires. Kept pure so the escalation ladder is easy to test.</summary>
public static class LifecycleTimeoutPolicy
{
    public static TimeoutAction Decide(ClaimLifecycleState state, ClaimStatus status, int maxResubmissions)
    {
        if (state.DeadlineAt is null || state.DeadlineKind == DeadlineKind.None) return TimeoutAction.None;
        // anything other than a silent submission means the payer answered in the meantime
        if (status != ClaimStatus.Submitted) return TimeoutAction.Clear;

        return state.DeadlineKind switch
        {
            DeadlineKind.Acknowledgement => TimeoutAction.SendStatusInquiry,
            DeadlineKind.StatusInquiry when state.Resubmissions < maxResubmissions => TimeoutAction.Resubmit,
            DeadlineKind.StatusInquiry => TimeoutAction.Escalate,
            DeadlineKind.Resubmission => TimeoutAction.Escalate,
            _ => TimeoutAction.Clear
        };
    }
}
