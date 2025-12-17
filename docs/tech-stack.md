# Tech stack

Every version is pinned exactly: `global.json` for the SDK, central package management with lock files for NuGet,
compiled `requirements.txt` files for Python, `package-lock.json` for the portal, image tags with patch versions, and
a committed `.terraform.lock.hcl`. Nothing floats, so a build next year resolves the same packages as today.

## Platform

| Component | Version | Why |
|---|---|---|
| .NET SDK | 9.0.304 (`global.json`, `latestPatch` roll-forward) | current STS release with C# 13, `HybridCache`, built-in OpenAPI and `TimeProvider` |
| ASP.NET Core / EF Core runtime | 9.0.8 | matches the SDK; images `mcr.microsoft.com/dotnet/aspnet:9.0.8-bookworm-slim` and `sdk:9.0.304-bookworm-slim` |
| Python | 3.12 (`python:3.12.3-slim-bookworm`) | supported by every pinned wheel, including scikit-learn and confluent-kafka |
| Node.js (portal build only) | 20.12.2 (`node:20.12.2-alpine3.19`) | LTS supported by Angular 19 |
| SQL Server | 2022 CU17 (`mssql/server:2022-CU17-ubuntu-22.04`) | same engine family as Azure SQL; one database per service |
| Apache Kafka | 3.9.0 (`apache/kafka:3.9.0`, KRaft) | no ZooKeeper; maps to Event Hubs' Kafka endpoint in Azure |
| Redis | 7.4.2 (`redis:7.4.2-alpine`) | second level for HybridCache, Data Protection keys |
| Azurite | 3.34.0 | blob storage emulator standing in for Azure Storage and payer SFTP folders |
| nginx | 1.25.2 (`nginx:1.25.2-alpine`) | serves the built portal and proxies `/api` to the gateway |
| Jaeger | 1.66.0 all-in-one | local trace viewer over OTLP |
| MailHog | 1.0.1 | catches outgoing email locally |
| kafka-ui | 0.7.2 | topic and consumer group browser for development |

## .NET packages

| Package | Version | Used for |
|---|---|---|
| Yarp.ReverseProxy | 2.3.0 | gateway routing |
| OpenIddict.AspNetCore / .EntityFrameworkCore | 6.4.0 | OAuth 2.0 / OpenID Connect server |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 9.0.8 | user store and password hashing |
| Microsoft.AspNetCore.Authentication.JwtBearer | 9.0.8 | token validation in every service |
| Microsoft.AspNetCore.DataProtection.StackExchangeRedis | 9.0.8 | shared cookie keys across identity replicas |
| Microsoft.EntityFrameworkCore.SqlServer / .Design | 9.0.8 | data access and migrations (`dotnet-ef` 9.0.8 as a local tool) |
| Confluent.Kafka | 2.11.0 | producer and consumers |
| Grpc.AspNetCore / Grpc.Net.ClientFactory | 2.71.0 | claims to scrubber calls |
| Grpc.Tools / Google.Protobuf | 2.72.0 / 3.31.1 | code generation from `scrubber.proto` |
| Microsoft.Extensions.Http.Resilience | 9.7.0 | Polly v8 pipelines: retry, circuit breaker, timeouts, rate and concurrency limiters |
| Microsoft.Extensions.Caching.Hybrid | 9.7.0 | cache-aside with stampede protection |
| Microsoft.Extensions.Caching.StackExchangeRedis | 9.0.8 | Redis as HybridCache's distributed level |
| Quartz.Extensions.Hosting | 3.14.0 | polling and timeout jobs |
| Azure.Storage.Blobs | 12.25.0 | claim check payloads, X12 files, PDFs |
| CsvHelper | 33.1.0 | batch CSV parsing |
| QuestPDF | 2025.7.0 (Community licence) | remittance and appeal PDFs |
| MailKit | 4.13.0 | SMTP notifications |
| FluentValidation (+ DI extensions) | 12.0.0 | request validation |
| Microsoft.AspNetCore.OpenApi | 9.0.8 | OpenAPI documents |
| Scalar.AspNetCore | 2.6.9 | API reference UI at `/scalar` in Development |
| Serilog.AspNetCore | 9.0.0 | structured logging with the PHI redaction enricher |
| OpenTelemetry.* | 1.12.0 | traces and metrics over OTLP |
| AspNetCore.HealthChecks.* | 9.0.0 | SQL Server, Redis, Kafka and blob readiness checks |

Test packages: xunit 2.9.3, xunit.runner.visualstudio 3.1.3, Microsoft.NET.Test.Sdk 17.14.1, Shouldly 4.3.0,
NSubstitute 5.3.0, Verify.Xunit 30.7.3, WireMock.Net 1.8.18, Testcontainers.MsSql 4.6.0,
Microsoft.AspNetCore.Mvc.Testing 9.0.8, Microsoft.EntityFrameworkCore.InMemory 9.0.8,
Microsoft.Extensions.TimeProvider.Testing 9.7.0, System.IdentityModel.Tokens.Jwt 8.13.1, coverlet.collector 6.0.4.

## Python packages

Both services pin direct dependencies in `requirements.in` and install the fully resolved `requirements.txt`.

| Package | Version | Used for |
|---|---|---|
| fastapi / uvicorn[standard] | 0.116.1 / 0.35.0 | HTTP API |
| pydantic / pydantic-settings | 2.11.7 / 2.10.1 | request models and configuration |
| SQLAlchemy / alembic | 2.0.43 / 1.16.4 | data access and migrations |
| pyodbc | 5.2.0 | SQL Server driver (with Microsoft ODBC Driver 18 in the image) |
| confluent-kafka | 2.11.0 | events |
| pandas / numpy | 2.3.1 / 2.3.2 | feature frames and aggregates |
| scikit-learn / joblib | 1.7.1 / 1.5.1 | denial model and its storage (predictor only) |
| PyJWT[crypto] | 2.10.1 | validating access tokens against the identity JWKS |
| azure-storage-blob | 12.26.0 | reading the seeded history |
| httpx | 0.28.1 | payer health sampling (analytics only) |
| opentelemetry-sdk / exporter-otlp-proto-http / instrumentation-fastapi | 1.36.0 / 1.36.0 / 0.57b0 | tracing |
| pytest (dev) | 8.4.1 | tests |

## Portal

| Package | Version | Used for |
|---|---|---|
| @angular/core and friends | 19.2.14 | standalone components, signals, new control flow |
| @angular/cli / build-angular | 19.2.16 | esbuild-based builds, dev server with proxy |
| @angular/material / cdk | 19.2.19 | UI components |
| angular-auth-oidc-client | 19.0.2 | code flow with PKCE and refresh tokens |
| echarts / ngx-echarts | 5.6.0 / 19.0.0 | dashboard charts |
| rxjs | 7.8.2 | streams |
| typescript | 5.8.3 | language |
| karma / jasmine-core | 6.4.4 / 5.9.0 | unit tests in headless Chrome |

## Infrastructure and delivery

| Tool | Version | Used for |
|---|---|---|
| Terraform (optional) | `>= 1.11.0, < 1.14.0` (validated with 1.11.4) | one way to create the Azure resources; nothing depends on it |
| azurerm provider | 4.40.0 (locked) | AKS, ACR, Azure SQL, Redis, Event Hubs, Storage, Key Vault, Log Analytics |
| Helm | 3.17.3 for lint and template | packaging the services for AKS |
| Azure DevOps Pipelines | YAML, templates under `deploy/pipelines` | build, test, scan, image push, plan and deploy |
| gitleaks | 8.24.0 | secret scanning in the pipeline |
| curl image | `curlimages/curl:8.12.1` | Kubernetes CronJobs that trigger internal jobs |

## Choices worth explaining

- **.NET for the services, Python for the model and analytics.** The claim flow is long-running, concurrent and
  strongly typed; the model and analytics work belongs where scikit-learn and pandas live. The boundary is an HTTP API
  and Kafka events, so neither side cares about the other's language.
- **Kafka locally, Event Hubs in Azure.** The code uses only the Kafka protocol, so the managed service drops in
  without a client change.
- **OpenIddict instead of a hosted identity provider.** Keeps the whole system runnable offline with `docker compose`
  and shows the OAuth flows end to end. A deployment that already has Entra ID would federate instead.
- **HybridCache over hand-rolled `IDistributedCache` code.** It gives the in-process level, Redis level and stampede
  protection in one call.
- **Exact pins and an October 2025 cut-off.** Reproducible builds matter more here than being on the newest patch.
  The cost is that two known advisories (MailKit 4.13.0 and OpenTelemetry.Exporter.OpenTelemetryProtocol 1.12.0) stay
  until the pins are moved; see [security and auth](security-and-auth.md#known-gaps).
