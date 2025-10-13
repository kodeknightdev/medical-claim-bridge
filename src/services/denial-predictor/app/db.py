from collections.abc import Iterator
from datetime import datetime, timezone

from sqlalchemy import JSON, Boolean, DateTime, Float, Integer, String, create_engine
from sqlalchemy.orm import DeclarativeBase, Mapped, Session, mapped_column, sessionmaker

from .config import get_settings


class Base(DeclarativeBase):
    pass


class ModelVersion(Base):
    __tablename__ = "model_versions"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    version: Mapped[str] = mapped_column(String(40), unique=True)
    trained_at: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    auc: Mapped[float] = mapped_column(Float)
    brier: Mapped[float] = mapped_column(Float)
    n_train: Mapped[int] = mapped_column(Integer)
    n_holdout: Mapped[int] = mapped_column(Integer)
    denial_rate: Mapped[float] = mapped_column(Float)
    importance: Mapped[dict] = mapped_column(JSON)
    path: Mapped[str] = mapped_column(String(300))
    active: Mapped[bool] = mapped_column(Boolean, default=False)


class PredictionLog(Base):
    __tablename__ = "prediction_log"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    claim_id: Mapped[str | None] = mapped_column(String(36), nullable=True, index=True)
    org_id: Mapped[str | None] = mapped_column(String(36), nullable=True)
    payer_id: Mapped[str] = mapped_column(String(20))
    model_version: Mapped[str] = mapped_column(String(40))
    probability: Mapped[float] = mapped_column(Float)
    factors: Mapped[list] = mapped_column(JSON)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=lambda: datetime.now(timezone.utc))


class TrainingClaim(Base):
    """Historical claims with the payer's first decision; no names or member ids are stored here."""

    __tablename__ = "training_claims"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    claim_number: Mapped[str] = mapped_column(String(20), unique=True)
    features: Mapped[dict] = mapped_column(JSON)
    denied: Mapped[bool] = mapped_column(Boolean)
    carc: Mapped[str | None] = mapped_column(String(5), nullable=True)
    adjudicated_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)


_engine = None
_session_factory: sessionmaker | None = None


def engine():
    global _engine, _session_factory
    if _engine is None:
        url = get_settings().database_url
        kwargs = {"pool_pre_ping": True}
        if url.startswith("sqlite"):
            kwargs["connect_args"] = {"check_same_thread": False}
        _engine = create_engine(url, **kwargs)
        _session_factory = sessionmaker(bind=_engine, expire_on_commit=False)
    return _engine


def reset_engine() -> None:
    global _engine, _session_factory
    _engine = None
    _session_factory = None


def get_session() -> Iterator[Session]:
    engine()
    assert _session_factory is not None
    with _session_factory() as session:
        yield session
