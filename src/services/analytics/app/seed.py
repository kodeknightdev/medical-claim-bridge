"""Loads the seeder's synthetic history straight into the fact tables."""
from __future__ import annotations

import gzip
import json
from datetime import date, datetime

from sqlalchemy.orm import Session

from .db import ClaimFact, ScrubEditFact


def _ts(value):
    return datetime.fromisoformat(value) if value else None


def import_history(session: Session, records: list[dict]) -> int:
    existing = {cid for (cid,) in session.query(ClaimFact.claim_number).all()}
    added = 0
    for r in records:
        if r["claimNumber"] in existing:
            continue
        claim_id = r["claimId"]
        submitted = _ts(r.get("submittedAt"))
        adjudicated = _ts(r.get("adjudicatedAt"))
        paid = r.get("outcome") in ("paid", "partial")
        adjustments = sum(a["amount"] for a in r.get("adjustments", []) if a["group"] != "PR")
        session.add(ClaimFact(
            claim_id=claim_id, claim_number=r["claimNumber"], org_id=r["orgId"], payer_id=r["payerId"],
            total_charge=r["totalCharge"], first_service_date=date.fromisoformat(r["lines"][0]["serviceDate"]),
            created_at=_ts(r.get("createdAt")), submitted_at=submitted, acked_at=submitted,
            accepted_first_pass=r.get("acceptedFirstPass"), scrub_clean_first_pass=r["scrub"]["firstPassClean"],
            status=r.get("finalStatus", "Closed"), adjudicated_at=adjudicated, paid_at=adjudicated if paid else None,
            paid_amount=r.get("paidAmount", 0.0), allowed_amount=r.get("allowedAmount", 0.0),
            patient_responsibility=r.get("patientResponsibility", 0.0), adjustment_amount=adjustments if not r.get("deniedInitially") else 0.0,
            denied=bool(r.get("deniedInitially")), denial_category=r.get("category"), carc=r.get("carc"),
            is_correction=r.get("frequencyCode") == "7", reversed_amount=0.0))
        for edit in r["scrub"]["edits"]:
            session.add(ScrubEditFact(claim_id=claim_id, org_id=r["orgId"], payer_id=r["payerId"], rule_code=edit["ruleCode"],
                                      severity=edit["severity"], first_attempt=True, at=_ts(r.get("createdAt"))))
        added += 1
    session.commit()
    return added


def read_history_blob(connection_string: str, container: str, name: str) -> list[dict]:
    from azure.storage.blob import BlobServiceClient

    data = BlobServiceClient.from_connection_string(connection_string).get_blob_client(container, name).download_blob().readall()
    if name.endswith(".gz"):
        data = gzip.decompress(data)
    return [json.loads(line) for line in data.decode().splitlines() if line.strip()]
