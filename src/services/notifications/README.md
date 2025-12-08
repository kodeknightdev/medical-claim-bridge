# notifications

Signed webhooks and email.

- One delivery row per matching subscription; a dispatcher sends due rows every two seconds
- `X-ClaimBridge-Signature: t=...,v1=...` (HMAC-SHA256 over timestamp and body)
- Backoff from 10 seconds to 6 hours over nine attempts; auto-disables a subscription after 15 consecutive failures
- Email to the practice's billing contact on denials and rejections (MailHog locally)

## Endpoints

| Method | Path | What |
|---|---|---|
| `GET` | `/api/webhook-deliveries`, `/api/webhook-deliveries/{id}`, `/api/webhook-deliveries/stats` | delivery log |
| `POST` | `/api/webhook-deliveries/{id}/redeliver` | send one again |

## Events

| Publishes | Consumes |
|---|---|
| none | `claims.status-changed`, `remittance.payment-posted`, `providers.webhook-subscription-changed` |

| | |
|---|---|
| Database | `cb_notifications` |
| Port | 5107 on the host |
| Tests | `tests/ClaimBridge.Notifications.Tests` |

## Run it alone

Start the infrastructure from Docker Compose, then the service (`Development` settings point at the host ports):

```bash
docker compose up -d sqlserver-init kafka-init redis azurite
dotnet run --project src/services/notifications/ClaimBridge.Notifications
```

It listens on http://localhost:5107; the OpenAPI reference is at http://localhost:5107/scalar/v1. Subscriptions come from providers on 5102; `docker compose up -d mailhog webhook-receiver` gives it somewhere to send.
