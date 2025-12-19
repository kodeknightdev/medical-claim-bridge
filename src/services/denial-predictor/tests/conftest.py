import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[4]
# the synthetic history generator lives with the seeder tool
sys.path.insert(0, str(ROOT / "tools" / "seed"))


@pytest.fixture(scope="session")
def history():
    from claimbridge_seed.generator import GeneratorConfig, generate_history, generate_members

    cfg = GeneratorConfig(seed=42)
    return generate_history(cfg, generate_members(cfg))


@pytest.fixture(scope="session")
def labelled(history):
    from app.seed import history_to_features

    rows = [c for c in history if c["deniedInitially"] is not None]
    return [history_to_features(c) for c in rows], [bool(c["deniedInitially"]) for c in rows]


@pytest.fixture(scope="session")
def trained(labelled):
    from app.model import train

    return train(*labelled, version="test-model")


@pytest.fixture()
def client(tmp_path, monkeypatch):
    monkeypatch.setenv("DATABASE_URL", f"sqlite:///{tmp_path / 'dp.db'}")
    monkeypatch.setenv("MODEL_DIR", str(tmp_path / "models"))
    monkeypatch.setenv("AUTH_DISABLED", "true")
    monkeypatch.setenv("KAFKA_BOOTSTRAP_SERVERS", "")

    from app import config, db
    from app.registry import registry

    config.get_settings.cache_clear()
    db.reset_engine()
    db.Base.metadata.create_all(db.engine())
    registry._active = None

    from fastapi.testclient import TestClient
    from app.main import app

    with TestClient(app) as c:
        yield c
    db.reset_engine()
    config.get_settings.cache_clear()
