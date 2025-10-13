from __future__ import annotations

import logging
from contextlib import asynccontextmanager

from fastapi import Depends, FastAPI, HTTPException
from pydantic import BaseModel, Field
from sqlalchemy import select, text
from sqlalchemy.orm import Session

from . import events
from .auth import Principal, require_admin, require_read, require_staff
from .config import get_settings
from .db import ModelVersion, PredictionLog, TrainingClaim, get_session
from .model import risk_band
from .registry import registry
from .seed import import_history, read_history_blob
from .telemetry import setup_telemetry

log = logging.getLogger("denial-predictor")
DISCLAIMER = "Demo model trained on synthetic data. Not for real billing decisions."


class ScoreRequest(BaseModel):
    claimId: str | None = None
    orgId: str | None = None
    payerId: str
    procedureCodes: list[str] = Field(min_length=1)
    modifiers: list[str] = []
    lineCharges: list[float] = Field(min_length=1)
    units: list[float] = []
    placeOfService: str = "11"
    diagnosisCount: int = 1
    diagnoses: list[str] = []
    hasPriorAuth: bool = False
    frequencyCode: str = "1"
    daysToSubmit: int = 0
    patientAge: int = 40


class Factor(BaseModel):
    factor: str
    label: str
    impact: float


class ScoreResponse(BaseModel):
    probability: float
    riskBand: str
    modelVersion: str
    factors: list[Factor]
    demo: bool = True
    disclaimer: str = DISCLAIMER


class SeedRequest(BaseModel):
    container: str | None = None
    name: str | None = None
    claims: list[dict] | None = None


@asynccontextmanager
async def lifespan(app: FastAPI):
    try:
        session = next(get_session())
        registry.load_active(session)
        session.close()
    except Exception as exc:  # the service still answers health checks without a model
        log.warning("No model loaded at startup: %s", exc)
    yield


app = FastAPI(title="claim-bridge denial-predictor", version="1.0.0", lifespan=lifespan)
setup_telemetry(app)


@app.get("/health")
def health(session: Session = Depends(get_session)):
    session.execute(text("SELECT 1"))
    return {"status": "Healthy", "model": registry.active.version if registry.active else None}


@app.get("/alive")
def alive():
    return {"status": "Healthy"}


def _score(request: ScoreRequest, session: Session) -> ScoreResponse:
    model = registry.active
    if model is None:
        raise HTTPException(503, "No trained model yet. Train one with POST /api/models/train.")
    claim = request.model_dump()
    if not claim["units"]:
        claim["units"] = [1.0] * len(request.procedureCodes)
    probability, factors = model.score(claim)
    session.add(PredictionLog(claim_id=request.claimId, org_id=request.orgId, payer_id=request.payerId,
                              model_version=model.version, probability=probability, factors=factors))
    session.commit()
    return ScoreResponse(probability=round(probability, 4), riskBand=risk_band(probability), modelVersion=model.version,
                         factors=[Factor(**f) for f in factors])


@app.post("/internal/score", response_model=ScoreResponse, tags=["internal"])
def score_internal(request: ScoreRequest, session: Session = Depends(get_session)):
    return _score(request, session)


@app.post("/api/risk/score", response_model=ScoreResponse)
def score(request: ScoreRequest, session: Session = Depends(get_session), principal: Principal = Depends(require_read)):
    return _score(request, session)


@app.get("/api/risk/claims/{claim_id}")
def score_for_claim(claim_id: str, session: Session = Depends(get_session), principal: Principal = Depends(require_read)):
    row = session.scalars(select(PredictionLog).where(PredictionLog.claim_id == claim_id).order_by(PredictionLog.id.desc())).first()
    if row is None or (not principal.is_staff and row.org_id != principal.org_id):
        raise HTTPException(404, "No score recorded for this claim")
    return {"claimId": row.claim_id, "probability": row.probability, "riskBand": risk_band(row.probability),
            "modelVersion": row.model_version, "factors": row.factors, "scoredAt": row.created_at, "demo": True}


@app.get("/api/models")
def models(session: Session = Depends(get_session), principal: Principal = Depends(require_staff)):
    rows = session.scalars(select(ModelVersion).order_by(ModelVersion.id.desc())).all()
    return [{"version": r.version, "trainedAt": r.trained_at, "auc": r.auc, "brier": r.brier, "nTrain": r.n_train,
             "nHoldout": r.n_holdout, "denialRate": r.denial_rate, "active": r.active, "importance": r.importance} for r in rows]


def _train(session: Session) -> dict:
    settings = get_settings()
    trained, version = registry.train_from_db(session, settings.model_dir, settings.min_training_rows)
    result = {"version": trained.version, "auc": trained.auc, "brier": trained.brier, "nTrain": trained.n_train,
              "nHoldout": trained.n_holdout, "denialRate": trained.denial_rate,
              "topFactors": sorted(trained.importance.items(), key=lambda kv: -kv[1])[:5]}
    events.publish("denial-predictor.model-trained", trained.version, "denial-predictor.model-trained",
                   {"version": trained.version, "auc": trained.auc, "brier": trained.brier, "nTrain": trained.n_train,
                    "trainedAt": trained.trained_at.isoformat(), "denialRate": trained.denial_rate})
    return result


@app.post("/api/models/train")
def train(session: Session = Depends(get_session), principal: Principal = Depends(require_admin)):
    try:
        return _train(session)
    except ValueError as exc:
        raise HTTPException(422, str(exc)) from exc


@app.post("/internal/train", tags=["internal"])
def train_internal(session: Session = Depends(get_session)):
    try:
        return _train(session)
    except ValueError as exc:
        raise HTTPException(422, str(exc)) from exc


@app.post("/internal/seed/history", tags=["internal"])
def seed_history(request: SeedRequest, session: Session = Depends(get_session)):
    if request.claims is not None:
        records = request.claims
    elif request.container and request.name:
        records = read_history_blob(get_settings().blob_connection_string, request.container, request.name)
    else:
        raise HTTPException(400, "Provide claims or a blob reference")
    added = import_history(session, records)
    total = session.query(TrainingClaim).count()
    return {"imported": added, "trainingClaims": total}
