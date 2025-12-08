# payers-gateway

Everything payer-specific lives here, behind one adapter per payer.

- Northwind (`PAYER-A`): JSON submit, adjudication callbacks
- Bluepeak (`PAYER-B`): 837P batch files out; 999, 277CA and 835 files in
- Cascade (`PAYER-C`): slow, flaky JSON API, status by polling
- Per-payer resilience pipeline: token bucket, bulkhead, retry with jitter, circuit breaker, timeouts
- Parks submissions while a payer is down and retries them later
- Eligibility with a 5 minute cache; payer health for the ops screen

| | |
|---|---|
| Database | `cb_payers` (submissions, history, X12 files, control numbers) |
| Blob | payer folders `payer-a`, `payer-b`, `payer-c`; `x12-archive`; `remittances` |
| Port | 5105 on the host |
| Consumes | `claims.claim-ready` |
| Publishes | `payers.*` |
| Tests | `tests/ClaimBridge.PayersGateway.Tests` |
