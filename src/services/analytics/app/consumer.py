"""Kafka consumer for the analytics read side. Failures go to <topic>.retry with backoff, then <topic>.dlq."""
from __future__ import annotations

import json
import logging
import threading
import time
from datetime import datetime, timedelta, timezone

from .config import get_settings
from .db import session_factory
from .projections import TOPICS, apply

log = logging.getLogger(__name__)
MAX_ATTEMPTS = 3


def _headers(msg) -> dict[str, str]:
    return {k: v.decode() if isinstance(v, bytes) else v for k, v in (msg.headers() or [])}


class AnalyticsConsumer(threading.Thread):
    def __init__(self) -> None:
        super().__init__(name="analytics-consumer", daemon=True)
        self._stop = threading.Event()

    def stop(self) -> None:
        self._stop.set()

    def run(self) -> None:
        from confluent_kafka import Consumer, Producer

        settings = get_settings()
        conf = {"bootstrap.servers": settings.kafka_bootstrap_servers, "group.id": settings.kafka_group,
                "auto.offset.reset": "earliest", "enable.auto.commit": False}
        consumer = Consumer(conf)
        producer = Producer({"bootstrap.servers": settings.kafka_bootstrap_servers})
        consumer.subscribe(TOPICS + [t + ".retry" for t in TOPICS])
        log.info("Analytics consumer subscribed to %s", TOPICS)

        while not self._stop.is_set():
            msg = consumer.poll(1.0)
            if msg is None:
                continue
            if msg.error():
                log.warning("Kafka error: %s", msg.error())
                continue
            headers = _headers(msg)
            if msg.topic().endswith(".retry") and "x-not-before" in headers:
                wait = (datetime.fromisoformat(headers["x-not-before"]) - datetime.now(timezone.utc)).total_seconds()
                if wait > 0:
                    time.sleep(min(wait, 30))
            try:
                envelope = json.loads(msg.value())
                with session_factory()() as session:
                    apply(session, envelope)
            except Exception as exc:  # noqa: BLE001 - every failure is routed, none is dropped
                self._route_failure(producer, msg, headers, exc)
            consumer.commit(msg, asynchronous=False)
        consumer.close()

    @staticmethod
    def _route_failure(producer, msg, headers: dict[str, str], exc: Exception) -> None:
        base = headers.get("x-original-topic", msg.topic().removesuffix(".retry"))
        attempt = int(headers.get("x-attempt", "0")) + 1
        target = f"{base}.dlq" if attempt >= MAX_ATTEMPTS else f"{base}.retry"
        delay = timedelta(seconds=5 * 2 ** (attempt - 1))
        headers.update({"x-original-topic": base, "x-attempt": str(attempt), "x-error": str(exc)[:500],
                        "x-not-before": (datetime.now(timezone.utc) + delay).isoformat()})
        log.warning("Analytics failed on %s (attempt %d), sending to %s: %s", base, attempt, target, exc)
        producer.produce(target, key=msg.key(), value=msg.value(), headers=list(headers.items()))
        producer.flush(5)
