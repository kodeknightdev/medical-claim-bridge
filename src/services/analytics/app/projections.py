"""Folds integration events into the analytics fact tables. Each handler is idempotent per event id."""
from __future__ import annotations

from datetime import date, datetime, timezone

from sqlalchemy.orm import Session

from .db import ClaimFact, ModelEvent, ProcessedEvent, ScrubEditFact

ADJUDICATED = {"Paid", "PartiallyPaid", "Denied"}


def _ts(value: str | None) -> datetime | None:
    return datetime.fromisoformat(value) if value else None


def _fact(session: Session, data: dict) -> ClaimFact:
    fact = session.get(ClaimFact, data["claimId"])
    if fact is None:
        fact = ClaimFact(claim_id=data["claimId"], claim_number=data["claimNumber"], org_id=data["orgId"], payer_id=data["payerId"],
                         total_charge=0.0, paid_amount=0.0, allowed_amount=0.0, patient_responsibility=0.0,
                         adjustment_amount=0.0, reversed_amount=0.0, denied=False, is_correction=False, status="Received")
        session.add(fact)
    return fact


def on_status_changed(session: Session, data: dict, occurred_at: datetime) -> None:
    fact = _fact(session, data)
    fact.total_charge = float(data.get("totalCharge") or fact.total_charge or 0)
    fact.is_correction = bool(data.get("isCorrection"))
    fact.created_at = fact.created_at or occurred_at
    if data.get("firstServiceDate"):
        fact.first_service_date = date.fromisoformat(data["firstServiceDate"])
    if data.get("submittedAt"):
        fact.submitted_at = fact.submitted_at or _ts(data["submittedAt"])

    to = data["toStatus"]
    if to == data.get("fromStatus") and data.get("trigger") != "Created":
        return
    fact.status = to
    if to in ("AcceptedByPayer", "RejectedByPayer") and fact.acked_at is None:
        fact.acked_at = occurred_at
        # the first payer answer decides first-pass acceptance; later resubmissions don't change it
        fact.accepted_first_pass = to == "AcceptedByPayer"
    if to in ADJUDICATED:
        fact.adjudicated_at = fact.adjudicated_at or occurred_at
        fact.denied = to == "Denied" or fact.denied
    if data.get("denialCategory"):
        fact.denial_category = data["denialCategory"]
        fact.carc = data.get("carcCode")


def on_scrubbed(session: Session, data: dict, occurred_at: datetime) -> None:
    fact = _fact(session, data)
    if data.get("firstAttempt"):
        fact.scrub_clean_first_pass = bool(data["passed"]) and not any(e["severity"] == "Error" for e in data.get("edits", []))
    if data.get("denialRisk") is not None:
        fact.denial_risk = float(data["denialRisk"])
    for edit in data.get("edits", []):
        session.add(ScrubEditFact(claim_id=data["claimId"], org_id=data["orgId"], payer_id=data["payerId"], rule_code=edit["ruleCode"],
                                  severity=edit["severity"], first_attempt=bool(data.get("firstAttempt")), at=occurred_at))


def on_payment_posted(session: Session, data: dict, occurred_at: datetime) -> None:
    fact = _fact(session, data)
    fact.total_charge = fact.total_charge or float(data["chargeAmount"])
    fact.paid_amount = float(data["paidAmount"])
    fact.allowed_amount = float(data["allowedAmount"])
    fact.patient_responsibility = float(data["patientResponsibility"])
    fact.adjustment_amount = float(sum(a["amount"] for a in data.get("adjustments", []) if a["groupCode"] != "PR"))
    fact.paid_at = occurred_at
    fact.adjudicated_at = fact.adjudicated_at or occurred_at
    fact.status = "PartiallyPaid" if data.get("partiallyPaid") else "Paid"


def on_claim_denied(session: Session, data: dict, occurred_at: datetime) -> None:
    fact = _fact(session, data)
    fact.total_charge = fact.total_charge or float(data["chargeAmount"])
    fact.denied = True
    fact.denial_category = data["category"]
    fact.carc = data["carcCode"]
    fact.adjudicated_at = fact.adjudicated_at or occurred_at
    fact.status = "Denied"


def on_reversal(session: Session, data: dict, occurred_at: datetime) -> None:
    fact = _fact(session, data)
    amount = float(data["reversedAmount"])
    fact.reversed_amount += amount
    fact.paid_amount = max(0.0, fact.paid_amount - amount)


def on_model_trained(session: Session, data: dict, occurred_at: datetime) -> None:
    if session.get(ModelEvent, data["version"]) is None:
        session.add(ModelEvent(version=data["version"], auc=float(data["auc"]), brier=data.get("brier"), n_train=int(data["nTrain"]),
                               trained_at=_ts(data["trainedAt"]) or occurred_at))


HANDLERS = {
    "claims.status-changed": on_status_changed,
    "claims.claim-scrubbed": on_scrubbed,
    "remittance.payment-posted": on_payment_posted,
    "remittance.claim-denied": on_claim_denied,
    "remittance.reversal-posted": on_reversal,
    "denial-predictor.model-trained": on_model_trained,
}

TOPICS = list(HANDLERS)


def apply(session: Session, envelope: dict) -> bool:
    """Applies one envelope; returns False when it was a duplicate or an event type we don't track."""
    handler = HANDLERS.get(envelope["type"])
    if handler is None or session.get(ProcessedEvent, envelope["id"]) is not None:
        return False
    occurred = datetime.fromisoformat(envelope["occurredAt"])
    if occurred.tzinfo is None:
        occurred = occurred.replace(tzinfo=timezone.utc)
    handler(session, envelope["data"], occurred)
    session.add(ProcessedEvent(event_id=envelope["id"], processed_at=datetime.now(timezone.utc)))
    session.commit()
    return True
