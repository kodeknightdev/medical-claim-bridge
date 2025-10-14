from datetime import datetime, timedelta, timezone

import pandas as pd

from app import kpis

NOW = datetime(2025, 11, 1, tzinfo=timezone.utc)


def frame(rows: list[dict]) -> pd.DataFrame:
    defaults = {"claim_id": "c", "payer_id": "PAYER-A", "total_charge": 100.0, "paid_amount": 0.0, "adjustment_amount": 0.0,
                "patient_responsibility": 0.0, "submitted_at": NOW - timedelta(days=10), "adjudicated_at": None, "paid_at": None,
                "accepted_first_pass": True, "scrub_clean_first_pass": True, "denied": False, "denial_category": None}
    df = pd.DataFrame([{**defaults, **r} for r in rows], columns=list(defaults))
    for col in ("submitted_at", "adjudicated_at", "paid_at"):
        df[col] = pd.to_datetime(df[col], utc=True)
    return df


def test_summary_rates():
    facts = frame([
        {"claim_id": "1", "paid_amount": 80, "adjustment_amount": 20, "adjudicated_at": NOW, "paid_at": NOW},
        {"claim_id": "2", "denied": True, "adjudicated_at": NOW, "accepted_first_pass": False},
        {"claim_id": "3", "scrub_clean_first_pass": False},
        {"claim_id": "4", "scrub_clean_first_pass": None, "accepted_first_pass": None},
    ])
    edits = pd.DataFrame([{"rule_code": "CB007", "severity": "Error"}] * 3 + [{"rule_code": "CB010", "severity": "Warning"}])
    s = kpis.summary(facts, edits, NOW)
    assert s["claims"] == 4
    assert s["denialRate"] == 0.5
    assert s["firstPassAcceptanceRate"] == round(2 / 3, 4)
    assert s["cleanClaimRate"] == round(2 / 3, 4)
    assert s["topScrubEdits"][0] == {"ruleCode": "CB007", "severity": "Error", "count": 3}
    assert s["medianPaymentTurnaroundDays"] == 10.0


def test_ageing_buckets_follow_submission_age():
    facts = frame([
        {"claim_id": "a", "submitted_at": NOW - timedelta(days=5), "total_charge": 100},
        {"claim_id": "b", "submitted_at": NOW - timedelta(days=45), "total_charge": 200},
        {"claim_id": "c", "submitted_at": NOW - timedelta(days=75), "total_charge": 300},
        {"claim_id": "d", "submitted_at": NOW - timedelta(days=120), "total_charge": 400, "payer_id": "PAYER-B"},
        {"claim_id": "e", "submitted_at": NOW - timedelta(days=50), "paid_at": NOW, "paid_amount": 100},
    ])
    result = kpis.ageing(facts, NOW)
    assert result["buckets"] == {"0-30": 100.0, "31-60": 200.0, "61-90": 300.0, "90+": 400.0}
    assert result["byPayer"]["PAYER-B"]["90+"] == 400.0


def test_denial_breakdown_by_payer_and_category():
    facts = frame([
        {"claim_id": "1", "adjudicated_at": NOW, "denied": True, "denial_category": "Authorization"},
        {"claim_id": "2", "adjudicated_at": NOW, "denied": True, "denial_category": "Coding", "payer_id": "PAYER-B"},
        {"claim_id": "3", "adjudicated_at": NOW, "payer_id": "PAYER-B"},
    ])
    result = kpis.denials(facts)
    by_payer = {r["payerId"]: r for r in result["byPayer"]}
    assert by_payer["PAYER-A"]["denialRate"] == 1.0
    assert by_payer["PAYER-B"]["denialRate"] == 0.5
    assert {c["category"] for c in result["byCategory"]} == {"Authorization", "Coding"}


def test_empty_frames_do_not_crash():
    empty = frame([])
    assert kpis.summary(empty, pd.DataFrame(columns=["rule_code", "severity"]), NOW) == {"claims": 0}
    assert kpis.trends(empty) == []
