"""Deterministic generator for members and ~5,000 historical claims with realistic outcomes.

Denials follow a hidden logistic model (missing prior auth, modifier 25 with some payers, high charges,
late filing, ...) so the denial predictor has real signal to learn, and dashboards show believable trends.
"""
from __future__ import annotations

import math
import random
from dataclasses import dataclass
from datetime import date, datetime, timedelta, timezone

from .reference import (CARC_CATEGORY, ENCOUNTERS, ENROLLED, ORGS, PAYERS, PRIOR_AUTH_REQUIRED, PROCEDURES)

SYLLABLES_FIRST = ["Ka", "Lo", "Mi", "Ra", "Te", "Vo", "Ze", "Ni", "Da", "Fe", "Jo", "Su", "Ari", "Bel", "Cor", "Dax"]
SYLLABLES_LAST = ["mor", "vane", "drel", "quist", "lund", "bry", "tark", "selm", "wynd", "hollo", "pren", "zett"]


@dataclass(frozen=True)
class GeneratorConfig:
    seed: int = 42
    members: int = 800
    claims: int = 5000
    days: int = 365
    today: date | None = None


def _name(rng: random.Random) -> tuple[str, str]:
    first = rng.choice(SYLLABLES_FIRST) + rng.choice(["n", "ra", "lin", "ko", "ssa", "x", ""])
    last = rng.choice(SYLLABLES_FIRST) + rng.choice(SYLLABLES_LAST)
    return first.capitalize(), last.capitalize()


def generate_members(cfg: GeneratorConfig) -> list[dict]:
    rng = random.Random(cfg.seed)
    today = cfg.today or date.today()
    members = []
    payer_ids = list(PAYERS)
    for i in range(cfg.members):
        payer = payer_ids[i % len(payer_ids)]
        info = PAYERS[payer]
        first, last = _name(rng)
        pediatric = rng.random() < 0.2
        age = rng.randint(1, 15) if pediatric else rng.randint(19, 88)
        dob = today - timedelta(days=age * 365 + rng.randint(0, 364))
        termed = rng.random() < 0.06
        members.append({
            "memberId": f"{info['prefix']}{1000100 + i}",
            "payerId": payer,
            "firstName": first,
            "lastName": last,
            "dateOfBirth": dob.isoformat(),
            "gender": rng.choice(["F", "M"]),
            "plan": info["plan"],
            "coverageStart": (today - timedelta(days=rng.randint(400, 3000))).isoformat(),
            "coverageEnd": (today - timedelta(days=rng.randint(10, 200))).isoformat() if termed else None,
            "copay": 25 if payer == "PAYER-A" else 30,
            "deductible": rng.choice([500, 1000, 1500, 2500]),
            "deductibleMet": rng.choice([0, 200, 500, 1000, 2500]),
        })
    return members


def _sigmoid(x: float) -> float:
    return 1 / (1 + math.exp(-x))


def _denial_logit(payer: str, procs: list[str], mods: set[str], has_pa: bool, charge_ratio: float, days_to_submit: int,
                  max_units: int, dx_count: int, line_count: int, age: int, org_kind: str, frequency: str) -> tuple[float, str]:
    """Hidden ground truth. Returns the logit and the CARC most responsible if the claim is denied."""
    score = -3.1
    reasons: list[tuple[float, str]] = [(0.1, "16")]
    if payer == "PAYER-C":
        score += 0.6
    elif payer == "PAYER-B":
        score += 0.3
    if "25" in mods:
        w = 1.4 if payer == "PAYER-B" else 0.6 if payer == "PAYER-A" else 0.2
        score += w
        reasons.append((w, "4"))
    if any(p in PRIOR_AUTH_REQUIRED for p in procs) and not has_pa:
        score += 2.8
        reasons.append((2.8, "197"))
    if charge_ratio > 1.4:
        w = min(1.6, (charge_ratio - 1.4) * 2.0 + 0.6)
        score += w
        reasons.append((w, "50"))
    if days_to_submit > PAYERS[payer]["filing_days"]:
        score += 4.0
        reasons.append((4.0, "29"))
    elif days_to_submit > 45:
        score += 0.7
        reasons.append((0.7, "16"))
    if max_units > 4 and payer == "PAYER-B":
        score += 1.6
        reasons.append((1.6, "151"))
    if dx_count == 1 and line_count >= 3:
        score += 0.5
        reasons.append((0.5, "11"))
    if frequency == "7":
        score -= 0.3
    if age > 65:
        score += 0.2
    if org_kind == "peds":
        score -= 0.2
    return score, max(reasons)[1]


def generate_history(cfg: GeneratorConfig, members: list[dict]) -> list[dict]:
    rng = random.Random(cfg.seed + 1)
    today = cfg.today or date.today()
    by_payer: dict[str, list[dict]] = {}
    for m in members:
        by_payer.setdefault(m["payerId"], []).append(m)

    org_ids = list(ORGS)
    org_weights = [0.45, 0.35, 0.20]
    claims: list[dict] = []
    eft_seq = 0

    for n in range(cfg.claims):
        org_id = rng.choices(org_ids, org_weights)[0]
        org = ORGS[org_id]
        payer = rng.choice(ENROLLED[org_id])
        member = rng.choice(by_payer[payer])
        dob = date.fromisoformat(member["dateOfBirth"])

        weights, templates = zip(*[(w, (p, d)) for w, p, d in ENCOUNTERS[org["kind"]]])
        procs, dx = rng.choices(templates, weights)[0]
        procs = list(procs)
        dx = list(dx)

        dos = today - timedelta(days=rng.randint(5, cfg.days))
        age = (dos - dob).days // 365
        # most claims go out within a week; a long tail is filed late
        lag = int(rng.expovariate(1 / 6)) if rng.random() > 0.06 else rng.randint(45, 220)
        submitted = min(dos + timedelta(days=lag), today - timedelta(days=1))
        days_to_submit = (submitted - dos).days

        has_pa = not any(p in PRIOR_AUTH_REQUIRED for p in procs) or rng.random() < 0.75
        lines = []
        mods_all: set[str] = set()
        for i, code in enumerate(procs):
            category, typical = PROCEDURES[code]
            mods = []
            if category == "EM" and len(procs) > 1 and rng.random() < 0.55:
                mods.append("25")
            if category == "Imaging" and org["kind"] == "ortho" and rng.random() < 0.5:
                mods.append(rng.choice(["RT", "LT"]))
            units = rng.choice([1, 2, 3, 4, 4, 5, 6]) if category == "Therapy" else (rng.randint(4, 10) if code == "J1100" else 1)
            markup = rng.lognormvariate(0, 0.18) * (1.7 if rng.random() < 0.07 else 1.0)
            charge = round(typical * units * markup, 2)
            mods_all.update(mods)
            lines.append({
                "procedureCode": code, "modifiers": mods, "units": units, "charge": charge, "serviceDate": dos.isoformat(),
                "placeOfService": "11", "dxPointers": [min(i + 1, len(dx))] if len(dx) > 1 else [1],
            })

        total = round(sum(l["charge"] for l in lines), 2)
        typical_total = sum(PROCEDURES[l["procedureCode"]][1] * l["units"] for l in lines)
        charge_ratio = total / typical_total
        max_units = max(l["units"] for l in lines)
        logit, carc = _denial_logit(payer, procs, mods_all, has_pa, charge_ratio, days_to_submit, max_units, len(dx), len(lines),
                                    age, org["kind"], "1")
        denied = rng.random() < _sigmoid(logit)

        scrub_edits = []
        if rng.random() < 0.11:
            scrub_edits.append({"ruleCode": rng.choice(["CB007", "CB004", "CB003", "CB006", "PA-A01", "CB010", "CB009"]),
                                "severity": "Error"})
        if rng.random() < 0.15:
            scrub_edits.append({"ruleCode": rng.choice(["CB010", "PA-A02", "CB012", "PA-C01"]), "severity": "Warning"})

        accepted_first_pass = rng.random() > (0.08 if payer == "PAYER-C" else 0.04)
        fee = PAYERS[payer]["fee"]
        submitted_at = datetime.combine(submitted, datetime.min.time(), timezone.utc) + timedelta(hours=rng.randint(8, 17))
        turnaround = int(rng.gammavariate(3, 6)) + (7 if payer == "PAYER-C" else 0)
        adjudicated = submitted + timedelta(days=turnaround)
        still_open = adjudicated >= today

        claim = {
            "claimNumber": f"CB{submitted:%y}{n + 1:07d}",
            "orgId": org_id, "payerId": payer, "billingNpi": org["npi"], "renderingNpi": rng.choice(org["rendering"]),
            "memberId": member["memberId"],
            "patient": {"firstName": member["firstName"], "lastName": member["lastName"], "dateOfBirth": member["dateOfBirth"],
                        "gender": member["gender"]},
            "priorAuthNumber": f"PA{rng.randint(100000, 999999)}" if has_pa and any(p in PRIOR_AUTH_REQUIRED for p in procs) else None,
            "frequencyCode": "1", "diagnoses": dx, "lines": lines, "totalCharge": total,
            "createdAt": (submitted_at - timedelta(minutes=rng.randint(5, 600))).isoformat(),
            "submittedAt": submitted_at.isoformat(),
            "daysToSubmit": days_to_submit,
            "scrub": {"firstPassClean": not any(e["severity"] == "Error" for e in scrub_edits), "edits": scrub_edits},
            "acceptedFirstPass": accepted_first_pass,
            "outcome": None, "carc": None, "category": None, "rarc": [],
            "paidAmount": 0.0, "allowedAmount": 0.0, "patientResponsibility": 0.0, "adjustments": [],
            "adjudicatedAt": None, "eftTraceNumber": None, "appeal": None, "finalStatus": "AcceptedByPayer",
        }

        # label for the denial model: the payer's first decision, before any appeal
        claim["deniedInitially"] = None if still_open else denied

        if not still_open:
            eft_seq += 1
            claim["adjudicatedAt"] = datetime.combine(adjudicated, datetime.min.time(), timezone.utc).isoformat()
            claim["eftTraceNumber"] = f"EFT{adjudicated:%Y%m%d}{eft_seq:05d}"
            if denied:
                claim.update(outcome="denied", carc=carc, category=CARC_CATEGORY.get(carc, "Other"),
                             adjustments=[{"group": "CO", "reason": carc, "amount": total}], finalStatus="Denied")
                if rng.random() < 0.3 and carc in ("197", "50", "4", "16"):
                    won = rng.random() < 0.45
                    resolved = adjudicated + timedelta(days=rng.randint(15, 60)) < today
                    claim["appeal"] = {"status": ("Won" if won else "Lost") if resolved else "Submitted",
                                       "submittedAt": (adjudicated + timedelta(days=rng.randint(3, 20))).isoformat()}
                    claim["finalStatus"] = "Appealed" if not resolved else ("Closed" if not won else "Closed")
                    if resolved and won:
                        claim["outcome"] = "paid"
                        claim["paidAmount"] = round(total * fee * 0.8, 2)
            else:
                allowed = round(total * fee, 2)
                deductible = round(min(allowed, rng.choice([0, 0, 0, 50, 120])), 2)
                coins = round((allowed - deductible) * (0.2 if payer != "PAYER-A" else 0.1), 2)
                partial = rng.random() < 0.08 and len(lines) > 1
                denied_line = round(lines[-1]["charge"], 2) if partial else 0.0
                paid = round(max(0.0, allowed - deductible - coins - denied_line * fee), 2)
                adjustments = [{"group": "CO", "reason": "45", "amount": round(total - allowed, 2)}]
                if deductible:
                    adjustments.append({"group": "PR", "reason": "1", "amount": deductible})
                if coins:
                    adjustments.append({"group": "PR", "reason": "2", "amount": coins})
                if partial:
                    adjustments.append({"group": "CO", "reason": "97", "amount": round(denied_line * fee, 2)})
                claim.update(outcome="partial" if partial else "paid", paidAmount=paid, allowedAmount=allowed,
                             patientResponsibility=round(deductible + coins, 2), adjustments=adjustments,
                             finalStatus="Posted" if partial else "Closed",
                             carc="97" if partial else None, category="Bundling" if partial else None)
        claims.append(claim)
    return claims


def features_from_history(claim: dict) -> dict:
    """The request shape the denial predictor scores, built from a history record."""
    lines = claim["lines"]
    dob = date.fromisoformat(claim["patient"]["dateOfBirth"])
    dos = date.fromisoformat(lines[0]["serviceDate"])
    return {
        "claimId": None,
        "orgId": claim["orgId"],
        "payerId": claim["payerId"],
        "procedureCodes": [l["procedureCode"] for l in lines],
        "modifiers": sorted({m for l in lines for m in l["modifiers"]}),
        "lineCharges": [l["charge"] for l in lines],
        "units": [l["units"] for l in lines],
        "placeOfService": lines[0]["placeOfService"],
        "diagnosisCount": len(claim["diagnoses"]),
        "diagnoses": claim["diagnoses"],
        "hasPriorAuth": claim["priorAuthNumber"] is not None,
        "frequencyCode": claim["frequencyCode"],
        "daysToSubmit": claim["daysToSubmit"],
        "patientAge": (dos - dob).days // 365,
    }
