"""Gradient boosting with calibrated probabilities. Labelled clearly as a demo model everywhere it surfaces."""
from __future__ import annotations

import os
from dataclasses import dataclass, field
from datetime import datetime, timezone

import joblib
import numpy as np
import pandas as pd
from sklearn.calibration import CalibratedClassifierCV
from sklearn.ensemble import GradientBoostingClassifier
from sklearn.inspection import permutation_importance
from sklearn.metrics import brier_score_loss, roc_auc_score
from sklearn.model_selection import train_test_split

from .features import FACTORS, FEATURES, describe, to_frame, to_row

RANDOM_STATE = 42

# values a factor is reset to when measuring how much it pushed this claim's risk up
NEUTRAL = {
    "missing_prior_auth": {"missing_prior_auth": 0.0},
    "modifier_25": {"mod_25": 0.0},
    "modifier_59": {"mod_59": 0.0},
    "high_charge": {"charge_ratio": 1.0},
    "late_filing": {"days_to_submit": 3.0},
    "units": {"max_units": 1.0},
    "payer": {"payer_a": 1.0, "payer_b": 0.0, "payer_c": 0.0},
    "thin_diagnosis": {"dx_count": 2.0, "line_count": 1.0},
    "patient_age": {"patient_age": 40.0},
}


@dataclass
class TrainedModel:
    version: str
    estimator: CalibratedClassifierCV
    auc: float
    brier: float
    n_train: int
    n_holdout: int
    denial_rate: float
    importance: dict[str, float] = field(default_factory=dict)
    trained_at: datetime = field(default_factory=lambda: datetime.now(timezone.utc))

    def predict(self, frame: pd.DataFrame) -> np.ndarray:
        return self.estimator.predict_proba(frame[FEATURES])[:, 1]

    def score(self, claim: dict) -> tuple[float, list[dict]]:
        row = to_row(claim)
        base = pd.DataFrame([row], columns=FEATURES)
        probability = float(self.predict(base)[0])

        variants = []
        for factor in FACTORS:
            neutral = dict(row)
            neutral.update(NEUTRAL[factor.key])
            if neutral == row:
                continue
            variants.append((factor.key, neutral))

        factors: list[dict] = []
        if variants:
            frame = pd.DataFrame([v for _, v in variants], columns=FEATURES)
            neutral_probs = self.predict(frame)
            for (key, _), p in zip(variants, neutral_probs):
                impact = probability - float(p)
                if impact > 0.01:
                    factors.append({"factor": key, "label": describe(key, row, claim.get("payerId", "")), "impact": round(impact, 4)})
        factors.sort(key=lambda f: (-f["impact"], f["factor"]))
        return probability, factors[:3]


def train(claims: list[dict], labels: list[bool], version: str | None = None) -> TrainedModel:
    if len(claims) != len(labels):
        raise ValueError("claims and labels differ in length")
    frame = to_frame(claims)
    y = np.asarray(labels, dtype=int)
    if y.sum() < 20 or (len(y) - y.sum()) < 20:
        raise ValueError("Need at least 20 denied and 20 paid claims to train")

    x_train, x_test, y_train, y_test = train_test_split(frame, y, test_size=0.2, stratify=y, random_state=RANDOM_STATE)
    booster = GradientBoostingClassifier(n_estimators=150, max_depth=3, learning_rate=0.08, subsample=0.9,
                                         random_state=RANDOM_STATE)
    model = CalibratedClassifierCV(booster, method="isotonic", cv=3)
    model.fit(x_train, y_train)

    probs = model.predict_proba(x_test)[:, 1]
    importance = permutation_importance(model, x_test, y_test, scoring="roc_auc", n_repeats=5, random_state=RANDOM_STATE)
    return TrainedModel(
        version=version or datetime.now(timezone.utc).strftime("dp-%Y%m%d-%H%M%S"),
        estimator=model,
        auc=round(float(roc_auc_score(y_test, probs)), 4),
        brier=round(float(brier_score_loss(y_test, probs)), 4),
        n_train=len(x_train),
        n_holdout=len(x_test),
        denial_rate=round(float(y.mean()), 4),
        importance={name: round(float(v), 4) for name, v in zip(FEATURES, importance.importances_mean)},
    )


def risk_band(probability: float) -> str:
    if probability >= 0.5:
        return "high"
    if probability >= 0.2:
        return "medium"
    return "low"


def save(model: TrainedModel, directory: str) -> str:
    os.makedirs(directory, exist_ok=True)
    path = os.path.join(directory, f"{model.version}.joblib")
    joblib.dump(model, path)
    return path


def load(path: str) -> TrainedModel:
    return joblib.load(path)
