# denial-predictor

FastAPI service that scores the risk of a claim being denied. Demo model on synthetic data.

- Gradient boosting with isotonic calibration (scikit-learn); holdout AUC about 0.81 on the seeded history
- Top three reasons per score from a counterfactual per factor group
- Model versions stored as joblib files and recorded with their metrics; training via `POST /api/models/train` or
  the weekly CronJob
- Every prediction logged with the model version

| | |
|---|---|
| Database | `cb_denial_predictor` (training claims, model versions, prediction log), Alembic migrations |
| Port | 8000 in the container, 5108 on the host |
| Publishes | `denial-predictor.model-trained` |

```bash
python3.12 -m venv .venv && .venv/bin/pip install -r requirements-dev.txt && .venv/bin/pytest
```
