import numpy as np

from app.features import to_frame
from app.model import risk_band, train

MRI_WITHOUT_AUTH = {
    "payerId": "PAYER-A", "procedureCodes": ["99204", "73721"], "modifiers": [], "lineCharges": [250, 1350],
    "units": [1, 1], "placeOfService": "11", "diagnosisCount": 2, "hasPriorAuth": False, "frequencyCode": "1",
    "daysToSubmit": 3, "patientAge": 47,
}

CLEAN_VISIT = {
    "payerId": "PAYER-A", "procedureCodes": ["99213"], "modifiers": [], "lineCharges": [120], "units": [1],
    "placeOfService": "11", "diagnosisCount": 1, "hasPriorAuth": False, "frequencyCode": "1", "daysToSubmit": 2,
    "patientAge": 40,
}


def test_holdout_auc_meets_minimum(trained):
    assert trained.auc >= 0.70, f"holdout AUC {trained.auc} below 0.70"


def test_probabilities_are_calibrated_into_unit_range(trained, labelled):
    claims, labels = labelled
    probs = trained.predict(to_frame(claims))
    assert np.all((probs >= 0) & (probs <= 1))
    # isotonic calibration should keep the average close to the observed denial rate
    assert abs(probs.mean() - np.mean(labels)) < 0.03


def test_high_risk_claim_explains_missing_prior_auth_first(trained):
    probability, factors = trained.score(MRI_WITHOUT_AUTH)
    assert probability > trained.score(CLEAN_VISIT)[0]
    assert factors and factors[0]["factor"] == "missing_prior_auth"
    assert all(f["impact"] > 0 for f in factors)


def test_factor_contributions_are_stable(trained, labelled):
    first = trained.score(MRI_WITHOUT_AUTH)
    second = trained.score(MRI_WITHOUT_AUTH)
    assert first == second

    retrained = train(*labelled, version="test-model-again")
    assert retrained.score(MRI_WITHOUT_AUTH)[1][0]["factor"] == first[1][0]["factor"]


def test_clean_claim_has_no_large_factors(trained):
    probability, factors = trained.score(CLEAN_VISIT)
    assert risk_band(probability) == "low"
    assert all(f["impact"] < 0.1 for f in factors)


def test_training_refuses_one_class_data(labelled):
    claims, _ = labelled
    try:
        train(claims[:200], [False] * 200)
    except ValueError as exc:
        assert "20 denied" in str(exc)
    else:
        raise AssertionError("expected ValueError")


def test_permutation_importance_finds_the_planted_signals(trained):
    # prior auth denials are rare, so they rank below the common drivers but must still carry weight
    top = [name for name, _ in sorted(trained.importance.items(), key=lambda kv: -kv[1])[:5]]
    assert {"mod_25", "days_to_submit", "charge_ratio"} <= set(top)
    assert trained.importance["missing_prior_auth"] > 0
