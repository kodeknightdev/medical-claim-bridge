from __future__ import annotations

import logging
from contextlib import asynccontextmanager
from datetime import datetime, timedelta, timezone

import pandas as pd
from fastapi import Depends, FastAPI, HTTPException, Query
from pydantic import BaseModel
from sqlalchemy import select, text
from sqlalchemy.orm import Session

from . import kpis
from .auth import Principal, require_read, require_staff
from .config import get_settings
from .consumer import AnalyticsConsumer
from .db import ClaimFact, ModelEvent, PayerHealthSample, ScrubEditFact, get_session
from .payer_health import PayerHealthPoller
from .seed import import_history, read_history_blob
from .telemetry import setup_telemetry

log = logging.getLogger("analytics")


@asynccontextmanager
async def lifespan(app: FastAPI):
    settings = get_settings()
    workers = []
    if settings.background_workers:
        if settings.kafka_bootstrap_servers:
            workers.append(AnalyticsConsumer())
        workers.append(PayerHealthPoller())
        for w in workers:
            w.start()
    yield
    for w in workers:
        w.stop()


app = FastAPI(title="claim-bridge analytics", version="1.0.0", lifespan=lifespan)
setup_telemetry(app)


class SeedRequest(BaseModel):
    container: str | None = None
    name: str | None = None
    claims: list[dict] | None = None


def _facts(session: Session, principal: Principal, org_id: str | None, since: datetime | None = None,
           until: datetime | None = None) -> pd.DataFrame:
    query = select(ClaimFact)
    org = principal.org_filter(org_id)
    if org is not None:
        query = query.where(ClaimFact.org_id == org)
    if since is not None:
        query = query.where(ClaimFact.submitted_at >= since)
    if until is not None:
        query = query.where(ClaimFact.submitted_at <= until)
    rows = session.scalars(query).all()
    columns = [c.name for c in ClaimFact.__table__.columns]
    frame = pd.DataFrame([{c: getattr(r, c) for c in columns} for r in rows], columns=columns)
    for col in ("created_at", "submitted_at", "acked_at", "adjudicated_at", "paid_at"):
        frame[col] = pd.to_datetime(frame[col], utc=True)
    for col in ("total_charge", "paid_amount", "allowed_amount", "patient_responsibility", "adjustment_amount"):
        frame[col] = frame[col].astype(float)
    frame["denied"] = frame["denied"].astype(bool)
    return frame


def _edits(session: Session, principal: Principal, org_id: str | None, since: datetime | None = None) -> pd.DataFrame:
    query = select(ScrubEditFact.rule_code, ScrubEditFact.severity, ScrubEditFact.payer_id)
    org = principal.org_filter(org_id)
    if org is not None:
        query = query.where(ScrubEditFact.org_id == org)
    if since is not None:
        query = query.where(ScrubEditFact.at >= since)
    return pd.DataFrame(session.execute(query).all(), columns=["rule_code", "severity", "payer_id"])


@app.get("/health")
def health(session: Session = Depends(get_session)):
    session.execute(text("SELECT 1"))
    return {"status": "Healthy"}


@app.get("/alive")
def alive():
    return {"status": "Healthy"}


@app.get("/api/analytics/kpis")
def kpi_summary(orgId: str | None = None, days: int = Query(365, ge=1, le=730), session: Session = Depends(get_session),
                principal: Principal = Depends(require_read)):
    since = datetime.now(timezone.utc) - timedelta(days=days)
    return kpis.summary(_facts(session, principal, orgId, since), _edits(session, principal, orgId, since))


@app.get("/api/analytics/denials")
def denial_breakdown(orgId: str | None = None, days: int = Query(365, ge=1, le=730), session: Session = Depends(get_session),
                     principal: Principal = Depends(require_read)):
    return kpis.denials(_facts(session, principal, orgId, datetime.now(timezone.utc) - timedelta(days=days)))


@app.get("/api/analytics/ageing")
def ar_ageing(orgId: str | None = None, session: Session = Depends(get_session), principal: Principal = Depends(require_read)):
    return kpis.ageing(_facts(session, principal, orgId))


@app.get("/api/analytics/scrub-edits")
def scrub_edits(orgId: str | None = None, days: int = Query(90, ge=1, le=730), session: Session = Depends(get_session),
                principal: Principal = Depends(require_read)):
    return kpis.top_edits(_edits(session, principal, orgId, datetime.now(timezone.utc) - timedelta(days=days)), 15)


@app.get("/api/analytics/trends")
def monthly_trends(orgId: str | None = None, months: int = Query(12, ge=1, le=24), session: Session = Depends(get_session),
                   principal: Principal = Depends(require_read)):
    return kpis.trends(_facts(session, principal, orgId), months)


@app.get("/api/analytics/payer-health")
def payer_health(minutes: int = Query(60, ge=5, le=1440), session: Session = Depends(get_session),
                 principal: Principal = Depends(require_staff)):
    since = datetime.now(timezone.utc) - timedelta(minutes=minutes)
    rows = session.scalars(select(PayerHealthSample).where(PayerHealthSample.at >= since).order_by(PayerHealthSample.at)).all()
    series: dict[str, list] = {}
    for r in rows:
        series.setdefault(r.payer_id, []).append({"at": r.at, "circuitState": r.circuit_state, "errorRate": r.error_rate,
                                                  "p50Ms": r.p50_ms, "p95Ms": r.p95_ms, "requests": r.requests})
    return {"series": series, "latest": {payer: points[-1] for payer, points in series.items()}}


@app.get("/api/analytics/model")
def model_history(session: Session = Depends(get_session), principal: Principal = Depends(require_staff)):
    rows = session.scalars(select(ModelEvent).order_by(ModelEvent.trained_at.desc())).all()
    return [{"version": r.version, "auc": r.auc, "brier": r.brier, "nTrain": r.n_train, "trainedAt": r.trained_at} for r in rows]


@app.post("/internal/seed/history", tags=["internal"])
def seed_history(request: SeedRequest, session: Session = Depends(get_session)):
    if request.claims is not None:
        records = request.claims
    elif request.container and request.name:
        records = read_history_blob(get_settings().blob_connection_string, request.container, request.name)
    else:
        raise HTTPException(400, "Provide claims or a blob reference")
    return {"imported": import_history(session, records)}
