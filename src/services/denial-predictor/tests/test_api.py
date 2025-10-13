def test_health_without_model(client):
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json()["model"] is None


def test_score_before_training_is_503(client):
    response = client.post("/internal/score", json={"payerId": "PAYER-A", "procedureCodes": ["99213"], "lineCharges": [120]})
    assert response.status_code == 503


def test_seed_train_and_score(client, history):
    seeded = client.post("/internal/seed/history", json={"claims": history[:2500]})
    assert seeded.status_code == 200
    assert seeded.json()["imported"] > 2000

    trained = client.post("/internal/train")
    assert trained.status_code == 200, trained.text
    assert trained.json()["auc"] >= 0.65

    scored = client.post("/api/risk/score", json={
        "claimId": "6f1c0a8e-0000-0000-0000-000000000001", "payerId": "PAYER-A", "procedureCodes": ["73721"],
        "lineCharges": [1350], "units": [1], "hasPriorAuth": False, "diagnosisCount": 1, "daysToSubmit": 2,
    })
    body = scored.json()
    assert scored.status_code == 200
    assert body["demo"] is True and "synthetic" in body["disclaimer"]
    assert 0 <= body["probability"] <= 1
    assert body["riskBand"] in ("low", "medium", "high")

    logged = client.get("/api/risk/claims/6f1c0a8e-0000-0000-0000-000000000001")
    assert logged.status_code == 200
    assert logged.json()["modelVersion"] == body["modelVersion"]

    models = client.get("/api/models").json()
    assert len(models) == 1 and models[0]["active"] is True


def test_seed_is_idempotent(client, history):
    first = client.post("/internal/seed/history", json={"claims": history[:300]}).json()
    second = client.post("/internal/seed/history", json={"claims": history[:300]}).json()
    assert second["imported"] == 0
    assert second["trainingClaims"] == first["trainingClaims"]


def test_score_validation(client):
    response = client.post("/internal/score", json={"payerId": "PAYER-A", "procedureCodes": [], "lineCharges": []})
    assert response.status_code == 422
