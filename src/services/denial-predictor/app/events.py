"""Publishes integration events in the same envelope the .NET services use."""
from __future__ import annotations

import json
import logging
import uuid
from datetime import datetime, timezone

from .config import get_settings

log = logging.getLogger(__name__)
_producer = None


def envelope(event_type: str, data: dict, source: str) -> dict:
    return {
        "id": str(uuid.uuid4()),
        "type": event_type,
        "schemaVersion": 1,
        "occurredAt": datetime.now(timezone.utc).isoformat(),
        "source": source,
        "correlationId": None,
        "data": data,
    }


def publish(topic: str, key: str, event_type: str, data: dict) -> None:
    settings = get_settings()
    if not settings.kafka_bootstrap_servers:
        log.info("Kafka not configured; skipping %s", event_type)
        return
    global _producer
    if _producer is None:
        from confluent_kafka import Producer

        _producer = Producer(settings.kafka_config(**{"client.id": settings.service_name, "enable.idempotence": True}))
    body = envelope(event_type, data, settings.service_name)
    _producer.produce(topic, key=key, value=json.dumps(body),
                      headers={"event-id": body["id"], "event-type": event_type})
    _producer.flush(5)
