from collections.abc import Iterator
from datetime import date, datetime

from sqlalchemy import Boolean, Date, DateTime, Float, Integer, String, create_engine
from sqlalchemy.orm import DeclarativeBase, Mapped, Session, mapped_column, sessionmaker

from .config import get_settings


class Base(DeclarativeBase):
    pass


class ClaimFact(Base):
    """One row per claim, folded from claim, scrub and remittance events. No PHI."""

    __tablename__ = "claim_facts"

    claim_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    claim_number: Mapped[str] = mapped_column(String(20), index=True)
    org_id: Mapped[str] = mapped_column(String(36), index=True)
    payer_id: Mapped[str] = mapped_column(String(20), index=True)
    total_charge: Mapped[float] = mapped_column(Float, default=0)
    first_service_date: Mapped[date | None] = mapped_column(Date, nullable=True)
    created_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    submitted_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True, index=True)
    acked_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    accepted_first_pass: Mapped[bool | None] = mapped_column(Boolean, nullable=True)
    scrub_clean_first_pass: Mapped[bool | None] = mapped_column(Boolean, nullable=True)
    status: Mapped[str] = mapped_column(String(20), default="Received")
    adjudicated_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    paid_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    paid_amount: Mapped[float] = mapped_column(Float, default=0)
    allowed_amount: Mapped[float] = mapped_column(Float, default=0)
    patient_responsibility: Mapped[float] = mapped_column(Float, default=0)
    adjustment_amount: Mapped[float] = mapped_column(Float, default=0)
    denied: Mapped[bool] = mapped_column(Boolean, default=False)
    denial_category: Mapped[str | None] = mapped_column(String(30), nullable=True)
    carc: Mapped[str | None] = mapped_column(String(5), nullable=True)
    is_correction: Mapped[bool] = mapped_column(Boolean, default=False)
    reversed_amount: Mapped[float] = mapped_column(Float, default=0)
    denial_risk: Mapped[float | None] = mapped_column(Float, nullable=True)


class ScrubEditFact(Base):
    __tablename__ = "scrub_edit_facts"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    claim_id: Mapped[str] = mapped_column(String(36), index=True)
    org_id: Mapped[str] = mapped_column(String(36))
    payer_id: Mapped[str] = mapped_column(String(20))
    rule_code: Mapped[str] = mapped_column(String(20), index=True)
    severity: Mapped[str] = mapped_column(String(10))
    first_attempt: Mapped[bool] = mapped_column(Boolean)
    at: Mapped[datetime] = mapped_column(DateTime(timezone=True))


class PayerHealthSample(Base):
    __tablename__ = "payer_health_samples"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    at: Mapped[datetime] = mapped_column(DateTime(timezone=True), index=True)
    payer_id: Mapped[str] = mapped_column(String(20))
    circuit_state: Mapped[str] = mapped_column(String(20))
    requests: Mapped[int] = mapped_column(Integer)
    error_rate: Mapped[float] = mapped_column(Float)
    p50_ms: Mapped[float | None] = mapped_column(Float, nullable=True)
    p95_ms: Mapped[float | None] = mapped_column(Float, nullable=True)
    queued: Mapped[int] = mapped_column(Integer, default=0)
    deferred: Mapped[int] = mapped_column(Integer, default=0)


class ModelEvent(Base):
    __tablename__ = "model_events"

    version: Mapped[str] = mapped_column(String(40), primary_key=True)
    auc: Mapped[float] = mapped_column(Float)
    brier: Mapped[float | None] = mapped_column(Float, nullable=True)
    n_train: Mapped[int] = mapped_column(Integer)
    trained_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))


class ProcessedEvent(Base):
    __tablename__ = "processed_events"

    event_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    processed_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))


_engine = None
_factory: sessionmaker | None = None


def engine():
    global _engine, _factory
    if _engine is None:
        url = get_settings().database_url
        kwargs = {"pool_pre_ping": True}
        if url.startswith("sqlite"):
            kwargs["connect_args"] = {"check_same_thread": False}
        _engine = create_engine(url, **kwargs)
        _factory = sessionmaker(bind=_engine, expire_on_commit=False)
    return _engine


def reset_engine() -> None:
    global _engine, _factory
    _engine = None
    _factory = None


def session_factory() -> sessionmaker:
    engine()
    assert _factory is not None
    return _factory


def get_session() -> Iterator[Session]:
    with session_factory()() as session:
        yield session
