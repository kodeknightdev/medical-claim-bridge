# claims

Owns the claim: intake, versions, lifecycle, denials and appeals.

- Intake from the portal, the `/api/v1/claims` submission API (idempotency keys) and batch files (CSV or 837P,
  fanned out through `claims.batch-uploaded`)
- Duplicate detection by business fingerprint
- Process manager driving the lifecycle state machine from scrubber, payer and remittance events, with durable
  ack/inquiry/resubmission deadlines
- Calls the scrubber over gRPC, the denial predictor and payers-gateway eligibility over HTTP
- Patient fields encrypted at rest; every PHI read audited
- Denial worklist, corrections (frequency 7), voids (frequency 8), appeals with PDF letters

| | |
|---|---|
| Database | `cb_claims` (claims, lines, history, lifecycle state, search entries, worklist, appeals, outbox) |
| Port | 5103 on the host |
| Consumes | `claims.*`, `payers.claim-submitted`, `payers.ack-received`, `payers.status-updated`, `remittance.*`, `providers.enrollment-changed` |
| Publishes | `claims.*` |
| Tests | `tests/ClaimBridge.Claims.Tests` |
