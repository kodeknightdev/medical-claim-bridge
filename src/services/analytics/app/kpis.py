"""KPI maths over the fact tables. Pure pandas so the definitions are easy to test and read."""
from __future__ import annotations

from datetime import datetime, timedelta, timezone

import numpy as np
import pandas as pd

BUCKETS = ["0-30", "31-60", "61-90", "90+"]


def _rate(numerator: int, denominator: int) -> float | None:
    return None if denominator == 0 else round(numerator / denominator, 4)


def outstanding(facts: pd.DataFrame) -> pd.Series:
    """Payer balance still owed: open submitted claims carry their full charge, denials count until resolved."""
    open_claims = facts["submitted_at"].notna() & facts["paid_at"].isna()
    balance = facts["total_charge"] - facts["paid_amount"] - facts["adjustment_amount"] - facts["patient_responsibility"]
    return np.where(open_claims, facts["total_charge"], balance.clip(lower=0))


def summary(facts: pd.DataFrame, edits: pd.DataFrame, now: datetime | None = None) -> dict:
    now = now or datetime.now(timezone.utc)
    if facts.empty:
        return {"claims": 0}

    acked = facts["accepted_first_pass"].dropna()
    scrubbed = facts["scrub_clean_first_pass"].dropna()
    adjudicated = facts[facts["adjudicated_at"].notna()]
    paid = facts[facts["paid_at"].notna() & facts["submitted_at"].notna()]
    turnaround = (paid["paid_at"] - paid["submitted_at"]).dt.total_seconds() / 86400

    owed = pd.Series(outstanding(facts), index=facts.index)
    recent = facts[facts["submitted_at"] >= now - timedelta(days=90)]
    daily_charges = recent["total_charge"].sum() / 90 if not recent.empty else 0

    return {
        "claims": int(len(facts)),
        "billed": round(float(facts["total_charge"].sum()), 2),
        "collected": round(float(facts["paid_amount"].sum()), 2),
        "collectionRate": _rate(facts["paid_amount"].sum(), facts["total_charge"].sum()),
        "firstPassAcceptanceRate": _rate(int(acked.sum()), int(len(acked))),
        "cleanClaimRate": _rate(int(scrubbed.sum()), int(len(scrubbed))),
        "denialRate": _rate(int(adjudicated["denied"].sum()), int(len(adjudicated))),
        "medianPaymentTurnaroundDays": None if turnaround.empty else round(float(turnaround.median()), 1),
        "outstanding": round(float(owed.sum()), 2),
        "daysInAr": None if daily_charges == 0 else round(float(owed.sum() / daily_charges), 1),
        "topScrubEdits": top_edits(edits, 5),
    }


def ageing(facts: pd.DataFrame, now: datetime | None = None) -> dict:
    now = now or datetime.now(timezone.utc)
    owed = pd.Series(outstanding(facts), index=facts.index) if not facts.empty else pd.Series(dtype=float)
    frame = facts.assign(owed=owed)
    frame = frame[(frame["owed"] > 0) & frame["submitted_at"].notna()]
    if frame.empty:
        return {"buckets": {b: 0.0 for b in BUCKETS}, "byPayer": {}}
    age = (now - frame["submitted_at"]).dt.days
    frame = frame.assign(bucket=pd.cut(age, bins=[-1, 30, 60, 90, 100000], labels=BUCKETS))
    totals = frame.groupby("bucket", observed=False)["owed"].sum()
    by_payer = frame.pivot_table(index="payer_id", columns="bucket", values="owed", aggfunc="sum", fill_value=0, observed=False)
    return {
        "buckets": {b: round(float(totals.get(b, 0)), 2) for b in BUCKETS},
        "byPayer": {payer: {b: round(float(row.get(b, 0)), 2) for b in BUCKETS} for payer, row in by_payer.iterrows()},
    }


def denials(facts: pd.DataFrame) -> dict:
    adjudicated = facts[facts["adjudicated_at"].notna()]
    if adjudicated.empty:
        return {"byPayer": [], "byCategory": [], "byPayerAndCategory": []}
    by_payer = adjudicated.groupby("payer_id").agg(adjudicated=("claim_id", "count"), denied=("denied", "sum")).reset_index()
    by_payer["denialRate"] = (by_payer["denied"] / by_payer["adjudicated"]).round(4)
    denied = adjudicated[adjudicated["denied"]]
    by_category = denied.groupby(denied["denial_category"].fillna("Other")).agg(count=("claim_id", "count"),
                                                                                  amount=("total_charge", "sum")).reset_index()
    cross = denied.groupby(["payer_id", denied["denial_category"].fillna("Other")]).size().reset_index(name="count")
    return {
        "byPayer": [{"payerId": r.payer_id, "adjudicated": int(r.adjudicated), "denied": int(r.denied), "denialRate": float(r.denialRate)}
                    for r in by_payer.itertuples()],
        "byCategory": [{"category": r.denial_category, "count": int(r.count), "amount": round(float(r.amount), 2)}
                       for r in by_category.sort_values("count", ascending=False).itertuples()],
        "byPayerAndCategory": [{"payerId": r.payer_id, "category": r.denial_category, "count": int(r.count)} for r in cross.itertuples()],
    }


def top_edits(edits: pd.DataFrame, limit: int = 10) -> list[dict]:
    if edits.empty:
        return []
    counts = edits.groupby(["rule_code", "severity"]).size().reset_index(name="count").sort_values("count", ascending=False)
    return [{"ruleCode": r.rule_code, "severity": r.severity, "count": int(r.count)} for r in counts.head(limit).itertuples()]


def trends(facts: pd.DataFrame, months: int = 12) -> list[dict]:
    frame = facts[facts["submitted_at"].notna()]
    if frame.empty:
        return []
    month = frame["submitted_at"].dt.tz_convert("UTC").dt.strftime("%Y-%m")
    grouped = frame.assign(month=month).groupby("month")
    rows = []
    for key, g in grouped:
        adjudicated = g[g["adjudicated_at"].notna()]
        scrubbed = g["scrub_clean_first_pass"].dropna()
        rows.append({
            "month": key,
            "claims": int(len(g)),
            "billed": round(float(g["total_charge"].sum()), 2),
            "collected": round(float(g["paid_amount"].sum()), 2),
            "denialRate": _rate(int(adjudicated["denied"].sum()), int(len(adjudicated))),
            "cleanClaimRate": _rate(int(scrubbed.sum()), int(len(scrubbed))),
        })
    return rows[-months:]
