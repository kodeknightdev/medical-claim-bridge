# analytics

FastAPI service behind the dashboard.

- Builds claim facts from events (idempotent on event id)
- KPIs, denial trends by category and payer, ageing, top scrub edits, payer health samples, model history
- All endpoints scoped to the caller's organisation unless the caller is staff

## Endpoints

| Method | Path | What |
|---|---|---|
| `GET` | `/api/analytics/kpis`, `/denials`, `/ageing`, `/scrub-edits`, `/trends`, `/payer-health`, `/model` | dashboard data |
| `POST` | `/internal/seed/history` | for the seed tool |

## Events

| Publishes | Consumes |
|---|---|
| none | `claims.claim-scrubbed`, `claims.status-changed`, `remittance.payment-posted`, `remittance.claim-denied`, `remittance.reversal-posted`, `denial-predictor.model-trained` |

| | |
|---|---|
| Database | `cb_analytics`, Alembic migrations |
| Port | 8000 in the container, 5110 on the host |

## Run it alone

Without `DATABASE_URL` it uses a local SQLite file; Kafka is optional.

```bash
cd src/services/analytics
python3.12 -m venv .venv && .venv/bin/pip install -r requirements-dev.txt
KAFKA_BOOTSTRAP_SERVERS=localhost:9094 .venv/bin/uvicorn app.main:app --port 5110 --reload
.venv/bin/pytest
```

Against the Compose SQL Server instead, set
`DATABASE_URL=mssql+pyodbc://analytics_svc:<SERVICE_DB_PASSWORD>@localhost:1433/cb_analytics?driver=ODBC+Driver+18+for+SQL+Server&TrustServerCertificate=yes`.
