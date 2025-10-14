import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(ROOT / "tools" / "seed"))

SUNRISE = "a1000000-0000-0000-0000-000000000001"
LAKESIDE = "a1000000-0000-0000-0000-000000000002"


@pytest.fixture(scope="session")
def history():
    from claimbridge_seed.generator import GeneratorConfig, generate_history, generate_members

    cfg = GeneratorConfig(seed=7, claims=1200)
    return generate_history(cfg, generate_members(cfg))


@pytest.fixture()
def session(tmp_path, monkeypatch):
    monkeypatch.setenv("DATABASE_URL", f"sqlite:///{tmp_path / 'analytics.db'}")
    from app import config, db

    config.get_settings.cache_clear()
    db.reset_engine()
    db.Base.metadata.create_all(db.engine())
    with db.session_factory()() as s:
        yield s
    db.reset_engine()


@pytest.fixture()
def client(session, monkeypatch):
    monkeypatch.setenv("AUTH_DISABLED", "true")
    monkeypatch.setenv("BACKGROUND_WORKERS", "false")
    from app import config

    config.get_settings.cache_clear()
    from fastapi.testclient import TestClient
    from app.main import app

    with TestClient(app) as c:
        yield c
