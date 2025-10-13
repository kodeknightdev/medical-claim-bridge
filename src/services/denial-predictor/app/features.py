"""Feature engineering shared by training and scoring, plus the plain-English labels for each factor."""
from __future__ import annotations

from dataclasses import dataclass

import numpy as np
import pandas as pd

PRIOR_AUTH_REQUIRED = {"73721", "72148", "29881", "27447"}

TYPICAL_CHARGE = {
    "99202": 110, "99203": 165, "99204": 250, "99205": 330, "99212": 75, "99213": 120, "99214": 175, "99215": 245,
    "99385": 230, "99392": 180, "99395": 210, "99396": 225, "G0439": 175, "90471": 30, "90686": 40, "90715": 55,
    "36415": 15, "80053": 45, "80061": 50, "81002": 12, "83036": 40, "85025": 35, "87880": 30, "93000": 60,
    "71046": 95, "73030": 85, "73562": 90, "72148": 1450, "73721": 1350, "11102": 150, "20610": 180, "29881": 4800,
    "27447": 24000, "97110": 45, "97140": 50, "97161": 120, "J1100": 5,
}

PAYERS = ["PAYER-A", "PAYER-B", "PAYER-C"]
PAYER_NAMES = {"PAYER-A": "Northwind", "PAYER-B": "Bluepeak", "PAYER-C": "Cascade"}

FEATURES = [
    "payer_a", "payer_b", "payer_c", "mod_25", "mod_59", "missing_prior_auth", "charge_ratio", "max_units",
    "line_count", "dx_count", "days_to_submit", "telehealth", "is_correction", "patient_age",
]


@dataclass(frozen=True)
class FactorDef:
    key: str
    columns: tuple[str, ...]


# factors group raw columns so explanations talk about one idea at a time
FACTORS = [
    FactorDef("missing_prior_auth", ("missing_prior_auth",)),
    FactorDef("modifier_25", ("mod_25",)),
    FactorDef("modifier_59", ("mod_59",)),
    FactorDef("high_charge", ("charge_ratio",)),
    FactorDef("late_filing", ("days_to_submit",)),
    FactorDef("units", ("max_units",)),
    FactorDef("payer", ("payer_a", "payer_b", "payer_c")),
    FactorDef("thin_diagnosis", ("dx_count", "line_count")),
    FactorDef("patient_age", ("patient_age",)),
]


def to_row(claim: dict) -> dict[str, float]:
    procedures = claim.get("procedureCodes") or []
    charges = [float(c) for c in claim.get("lineCharges") or []]
    units = [float(u) for u in claim.get("units") or [1] * len(procedures)]
    typical = sum(TYPICAL_CHARGE.get(p, c) * u for p, c, u in zip(procedures, charges, units)) or 1.0
    mods = set(claim.get("modifiers") or [])
    payer = claim.get("payerId", "")
    return {
        "payer_a": float(payer == "PAYER-A"),
        "payer_b": float(payer == "PAYER-B"),
        "payer_c": float(payer == "PAYER-C"),
        "mod_25": float("25" in mods),
        "mod_59": float("59" in mods),
        "missing_prior_auth": float(any(p in PRIOR_AUTH_REQUIRED for p in procedures) and not claim.get("hasPriorAuth", False)),
        "charge_ratio": round(sum(charges) / typical, 4),
        "max_units": max(units) if units else 1.0,
        "line_count": float(len(procedures)),
        "dx_count": float(claim.get("diagnosisCount", 1)),
        "days_to_submit": float(max(0, claim.get("daysToSubmit", 0))),
        "telehealth": float(claim.get("placeOfService") in ("02", "10")),
        "is_correction": float(claim.get("frequencyCode", "1") == "7"),
        "patient_age": float(claim.get("patientAge", 40)),
    }


def to_frame(claims: list[dict]) -> pd.DataFrame:
    return pd.DataFrame([to_row(c) for c in claims], columns=FEATURES).astype(np.float64)


def describe(factor: str, row: dict[str, float], payer_id: str) -> str:
    name = PAYER_NAMES.get(payer_id, payer_id)
    match factor:
        case "missing_prior_auth":
            return "Procedure usually needs prior authorisation and none is on the claim"
        case "modifier_25":
            return f"Modifier 25 with {name}"
        case "modifier_59":
            return f"Modifier 59 with {name}"
        case "high_charge":
            return f"Charge is {row['charge_ratio']:.1f}x the payer average for these codes"
        case "late_filing":
            return f"Filed {int(row['days_to_submit'])} days after the date of service"
        case "units":
            return f"{int(row['max_units'])} units on one line"
        case "payer":
            return f"{name}'s historical denial rate"
        case "thin_diagnosis":
            return "Several services supported by a single diagnosis"
        case "patient_age":
            return f"Patient age {int(row['patient_age'])}"
    return factor
