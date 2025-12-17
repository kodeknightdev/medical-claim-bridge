# Development

## Prerequisites

| Tool | Version | Needed for |
|---|---|---|
| Docker with Compose v2 | Compose 2.24 or later | the whole stack, Testcontainers tests |
| .NET SDK | 9.0.304 (see `global.json`) | building and testing the .NET services |
| Python | 3.12 | the denial predictor, analytics and the seed and e2e tools |
| Node.js | 20 LTS | the portal |

About 8 GB of memory for Docker is comfortable. On Apple silicon the SQL Server image runs under emulation; it is slow
to start the first time but works.

## Running the stack

```bash
cp .env.example .env
docker compose up --build
```

The root `compose.yaml` includes [`deploy/docker-compose.yml`](../deploy/docker-compose.yml) and the override that
publishes every service on the host. On first start `sqlserver-init` creates the databases and logins,
`kafka-init` creates the topics, each service applies its migrations, and `seed` loads 5,000 historical claims and
trains the first model. The stack is ready when `seed` exits with code 0 (a few minutes on a cold start).

To start over with empty databases:

```bash
docker compose down -v
```

### On a local Kubernetes cluster

The same images run on Docker Desktop's Kubernetes or kind with the Helm chart and
[`values-local.yaml`](../deploy/helm/claim-bridge/values-local.yaml), which brings its own SQL Server, Kafka, Redis,
Azurite and MailHog and needs no Azure account or Terraform. The commands are in
[deployment](deployment.md#kubernetes-without-azure).

### Ports

| Port | What |
|---|---|
| 4200 | portal |
| 5080 | gateway (all public APIs, OpenID Connect endpoints); not 5000, which macOS keeps for AirPlay Receiver |
| 5101-5108 | identity, providers, claims, scrubber, payers-gateway, remittance, notifications, denial-predictor |
| 5109 | payer simulator (`/sim/*` control endpoints) |
| 5110 | analytics |
| 5114 | scrubber gRPC |
| 5190 | webhook receiver (`GET /webhooks` lists what arrived) |
| 1433, 9094, 6379, 10000 | SQL Server, Kafka (external listener), Redis, Azurite |
| 8085 | kafka-ui |
| 8025 | MailHog |
| 16686 | Jaeger |

## Working on one service

Stop the container and run the service from your IDE or the command line; the `Development` settings in each
service's `appsettings.Development.json` point at the host ports above.

```bash
docker compose stop claims
dotnet run --project src/services/claims/ClaimBridge.Claims
```

`launchSettings.json` uses the same port as the container mapping, so the gateway and other services find it without
changes. The portal works the same way:

```bash
cd angular-frontend
npm ci
npm start
```

`npm start` serves on 4200 and proxies `/api` to the gateway on 5080 ([`proxy.conf.json`](../angular-frontend/proxy.conf.json)).
Stop the `portal` container first, since it uses the same port.

For the Python services:

```bash
cd src/services/denial-predictor
python3.12 -m venv .venv
.venv/bin/pip install -r requirements-dev.txt
.venv/bin/uvicorn app.main:app --port 5108 --reload
```

Without `DATABASE_URL` they fall back to a local SQLite file, which is enough for most work on the API.

## Tests

| What | Command | Notes |
|---|---|---|
| all .NET tests | `dotnet test ClaimBridge.sln` | tests tagged as Docker tests are skipped when Docker is not running |
| one project | `dotnet test tests/ClaimBridge.Scrubber.Tests` | |
| denial predictor | `cd src/services/denial-predictor && .venv/bin/pytest` | |
| analytics | `cd src/services/analytics && .venv/bin/pytest` | |
| portal | `cd angular-frontend && npm run test:ci` | needs Chrome or Chromium for Karma |
| end to end | `python tools/e2e/scenarios.py` | against a running compose stack; install `tools/e2e/requirements.txt` first |

The .NET test projects:

| Project | Covers |
|---|---|
| `ClaimBridge.Scrubber.Tests` | every built-in rule, payer rule compilation, the gRPC service |
| `ClaimBridge.X12.Tests` | reading and writing 837P, 999, 277CA and 835; envelope errors; Verify snapshots of generated files |
| `ClaimBridge.Claims.Tests` | lifecycle table, process manager, timeouts, idempotency, duplicates, batch parsing; SQL Server via Testcontainers |
| `ClaimBridge.Remittance.Tests` | posting maths, partial payments, takebacks, unmatched lines, ageing |
| `ClaimBridge.Notifications.Tests` | signatures, backoff, retries and auto-disable |
| `ClaimBridge.PayersGateway.Tests` | each adapter against WireMock, retries, timeouts, circuit breakers |
| `ClaimBridge.Gateway.Tests` | routing, composition with missing sources |
| `ClaimBridge.Security.Tests` | scopes, org scoping, rate limits, PHI redaction and encryption |

`ClaimBridge.Testing` holds shared helpers: locally signed test tokens (`UseTestTokens`), the SQL Server fixture and
the `DockerFact` attribute.

The e2e script runs six scenarios: Payer A paid with signed webhooks, idempotent resubmission, scrub failure fixed and
paid, a Payer B X12 batch with a partial payment, a Payer C outage with the circuit breaker opening and recovering, and
a denial corrected and paid.

## Database migrations

.NET services use EF Core migrations, applied on start. To add one:

```bash
dotnet tool restore
dotnet ef migrations add AddSomething --project src/services/claims/ClaimBridge.Claims
```

Python services use Alembic, also applied on start by `docker-entrypoint.sh`:

```bash
cd src/services/analytics
.venv/bin/alembic revision -m "add something"
```

## Dependencies

- NuGet versions live only in [`Directory.Packages.props`](../Directory.Packages.props); projects reference packages
  without versions. Lock files are on, and CI restores in locked mode. After changing a version run
  `dotnet restore --force-evaluate` and commit the updated `packages.lock.json` files.
- Python: edit `requirements.in` and recompile the lock, for example
  `uv pip compile requirements.in -o requirements.txt --python-version 3.12`.
- Portal: `npm install <package>@<exact version>` and commit `package-lock.json`.

## Conventions

- Comments explain why, not what. Public and shared APIs get XML doc comments; the rest rarely needs them.
- One aggregate or concern per file; folders by feature inside a service (`Lifecycle`, `Denials`, `Batches`).
- Time comes from `TimeProvider`, never `DateTime.Now`, so tests can move the clock.
- New events go in `ClaimBridge.Contracts/Events` with a `SchemaVersion`; a breaking change is a new event type.
- No PHI in logs, events, metric tags or exception messages.
- Commit messages follow Conventional Commits (`feat(claims): ...`, `fix(gateway): ...`).
