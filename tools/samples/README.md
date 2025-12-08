# samples

Example inputs, all synthetic.

| File | What |
|---|---|
| `claim.json` | one claim for the submission API; replace `__DOS__` with a recent date |
| `claims-batch.csv` | batch upload in the CSV template format |
| `payer-b-batch.837.x12` | two professional claims for Bluepeak |
| `ack.999.x12`, `ack-rejected.999.x12` | accepted and rejected implementation acknowledgements |
| `claim-ack.277ca.x12` | claim-level acknowledgement |
| `remittance.835.x12` | remittance with a payment and adjustments |
| `malformed.835.x12` | broken envelope, for error handling |
