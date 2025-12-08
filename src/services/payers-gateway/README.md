# payers-gateway

Everything payer-specific lives here, behind one adapter per payer.

- Northwind (`PAYER-A`): JSON submit, adjudication callbacks
- Bluepeak (`PAYER-B`): 837P batch files out; 999, 277CA and 835 files in
- Cascade (`PAYER-C`): slow, flaky JSON API, status by polling
- Per-payer resilience pipeline: token bucket, bulkhead, retry with jitter, circuit breaker, timeouts
- Parks submissions while a payer is down and retries them later

## Endpoints

| Method | Path | What |
|---|---|---|
| `POST` | `/api/eligibility` | eligibility check, cached 5 minutes |
| `GET` | `/api/submissions/by-claim/{claimId}` | submission attempts and acknowledgements |
| `GET` | `/api/payers/health`, `/api/payers/files` | payer health and X12 files (staff) |
| `POST` | `/api/payers/payer-a/callbacks/adjudication` | Northwind's callback (shared token header) |
| `POST` | `/internal/eligibility`, `/internal/status-inquiries` | for claims only |

## Events

| Publishes | Consumes |
|---|---|
| `payers.claim-submitted`, `payers.ack-received`, `payers.status-updated`, `payers.remittance-received` | `claims.claim-ready` |

| | |
|---|---|
| Database | `cb_payers` (submissions, history, X12 files, control numbers) |
| Blob | payer folders `payer-a`, `payer-b`, `payer-c`; `x12-archive`; `remittances` |
| Port | 5105 on the host |
| Tests | `tests/ClaimBridge.PayersGateway.Tests` |

## Run it alone

Start the infrastructure from Docker Compose, then the service (`Development` settings point at the host ports):

```bash
docker compose up -d sqlserver-init kafka-init redis azurite
dotnet run --project src/services/payers-gateway/ClaimBridge.PayersGateway
```

It listens on http://localhost:5105; the OpenAPI reference is at http://localhost:5105/scalar/v1. The payers are the simulator: `docker compose up -d payer-sim` (port 5109).
