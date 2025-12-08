# denial-predictor

FastAPI service that scores the risk of a claim being denied. Demo model on synthetic data.

- Gradient boosting with isotonic calibration (scikit-learn); holdout AUC about 0.81 on the seeded history
- Top three reasons per score from a counterfactual per factor group
- Model versions stored as joblib files and recorded with their metrics
- Every prediction logged with the model version

## Endpoints

| Method | Path | What |
|---|---|---|
| `POST` | `/api/risk/score` | score an unsaved claim summary |
| `GET` | `/api/risk/claims/{claimId}` | latest prediction for a claim |
| `GET`/`POST` | `/api/models`, `/api/models/train` | model versions (staff), training (admin) |
| `POST` | `/internal/score`, `/internal/train`, `/internal/seed/history` | for claims, the CronJob and the seed tool |

## Events

| Publishes | Consumes |
|---|---|
| `denial-predictor.model-trained` | none |

| | |
|---|---|
| Database | `cb_denial_predictor` (training claims, model versions, prediction log), Alembic migrations |
| Port | 8000 in the container, 5108 on the host |

## Run it alone

Without `DATABASE_URL` it uses a local SQLite file; Kafka is optional.

```bash
cd src/services/denial-predictor
python3.12 -m venv .venv && .venv/bin/pip install -r requirements-dev.txt
KAFKA_BOOTSTRAP_SERVERS=localhost:9094 .venv/bin/uvicorn app.main:app --port 5108 --reload
.venv/bin/pytest
```

Against the Compose SQL Server instead, set
`DATABASE_URL=mssql+pyodbc://denial_predictor_svc:<SERVICE_DB_PASSWORD>@localhost:1433/cb_denial_predictor?driver=ODBC+Driver+18+for+SQL+Server&TrustServerCertificate=yes`.
