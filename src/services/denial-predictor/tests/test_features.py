from app.features import FEATURES, to_frame, to_row


def claim(**overrides):
    base = {
        "payerId": "PAYER-B", "procedureCodes": ["99214", "20610"], "modifiers": ["25"], "lineCharges": [175, 180],
        "units": [1, 1], "placeOfService": "11", "diagnosisCount": 2, "hasPriorAuth": False, "frequencyCode": "1",
        "daysToSubmit": 4, "patientAge": 52,
    }
    base.update(overrides)
    return base


def test_flags_and_payer_one_hot():
    row = to_row(claim())
    assert row["payer_b"] == 1.0 and row["payer_a"] == 0.0 and row["payer_c"] == 0.0
    assert row["mod_25"] == 1.0
    assert row["mod_59"] == 0.0
    assert row["charge_ratio"] == 1.0


def test_missing_prior_auth_only_for_codes_that_need_it():
    assert to_row(claim(procedureCodes=["73721"], lineCharges=[1350], units=[1]))["missing_prior_auth"] == 1.0
    assert to_row(claim(procedureCodes=["73721"], lineCharges=[1350], units=[1], hasPriorAuth=True))["missing_prior_auth"] == 0.0
    assert to_row(claim())["missing_prior_auth"] == 0.0


def test_charge_ratio_uses_typical_charges_and_units():
    row = to_row(claim(procedureCodes=["97110"], lineCharges=[180], units=[2], modifiers=[]))
    assert row["charge_ratio"] == 2.0
    assert row["max_units"] == 2.0


def test_frame_has_stable_column_order():
    frame = to_frame([claim(), claim(payerId="PAYER-A")])
    assert list(frame.columns) == FEATURES
    assert frame.shape == (2, len(FEATURES))
