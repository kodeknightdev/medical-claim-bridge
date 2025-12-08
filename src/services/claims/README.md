# claims

Owns the claim: intake, versions, lifecycle, denials and appeals.

- Intake from the portal, the submission API (idempotency keys) and batch files (CSV or 837P)
- Duplicate detection by business fingerprint
- Process manager driving the lifecycle state machine, with durable ack, inquiry and resubmission deadlines
- Calls the scrubber over gRPC, the denial predictor and payers-gateway eligibility over HTTP
- Patient fields encrypted at rest; every PHI read audited

## Endpoints

| Method | Path | What |
|---|---|---|
| `POST`/`GET` | `/api/v1/claims`, `/api/v1/claims/{id}` | submission API for practice systems (`Idempotency-Key`) |
| `GET`/`POST`/`PUT` | `/api/claims`, `/api/claims/{id}`, `/api/claims/{id}/history` | search, detail, create, edit |
| `POST` | `/api/claims/scrub-preview`, `/api/claims/{id}/correct`, `/api/claims/{id}/void` | inline scrub, frequency 7 and 8 |
| `GET`/`POST` | `/api/batches`, `/api/batches/{id}`, `/api/batches/template` | batch upload |
| `GET` | `/api/denials` | denial worklist |
| `GET`/`POST` | `/api/appeals`, `/api/appeals/{id}/letter`, `/api/appeals/{id}/submit`, `/api/appeals/{id}/outcome` | appeals |
| `GET` | `/internal/claims/{id}/submission`, `/internal/claims/by-number/{number}` | for payers-gateway only |

## Events

| Publishes | Consumes |
|---|---|
| `claims.batch-uploaded`, `claims.claim-received`, `claims.claim-scrubbed`, `claims.claim-ready`, `claims.status-changed` | its own `claims.*` topics, `payers.claim-submitted`, `payers.ack-received`, `payers.status-updated`, `remittance.payment-posted`, `remittance.claim-denied`, `remittance.reversal-posted`, `providers.enrollment-changed` |

| | |
|---|---|
| Database | `cb_claims` (claims, lines, history, lifecycle state, search entries, worklist, appeals, outbox) |
| Port | 5103 on the host |
| Tests | `tests/ClaimBridge.Claims.Tests` |

## Run it alone

Start the infrastructure from Docker Compose, then the service (`Development` settings point at the host ports):

```bash
docker compose up -d sqlserver-init kafka-init redis azurite
dotnet run --project src/services/claims/ClaimBridge.Claims
```

It listens on http://localhost:5103; the OpenAPI reference is at http://localhost:5103/scalar/v1. Scrubbing and submission also need scrubber, providers and payers-gateway running.
