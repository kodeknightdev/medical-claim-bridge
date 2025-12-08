# gateway

The single public entry point. YARP proxies `/api/*`, `/connect/*`, `/.well-known/*` and `/account/*` to the
services; Minimal API endpoints add the composed claim view and a couple of helpers.

- Validates the bearer token before proxying and forwards it, so services authorise again
- Per-client fixed-window rate limits from the `cb_rate_limit` token claim
- `GET /api/claims/{id}/details` composes claims, payers-gateway, remittance, scrubber and denial-predictor in
  parallel and returns a partial result when an optional source is down
- Audits PHI-bearing reads to the `Audit` log category

| | |
|---|---|
| Port | 8080 in the container, 5000 on the host |
| Config | routes and clusters in `ClaimBridge.Gateway/appsettings.json`; `RateLimiting:*`, `Cors:Origins` |
| Tests | `tests/ClaimBridge.Gateway.Tests`, `tests/ClaimBridge.Security.Tests` |
