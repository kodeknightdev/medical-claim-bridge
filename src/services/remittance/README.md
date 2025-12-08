# remittance

Turns 835 files into postings and keeps accounts receivable.

- Reads each 835 by reference, posts every claim that matches and balances, parks the rest for reconciliation
- Classifies payment, partial payment, denial and reversal; maps CARCs to denial categories
- A/R balance per claim, ageing buckets, days in A/R, nightly snapshots
- Remittance PDFs with QuestPDF

## Endpoints

| Method | Path | What |
|---|---|---|
| `GET` | `/api/remittances`, `/api/remittances/{id}`, `/api/remittances/{id}/pdf`, `/api/remittances/by-claim/{claimId}` | remittances and postings |
| `GET` | `/api/reason-codes` | CARC/RARC explanations |
| `GET`/`POST` | `/api/reconciliation`, `/api/reconciliation/{id}/match`, `/api/reconciliation/{id}/dismiss` | unmatched lines (staff) |
| `GET` | `/api/ar/summary`, `/api/ar/open-claims`, `/api/ar/snapshots` | A/R and ageing |
| `POST` | `/internal/jobs/ar-snapshot` | for the Kubernetes CronJob |

## Events

| Publishes | Consumes |
|---|---|
| `remittance.payment-posted`, `remittance.claim-denied`, `remittance.reversal-posted` | `payers.remittance-received`, `claims.status-changed` |

| | |
|---|---|
| Database | `cb_remittance` |
| Port | 5106 on the host |
| Tests | `tests/ClaimBridge.Remittance.Tests` |

## Run it alone

Start the infrastructure from Docker Compose, then the service (`Development` settings point at the host ports):

```bash
docker compose up -d sqlserver-init kafka-init redis azurite
dotnet run --project src/services/remittance/ClaimBridge.Remittance
```

It listens on http://localhost:5106; the OpenAPI reference is at http://localhost:5106/scalar/v1.
