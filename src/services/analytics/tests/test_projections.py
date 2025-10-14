import uuid
from datetime import datetime, timedelta, timezone

from app.db import ClaimFact, ScrubEditFact
from app.projections import apply

CLAIM = str(uuid.uuid4())
T0 = datetime(2025, 10, 1, 9, 0, tzinfo=timezone.utc)


def env(event_type: str, data: dict, minutes: int = 0, event_id: str | None = None) -> dict:
    return {"id": event_id or str(uuid.uuid4()), "type": event_type, "schemaVersion": 1,
            "occurredAt": (T0 + timedelta(minutes=minutes)).isoformat(), "source": "test", "data": data}


def status(to: str, frm: str, minutes: int, **extra) -> dict:
    data = {"claimId": CLAIM, "claimNumber": "CB250000001", "orgId": "org-1", "payerId": "PAYER-A", "fromStatus": frm,
            "toStatus": to, "trigger": "x", "totalCharge": 355.0, "submittedAt": None, "firstServiceDate": "2025-09-28",
            "isCorrection": False}
    data.update(extra)
    return env("claims.status-changed", data, minutes)


def test_claim_journey_builds_one_fact(session):
    apply(session, status("Received", "Received", 0, trigger="Created"))
    apply(session, env("claims.claim-scrubbed", {"claimId": CLAIM, "claimNumber": "CB250000001", "orgId": "org-1", "payerId": "PAYER-A",
                                                  "passed": True, "edits": [{"ruleCode": "CB010", "severity": "Warning"}],
                                                  "denialRisk": 0.12, "firstAttempt": True}, 1))
    apply(session, status("Submitted", "ReadyToSubmit", 2, submittedAt=(T0 + timedelta(minutes=2)).isoformat()))
    apply(session, status("AcceptedByPayer", "Submitted", 3))
    apply(session, env("remittance.payment-posted", {"claimId": CLAIM, "claimNumber": "CB250000001", "orgId": "org-1",
                                                      "payerId": "PAYER-A", "chargeAmount": 355, "paidAmount": 220, "allowedAmount": 284,
                                                      "patientResponsibility": 64, "partiallyPaid": False,
                                                      "adjustments": [{"groupCode": "CO", "reasonCode": "45", "amount": 71},
                                                                      {"groupCode": "PR", "reasonCode": "2", "amount": 64}]}, 600))

    fact = session.get(ClaimFact, CLAIM)
    assert fact.accepted_first_pass is True
    assert fact.scrub_clean_first_pass is True
    assert fact.denial_risk == 0.12
    assert fact.paid_amount == 220 and fact.adjustment_amount == 71
    assert fact.total_charge == fact.paid_amount + fact.adjustment_amount + fact.patient_responsibility
    assert session.query(ScrubEditFact).count() == 1


def test_duplicate_events_are_ignored(session):
    event = status("Received", "Received", 0, trigger="Created")
    assert apply(session, event) is True
    assert apply(session, event) is False


def test_rejection_marks_first_pass_failure_even_after_resubmission(session):
    apply(session, status("Submitted", "ReadyToSubmit", 1))
    apply(session, status("RejectedByPayer", "Submitted", 2))
    apply(session, status("AcceptedByPayer", "Submitted", 30))
    assert session.get(ClaimFact, CLAIM).accepted_first_pass is False


def test_denial_and_reversal(session):
    base = {"claimId": CLAIM, "claimNumber": "CB250000001", "orgId": "org-1", "payerId": "PAYER-C"}
    apply(session, env("remittance.payment-posted", {**base, "chargeAmount": 100, "paidAmount": 70, "allowedAmount": 80,
                                                      "patientResponsibility": 10, "partiallyPaid": False, "adjustments": []}))
    apply(session, env("remittance.reversal-posted", {**base, "reversedAmount": 70, "remittanceId": str(uuid.uuid4())}, 5))
    fact = session.get(ClaimFact, CLAIM)
    assert fact.paid_amount == 0 and fact.reversed_amount == 70

    apply(session, env("remittance.claim-denied", {**base, "chargeAmount": 100, "carcCode": "197", "category": "Authorization",
                                                    "rarcCodes": []}, 10))
    assert fact.denied and fact.denial_category == "Authorization"


def test_unknown_event_types_are_skipped(session):
    assert apply(session, env("claims.something-new", {"claimId": CLAIM})) is False
