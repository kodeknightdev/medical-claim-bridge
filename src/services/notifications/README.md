# notifications

Signed webhooks and email.

- One delivery row per matching subscription; a dispatcher sends due rows every two seconds
- `X-ClaimBridge-Signature: t=...,v1=...` (HMAC-SHA256 over timestamp and body)
- Backoff from 10 seconds to 6 hours over nine attempts; auto-disables a subscription after 15 consecutive failures
- Delivery log and manual redelivery under `/api/webhook-deliveries`
- Email to the practice's billing contact on denials and rejections (MailHog locally)

| | |
|---|---|
| Database | `cb_notifications` |
| Port | 5107 on the host |
| Consumes | `claims.status-changed`, `remittance.payment-posted`, `providers.webhook-subscription-changed` |
| Tests | `tests/ClaimBridge.Notifications.Tests` |
