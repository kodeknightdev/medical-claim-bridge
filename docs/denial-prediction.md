# Denial prediction

The denial predictor gives every claim a probability that the payer will deny it, before it is sent, and names the
two or three things that push the risk up. It is a demo model trained on synthetic history: it shows how a model fits
into the claim flow, not how well one could predict real denials. The portal labels it that way wherever a score is
shown.

## Where the score is used

- **While typing.** The scrub preview (`POST /api/claims/scrub-preview`) asks for a score alongside the scrub and
  eligibility checks; the claim form shows it as a low/medium/high badge with the factors.
- **Final scrub.** [`ScrubStepHandler`](../src/services/claims/ClaimBridge.Claims/Lifecycle/ScrubStepHandler.cs) scores
  the claim and stores the probability on it; `claims.claim-scrubbed` carries it to analytics.
- **On request.** `GET /api/risk/claims/{claimId}` returns the latest stored prediction with its factors.

Claims calls `POST /internal/score` through
[`RiskClient`](../src/services/claims/ClaimBridge.Claims/Application/Clients/RiskClient.cs) with a two second
timeout. If the predictor is down, slow or has no model, the client returns nothing and the claim carries on without
a score. A missing prediction never blocks a claim.

## Features

Features are computed in [`features.py`](../src/services/denial-predictor/app/features.py) from a de-identified
summary of the claim (codes, counts, amounts, ages and day counts; no names, member ids or dates):

| Feature | Meaning |
|---|---|
| `payer_a`, `payer_b`, `payer_c` | one-hot payer |
| `mod_25`, `mod_59` | modifier 25 or 59 on any line |
| `missing_prior_auth` | a procedure that usually needs prior authorisation and no authorisation number |
| `charge_ratio` | total charge divided by the typical charge for the same codes and units |
| `max_units` | highest units on one line |
| `line_count`, `dx_count` | number of lines and diagnoses |
| `days_to_submit` | days from the first date of service to submission |
| `telehealth` | place of service 02 or 10 |
| `is_correction` | frequency code 7 |
| `patient_age` | age in years at submission |

The same function builds rows for training and for scoring, so the two can never drift apart.

## Model

[`model.py`](../src/services/denial-predictor/app/model.py) trains a scikit-learn `GradientBoostingClassifier`
(150 trees, depth 3) wrapped in `CalibratedClassifierCV` with isotonic calibration, so a score of 0.3 means roughly
three in ten such claims were denied. Training holds out a stratified 20 % and records:

| Metric | Value on the seeded history |
|---|---|
| Training claims | 4,591 (labelled from 5,000 generated claims) |
| Holdout ROC AUC | 0.81 |
| Brier score | 0.09 |
| Base denial rate | about 13 % |

The numbers reflect the patterns the seed generator plants (missing authorisations, modifier 59 with Bluepeak, late
filing, inflated charges), plus noise. Bands are `low` below 0.2, `medium` up to 0.5 and `high` above.

## Explanations

A probability alone does not tell a biller what to fix. For each factor group (prior auth, modifier 25, modifier 59,
high charge, late filing, units, payer, thin diagnosis, patient age), the scorer resets the claim's values to a
neutral baseline, scores again, and reports the drop in probability as that factor's impact. Factors with an impact
above one percentage point are sorted and the top three come back with a plain sentence, for example
"Procedure usually needs prior authorisation and none is on the claim". This is a simple counterfactual, cheap enough
to run on every keystroke-debounced preview, and easier to read than raw feature weights. Permutation importance for
the whole model is stored with each version for the model page.

## Training and versions

- Training data lives in `training_claims` (features and the label "denied on first submission"). The seed tool
  generates the history and posts it to `/internal/seed/history`; in a real system the label would come from the
  `remittance.claim-denied` and `payment-posted` events.
- `POST /api/models/train` (admin) or `POST /internal/train` trains a new version from the table; at least 500 rows
  and 20 of each class are required.
- Each version is saved as a joblib file under `MODEL_DIR` and recorded in `model_versions` with its metrics; the new
  version becomes active immediately ([`registry.py`](../src/services/denial-predictor/app/registry.py)). On start the
  service loads the active version, and still serves health checks if there is none.
- A trained version publishes `denial-predictor.model-trained`; analytics keeps the history for the model card.
- In Kubernetes a weekly CronJob (Sunday 03:00) calls `/internal/train`; the chart mounts a persistent volume for
  the model files.

Every score is written to `prediction_log` with the model version, so a later audit can show which model said what
about a claim.

## Limits

- Synthetic data: the model learns the generator's rules, so its AUC says nothing about real-world performance.
- No drift monitoring beyond comparing each new version's AUC and denial rate on the model page.
- Isotonic calibration on a few thousand rows produces stepped probabilities; that is acceptable for three bands.
