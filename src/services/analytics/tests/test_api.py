from app.auth import Principal, require_read

from .conftest import LAKESIDE, SUNRISE


def test_seeded_history_produces_dashboard_numbers(client, history):
    assert client.post("/internal/seed/history", json={"claims": history}).json()["imported"] == len(history)

    kpis = client.get("/api/analytics/kpis").json()
    assert kpis["claims"] > 1000
    assert 0.05 < kpis["denialRate"] < 0.3
    assert 0.8 < kpis["cleanClaimRate"] <= 1
    assert kpis["daysInAr"] is not None

    trends = client.get("/api/analytics/trends?months=12").json()
    assert 10 <= len(trends) <= 12
    denials = client.get("/api/analytics/denials").json()
    assert {p["payerId"] for p in denials["byPayer"]} == {"PAYER-A", "PAYER-B", "PAYER-C"}
    assert client.get("/api/analytics/scrub-edits").json()


def test_provider_users_only_see_their_org(client, history):
    from app.main import app

    client.post("/internal/seed/history", json={"claims": history})
    everyone = client.get("/api/analytics/kpis").json()["claims"]

    app.dependency_overrides[require_read] = lambda: Principal("u", frozenset({"ProviderBiller"}), SUNRISE, frozenset({"api"}))
    try:
        sunrise = client.get("/api/analytics/kpis").json()["claims"]
        # asking for another org's data is ignored for provider users
        still_sunrise = client.get(f"/api/analytics/kpis?orgId={LAKESIDE}").json()["claims"]
    finally:
        app.dependency_overrides.clear()

    expected = sum(1 for c in history if c["orgId"] == SUNRISE)
    assert sunrise == still_sunrise == expected
    assert sunrise < everyone
