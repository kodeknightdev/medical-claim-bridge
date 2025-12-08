# analytics

FastAPI service behind the dashboard.

- Builds claim facts from `claims.*` and `remittance.*` events (idempotent on event id)
- KPIs, denial trends by category and payer, ageing, top scrub edits, payer health samples, model history
- All endpoints scoped to the caller's organisation unless the caller is staff

| | |
|---|---|
| Database | `cb_analytics`, Alembic migrations |
| Port | 8000 in the container, 5110 on the host |
| Consumes | `claims.claim-scrubbed`, `claims.status-changed`, `remittance.*`, `denial-predictor.model-trained` |

```bash
python3.12 -m venv .venv && .venv/bin/pip install -r requirements-dev.txt && .venv/bin/pytest
```
