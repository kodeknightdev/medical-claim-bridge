"""Keeps the active model in memory and records every trained version in the database."""
from __future__ import annotations

import logging
import os
import threading

from sqlalchemy import select, update
from sqlalchemy.orm import Session

from . import model as ml
from .db import ModelVersion, TrainingClaim

log = logging.getLogger(__name__)


class ModelRegistry:
    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._active: ml.TrainedModel | None = None

    @property
    def active(self) -> ml.TrainedModel | None:
        return self._active

    def load_active(self, session: Session) -> ml.TrainedModel | None:
        row = session.scalars(select(ModelVersion).where(ModelVersion.active.is_(True))).first()
        if row is None or not os.path.exists(row.path):
            return None
        with self._lock:
            self._active = ml.load(row.path)
        log.info("Loaded model %s (holdout AUC %.3f)", row.version, row.auc)
        return self._active

    def train_from_db(self, session: Session, model_dir: str, min_rows: int) -> tuple[ml.TrainedModel, ModelVersion]:
        rows = session.scalars(select(TrainingClaim)).all()
        if len(rows) < min_rows:
            raise ValueError(f"Only {len(rows)} training claims available, need {min_rows}")
        trained = ml.train([r.features for r in rows], [r.denied for r in rows])
        path = ml.save(trained, model_dir)

        session.execute(update(ModelVersion).values(active=False))
        version = ModelVersion(version=trained.version, trained_at=trained.trained_at, auc=trained.auc, brier=trained.brier,
                               n_train=trained.n_train, n_holdout=trained.n_holdout, denial_rate=trained.denial_rate,
                               importance=trained.importance, path=path, active=True)
        session.add(version)
        session.commit()
        with self._lock:
            self._active = trained
        log.info("Trained %s on %d claims, holdout AUC %.3f", trained.version, len(rows), trained.auc)
        return trained, version


registry = ModelRegistry()
