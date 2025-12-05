# claim-bridge

A medical claims clearinghouse built as a set of microservices: practices submit professional claims, claim-bridge
scrubs them, routes them to each payer in that payer's format, tracks acknowledgements and status, posts the
remittances, and keeps the practice informed through a portal, an API and signed webhooks.

> **Demo only.** All patients, members, providers and payers are synthetic. NPIs and tax ids are made up (NPIs pass
> the check digit but belong to no one). The X12 support is a simplified subset of 837P, 999, 277CA and 835, the
> payers are simulators, and the denial model is trained on generated history. Nothing here is certified or
> suitable for real PHI.

## Features

- Claim intake from the portal form, a REST API with idempotency keys, and batch upload (CSV or 837P)
- Scrubbing with 13 built-in edits and data-driven payer rules, shown inline while a claim is typed
- Three simulated payers: a JSON API with callbacks, an X12 file exchange with 999/277CA/835, and a flaky API behind
  a circuit breaker
- Claim lifecycle with durable timeouts: status inquiry, resubmission and escalation when a payer goes quiet
- 835 posting with partial payments, takebacks and a reconciliation queue; A/R ageing and remittance PDFs
- Denial worklist, corrected and voided claims, appeals with generated letters
- Denial risk score with plain-language reasons, from a calibrated gradient boosting model
- HMAC-signed webhooks with retries and auto-disable, plus email notices
- OAuth 2.0 / OpenID Connect (code + PKCE for people, client credentials for practice systems), per-client rate
  limits, encrypted PHI columns, PHI-free logs and events, audited PHI reads
- Dashboard with KPIs, denial trends, ageing, top edits and payer health

**Tech stack:** .NET 9 (ASP.NET Core, EF Core, YARP, OpenIddict, gRPC), Python 3.12 (FastAPI, scikit-learn),
Angular 19, Kafka 3.9, SQL Server 2022, Redis, Azure Blob (Azurite), Docker Compose, Helm, Terraform (azurerm 4.x),
Azure DevOps.

## Getting started

Requirements: Docker with Compose v2 and about 8 GB of memory for it.

```bash
cp .env.example .env
docker compose up --build
```

The first start builds the images, creates the databases and topics, loads 5,000 historical claims and trains the
first model; it is ready when the `seed` container exits.

| What | URL |
|---|---|
| Portal | http://localhost:4200 |
| API gateway | http://localhost:5080 |
| API reference (claims service) | http://localhost:5103/scalar/v1 |
| Payer simulator | http://localhost:5109 |
| Webhook receiver | http://localhost:5190/webhooks |
| Kafka UI | http://localhost:8085 |
| MailHog | http://localhost:8025 |
| Jaeger | http://localhost:16686 |

Submit a claim as the demo practice system (needs `jq`):

```bash
TOKEN=$(curl -s -u sunrise-pms:demo-sunrise-secret-change-me -d grant_type=client_credentials -d "scope=claims.submit claims.read" http://localhost:5080/connect/token | jq -r .access_token) && sed "s/__DOS__/$(date +%F)/" tools/samples/claim.json | curl -s http://localhost:5080/api/v1/claims -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" -d @-
```

The response has a `statusUrl`; within a minute or so the claim moves from `Received` through `Submitted` and
`AcceptedByPayer` to `Paid`, `Posted` and `Closed`, and the webhook receiver shows the signed deliveries.

## Seed data and demo logins

The seed creates three practices (Sunrise Family Clinic, Lakeside Orthopedics, Harbor Pediatrics), their enrollments
with the three payers (Northwind Health Plan, Bluepeak Mutual, Cascade Care Insurance), simulator members, payer rules,
code sets and 5,000 claims of history with payments, denials and appeals.

All demo users share the password from `SEED_DEMO_PASSWORD` in `.env` (default `Demo@12345`).

| Login | Role | Sees |
|---|---|---|
| admin@claimbridge.local | Admin | everything, payer rules, model training |
| ops@claimbridge.local | OpsAnalyst | all practices, reconciliation, payer health |
| admin@sunriseclinic.local | ProviderAdmin | Sunrise: users, API clients, webhooks, enrollments, claims |
| billing@sunriseclinic.local | ProviderBiller | Sunrise claims, batches, denials, remittances |
| billing@lakesideortho.local | ProviderBiller | Lakeside claims, batches, denials, remittances |

The demo API client is `sunrise-pms` with the secret from `DEMO_CLIENT_SECRET` (default
`demo-sunrise-secret-change-me`). Sunrise's demo webhook subscription points at the webhook receiver and signs with
`DEMO_WEBHOOK_SECRET`.

## Running tests

```bash
dotnet test ClaimBridge.sln
```

Needs the .NET 9 SDK; the SQL Server integration tests use Testcontainers and are skipped when Docker is not running.

```bash
cd src/services/denial-predictor && python3.12 -m venv .venv && .venv/bin/pip install -r requirements-dev.txt && .venv/bin/pytest
```

The same works in `src/services/analytics`.

```bash
cd angular-frontend && npm ci && npm run test:ci
```

With the stack running, the end-to-end scenarios (paid claim with webhooks, idempotent replay, scrub fix, X12 batch
with a partial payment, payer outage and recovery, denial and correction) run with:

```bash
pip install -r tools/e2e/requirements.txt && python tools/e2e/scenarios.py
```

## Project layout

```
src/gateway            YARP gateway and claim detail composition
src/services/*         identity, providers, claims, scrubber, payers-gateway, remittance,
                       notifications, denial-predictor, analytics
src/shared             service defaults, event contracts, X12 library, protos
angular-frontend       portal
tools                  payer simulator, webhook receiver, seed, samples, e2e scenarios
tests                  .NET test projects
deploy                 compose, Helm chart, SQL Server and Kafka init, pipeline templates
infra/terraform        Azure modules and environments
```
