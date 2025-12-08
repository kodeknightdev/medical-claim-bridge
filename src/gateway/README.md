# gateway

The single public entry point. YARP proxies `/api/*`, `/connect/*`, `/.well-known/*` and `/account/*` to the
services; Minimal API endpoints add the composed claim view and a couple of helpers.

- Validates the bearer token before proxying and forwards it, so services authorise again
- Per-client fixed-window rate limits from the `cb_rate_limit` token claim
- Audits PHI-bearing reads to the `Audit` log category

## Endpoints

| Method | Path | What |
|---|---|---|
| `GET` | `/api/claims/{id}/details` | claim from claims plus scrub result, submissions, remittances and risk, fetched in parallel; `partial: true` when an optional source is down |
| `GET` | `/api/gateway/me` | the caller's claims as the gateway sees them |
| `GET` | `/api/gateway/routes` | proxied routes (staff only) |
| any | everything else under `/api`, `/connect`, `/.well-known`, `/account` | proxied by YARP to the owning service |

## Events

None; the gateway is HTTP only.

| | |
|---|---|
| Port | 8080 in the container, 5080 on the host |
| Config | routes and clusters in `ClaimBridge.Gateway/appsettings.json`; `RateLimiting:*`, `Cors:Origins` |
| Tests | `tests/ClaimBridge.Gateway.Tests`, `tests/ClaimBridge.Security.Tests` |

## Run it alone

Start the infrastructure from Docker Compose, then the service (`Development` settings point at the host ports):

```bash
docker compose up -d sqlserver-init kafka-init redis azurite
dotnet run --project src/gateway/ClaimBridge.Gateway
```

It listens on http://localhost:5080; the OpenAPI reference is at http://localhost:5080/scalar/v1. Token checks need identity on 5101, and proxied routes need the services they point at.
