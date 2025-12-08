# providers

Provider organisations, rendering providers, payer enrollments and webhook subscriptions.

- NPI check digit validation when a rendering provider is added
- Enrollment requests start `Pending`; staff approve or reject; changes publish `providers.enrollment-changed`
- Webhook secrets are generated here, shown once and stored encrypted; changes publish
  `providers.webhook-subscription-changed`
- Internal endpoints for claims (enrollment check) and notifications (subscriptions, auto-disable)

| | |
|---|---|
| Database | `cb_providers` |
| Port | 5102 on the host |
| Publishes | `providers.enrollment-changed`, `providers.webhook-subscription-changed` |
