"""Samples payers-gateway's live payer health every few seconds for the ops dashboard history."""
from __future__ import annotations

import logging
import threading
from datetime import datetime, timedelta, timezone

import httpx

from .config import get_settings
from .db import PayerHealthSample, session_factory

log = logging.getLogger(__name__)


def record(samples: list[dict], at: datetime) -> list[PayerHealthSample]:
    rows = []
    for s in samples:
        health = s["health"]
        rows.append(PayerHealthSample(at=at, payer_id=s["payerId"], circuit_state=health["circuitState"], requests=health["requests"],
                                      error_rate=health["errorRate"], p50_ms=health.get("p50Ms"), p95_ms=health.get("p95Ms"),
                                      queued=s.get("queued", 0), deferred=s.get("deferred", 0)))
    return rows


class PayerHealthPoller(threading.Thread):
    def __init__(self) -> None:
        super().__init__(name="payer-health-poller", daemon=True)
        self._stop = threading.Event()

    def stop(self) -> None:
        self._stop.set()

    def run(self) -> None:
        settings = get_settings()
        url = settings.payers_gateway_url.rstrip("/") + "/api/payers/health"
        while not self._stop.wait(settings.payer_health_poll_seconds):
            try:
                samples = httpx.get(url, timeout=5).json()
                now = datetime.now(timezone.utc)
                with session_factory()() as session:
                    session.add_all(record(samples, now))
                    # a week of samples is plenty for the dashboard
                    session.query(PayerHealthSample).filter(PayerHealthSample.at < now - timedelta(days=7)).delete()
                    session.commit()
            except Exception as exc:  # noqa: BLE001
                log.info("Payer health poll failed: %s", exc)
