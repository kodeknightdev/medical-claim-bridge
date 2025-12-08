# providers

Provider organisations, rendering providers, payer enrollments and webhook subscriptions.

- NPI check digit validation when a rendering provider is added
- Enrollment requests start `Pending`; staff approve or reject
- Webhook secrets are generated here, shown once and stored encrypted

## Endpoints

| Method | Path | What |
|---|---|---|
| `GET` | `/api/orgs`, `/api/orgs/{id}`, `/api/payers` | organisations and the payer directory |
| `GET`/`POST` | `/api/orgs/{id}/providers` | rendering providers |
| `GET`/`POST` | `/api/enrollments`, `/api/enrollments/{id}/decision` | payer enrollment |
| `GET`/`POST`/`DELETE` | `/api/webhooks`, `/api/webhooks/{id}/rotate-secret`, `/api/webhooks/{id}/enable` | webhook subscriptions |
| `GET`/`POST` | `/internal/enrollments/check`, `/internal/webhooks/by-org/{orgId}`, `/internal/webhooks/{id}/disable`, `/internal/orgs/{id}` | for claims and notifications only |

## Events

| Publishes | Consumes |
|---|---|
| `providers.enrollment-changed`, `providers.webhook-subscription-changed` | none |

| | |
|---|---|
| Database | `cb_providers` |
| Port | 5102 on the host |

## Run it alone

Start the infrastructure from Docker Compose, then the service (`Development` settings point at the host ports):

```bash
docker compose up -d sqlserver-init kafka-init redis azurite
dotnet run --project src/services/providers/ClaimBridge.Providers
```

It listens on http://localhost:5102; the OpenAPI reference is at http://localhost:5102/scalar/v1.
