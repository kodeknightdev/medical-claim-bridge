"""Imports the seeder's synthetic claim history (JSON lines, gzipped) as training data."""
from __future__ import annotations

import gzip
import json
from datetime import date, datetime

from sqlalchemy import select
from sqlalchemy.orm import Session

from .db import TrainingClaim


def history_to_features(claim: dict) -> dict:
    lines = claim["lines"]
    dob = date.fromisoformat(claim["patient"]["dateOfBirth"])
    dos = date.fromisoformat(lines[0]["serviceDate"])
    return {
        "payerId": claim["payerId"],
        "orgId": claim["orgId"],
        "procedureCodes": [line["procedureCode"] for line in lines],
        "modifiers": sorted({m for line in lines for m in line["modifiers"]}),
        "lineCharges": [line["charge"] for line in lines],
        "units": [line["units"] for line in lines],
        "placeOfService": lines[0]["placeOfService"],
        "diagnosisCount": len(claim["diagnoses"]),
        "hasPriorAuth": claim.get("priorAuthNumber") is not None,
        "frequencyCode": claim.get("frequencyCode", "1"),
        "daysToSubmit": claim.get("daysToSubmit", 0),
        "patientAge": (dos - dob).days // 365,
    }


def import_history(session: Session, records: list[dict]) -> int:
    existing = set(session.scalars(select(TrainingClaim.claim_number)).all())
    added = 0
    for record in records:
        if record.get("deniedInitially") is None or record["claimNumber"] in existing:
            continue
        session.add(TrainingClaim(
            claim_number=record["claimNumber"],
            features=history_to_features(record),
            denied=bool(record["deniedInitially"]),
            carc=record.get("carc"),
            adjudicated_at=datetime.fromisoformat(record["adjudicatedAt"]) if record.get("adjudicatedAt") else None,
        ))
        added += 1
    session.commit()
    return added


def read_history_blob(connection_string: str, container: str, name: str) -> list[dict]:
    from azure.storage.blob import BlobServiceClient

    data = BlobServiceClient.from_connection_string(connection_string).get_blob_client(container, name).download_blob().readall()
    if name.endswith(".gz"):
        data = gzip.decompress(data)
    return [json.loads(line) for line in data.decode().splitlines() if line.strip()]
