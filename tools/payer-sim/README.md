# payer-sim

Three simulated payers for development and tests. Never deployed to production.

- `/payer-a/v1/*`: Northwind JSON API; sends adjudication callbacks and later an 835
- Bluepeak: watches `payer-b/inbound/` for 837P files, answers with 999 and 277CA files (in either order) and an 835
- `/payer-c/api/*`: Cascade, slow and failing on purpose
- `/sim/*`: control endpoints for scenarios (denial rate, latency, outage, pend next, reject next file) and members

Adjudication is deterministic: allowed amounts from a share of the charge, deductible then copay or coinsurance,
a few payer-specific denials, and a configurable random denial rate keyed on the claim number.
