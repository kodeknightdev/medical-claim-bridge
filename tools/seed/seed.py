"""One-shot seeder: generates synthetic members and claim history, drops them in blob storage, asks each service
to import its slice, and trains the first denial model. Safe to run more than once."""
from __future__ import annotations

import gzip
import io
import json
import logging
import os
import sys
import time

import requests
from azure.storage.blob import BlobServiceClient

from claimbridge_seed.generator import GeneratorConfig, generate_history, generate_members

logging.basicConfig(level=logging.INFO, format="[seed] %(message)s")
log = logging.getLogger()

BLOB = os.getenv("BLOB_CONNECTION_STRING", "UseDevelopmentStorage=true")
CONTAINER = "seed"
SERVICES = {
    "claims": os.getenv("CLAIMS_URL", "http://localhost:5103"),
    "remittance": os.getenv("REMITTANCE_URL", "http://localhost:5106"),
    "analytics": os.getenv("ANALYTICS_URL", "http://localhost:5110"),
    "denial-predictor": os.getenv("DENIAL_PREDICTOR_URL", "http://localhost:5108"),
}


def wait_for(name: str, url: str, timeout: int = 600) -> None:
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            if requests.get(f"{url}/alive", timeout=3).ok:
                log.info("%s is up", name)
                return
        except requests.RequestException:
            pass
        time.sleep(5)
    raise SystemExit(f"{name} did not become healthy at {url}")


def upload(client: BlobServiceClient, name: str, data: bytes) -> None:
    container = client.get_container_client(CONTAINER)
    if not container.exists():
        container.create_container()
    container.upload_blob(name, data, overwrite=True)


def main() -> int:
    cfg = GeneratorConfig(seed=int(os.getenv("SEED", "42")), members=int(os.getenv("SEED_MEMBERS", "800")),
                          claims=int(os.getenv("SEED_CLAIMS", "5000")))
    members = generate_members(cfg)
    history = generate_history(cfg, members)
    log.info("generated %d members and %d claims", len(members), len(history))

    client = BlobServiceClient.from_connection_string(BLOB)
    upload(client, "members.json", json.dumps(members).encode())
    buffer = io.BytesIO()
    with gzip.GzipFile(fileobj=buffer, mode="wb") as gz:
        for record in history:
            gz.write((json.dumps(record) + "\n").encode())
    upload(client, "history.jsonl.gz", buffer.getvalue())

    blob_ref = {"container": CONTAINER, "name": "history.jsonl.gz"}
    for name, url in SERVICES.items():
        wait_for(name, url)
        response = requests.post(f"{url}/internal/seed/history", json=blob_ref, timeout=600)
        response.raise_for_status()
        log.info("%s import: %s", name, response.json())

    trained = requests.post(f"{SERVICES['denial-predictor']}/internal/train", timeout=600)
    if trained.ok:
        log.info("denial model trained: version %s, holdout AUC %s", trained.json()["version"], trained.json()["auc"])
    else:
        log.warning("model training failed: %s", trained.text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
