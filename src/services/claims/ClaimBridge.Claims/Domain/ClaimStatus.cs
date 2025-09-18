namespace ClaimBridge.Claims.Domain;

public enum ClaimStatus
{
    Received,
    Scrubbing,
    ScrubFailed,
    ReadyToSubmit,
    Submitted,
    AcceptedByPayer,
    RejectedByPayer,
    Pended,
    Paid,
    PartiallyPaid,
    Denied,
    Posted,
    Closed,
    Escalated,
    Appealed,
    Superseded,
    Voided
}

public enum LifecycleTrigger
{
    StartScrub,
    ScrubPassed,
    ScrubFailed,
    Edited,
    Submit,
    PayerAccepted,
    PayerRejected,
    PayerPended,
    AdjudicatedPaid,
    AdjudicatedPartial,
    AdjudicatedDenied,
    RemittancePosted,
    Close,
    Escalate,
    Appeal,
    AppealWon,
    AppealLost,
    ReversalPosted,
    Supersede,
    Void
}

public enum ClaimSource { Portal, Batch, Api }
