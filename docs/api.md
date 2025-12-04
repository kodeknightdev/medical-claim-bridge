# API

All public endpoints are served through the gateway (`http://localhost:5080` locally). Each service also publishes
its own OpenAPI document at `/openapi/v1.json` and a Scalar reference at `/scalar/v1` in Development, on its own port
(5101-5108, see [development](development.md#ports)).

## Conventions

- **Auth.** `Authorization: Bearer <access token>` on everything except `/connect/*`, `/.well-known/*` and
  `/account/*`. See [security and auth](security-and-auth.md).
- **Errors** are RFC 9457 problem details (`application/problem+json`) with `title`, `detail` and, for validation,
  an `errors` map keyed by field.
- **Organisation scoping.** Provider users and API clients only ever see their own organisation; an `orgId` parameter
  is honoured for staff only. A resource in another organisation is `404`.
- **Paging.** List endpoints take `page` (from 1) and `pageSize` (default 25, at most 200) and return
  `{ items, total, page, pageSize }`.
- **Rate limits.** `429` with `Retry-After` when the caller's per-minute window is used up.
- **Dates** are ISO 8601; money is a JSON number with two decimals.

## Getting a token

Practice management systems use client credentials:

```bash
curl -s -u sunrise-pms:$DEMO_CLIENT_SECRET \
  -d grant_type=client_credentials -d "scope=claims.submit claims.read" \
  http://localhost:5080/connect/token
```

The portal uses authorization code with PKCE against `/connect/authorize` and `/connect/token`; discovery is at
`/.well-known/openid-configuration`.

## Submission API (for practice systems)

| Method | Path | Scope | Notes |
|---|---|---|---|
| `POST` | `/api/v1/claims` | `claims.submit` | `Idempotency-Key` header recommended; `202 Accepted` with `claimId`, `claimNumber`, `status`, `statusUrl` |
| `GET` | `/api/v1/claims/{id}` | `claims.submit` | status, status time, charge, paid, patient responsibility |

Request body:

```json
{
  "payerId": "PAYER-A",
  "billingNpi": "1999000015",
  "renderingNpi": "1999000114",
  "patient": { "memberId": "NWH1000002", "firstName": "Jamie", "lastName": "Example", "dateOfBirth": "1958-02-12", "gender": "F" },
  "diagnoses": ["I10"],
  "priorAuthNumber": null,
  "frequencyCode": "1",
  "originalClaimNumber": null,
  "lines": [
    { "procedureCode": "99213", "modifiers": [], "units": 1, "charge": 120.00, "serviceDate": "2025-11-03", "placeOfService": "11", "dxPointers": [1] }
  ]
}
```

Structural problems (missing fields, more than 12 diagnoses or 50 lines, malformed NPI) are `400`. Business problems
found at intake are `422` (not enrolled with the payer, original claim not found, payer mismatch on a correction) or
`409` (original claim still with the payer). Coding problems are not errors here: the claim is accepted and the
scrubber's edits show up as `ScrubFailed` with the edit list.

Idempotency: the same key with the same body returns the first response again with `Idempotent-Replayed: true`; the
same key with a different body is `422`; the same key while the first request is still running is `409`.

## Claims (portal)

| Method | Path | Policy | Notes |
|---|---|---|---|
| `GET` | `/api/claims` | claims.read | filters `status` (comma separated), `payerId`, `orgId`, `from`, `to`, `search` (claim number prefix, full member id, member id last 4 or procedure code) |
| `GET` | `/api/claims/{id}` | claims.read | full claim with decrypted patient fields; audited |
| `GET` | `/api/claims/{id}/history` | claims.read | status timeline |
| `GET` | `/api/claims/{id}/details` | claims.read | gateway composition: claim, submissions, remittances, scrub result, risk; `partial` flag |
| `POST` | `/api/claims` | claims.submit | create from the portal form |
| `PUT` | `/api/claims/{id}` | claims.submit | edit a claim that has not been sent |
| `POST` | `/api/claims/scrub-preview` | claims.submit | scrub, eligibility and risk without saving |
| `POST` | `/api/claims/{id}/correct` | claims.submit | replacement claim (frequency 7) |
| `POST` | `/api/claims/{id}/void` | claims.submit | void (frequency 8) |
| `POST` | `/api/batches` | claims.read | multipart `file`: CSV (template at `GET /api/batches/template`) or 837P; `202` with the batch |
| `GET` | `/api/batches`, `/api/batches/{id}` | claims.read | batch progress and per-row errors |
| `GET` | `/api/denials` | claims.read | worklist with filters `status`, `payerId`, `category`, grouped counts |
| `GET`/`POST` | `/api/appeals` | claims.read | list, create a draft |
| `GET` | `/api/appeals/{id}/letter` | claims.read | PDF |
| `POST` | `/api/appeals/{id}/submit`, `/outcome` | claims.read | move the appeal along |

## Payers, eligibility and scrubbing

| Method | Path | Policy | Notes |
|---|---|---|---|
| `POST` | `/api/eligibility` | claims.read | member id, DOB, date of service, payer; cached 5 minutes |
| `GET` | `/api/submissions/by-claim/{claimId}` | claims.read | submission attempts, channel, acks, X12 control numbers |
| `GET` | `/api/payers/health` | staff | latency, error rate, circuit state per payer |
| `GET` | `/api/payers/files` | staff | X12 files in and out, with errors |
| `GET` | `/api/scrub-results/{claimId}` | claims.read | latest scrub run with edits |
| `GET` | `/api/scrub-results/top-edits` | claims.read | most frequent edits |
| `GET` | `/api/codesets/icd10`, `/procedures`, `/modifiers`, `/adjustment-reasons` | authenticated | lookups for the form |
| `GET`/`POST` | `/api/payer-rules` | staff / admin | list, create |
| `PUT` | `/api/payer-rules/{id}` | admin | enable, disable, change severity |
| `GET` | `/api/payer-rules/built-in` | staff | built-in rule codes and names |

## Remittance and A/R

| Method | Path | Policy | Notes |
|---|---|---|---|
| `GET` | `/api/remittances` | claims.read | remittance files with totals |
| `GET` | `/api/remittances/{id}` | claims.read | postings and adjustments |
| `GET` | `/api/remittances/{id}/pdf` | claims.read | provider-friendly PDF |
| `GET` | `/api/remittances/by-claim/{claimId}` | claims.read | postings for one claim |
| `GET` | `/api/reason-codes` | claims.read | CARC/RARC explanations |
| `GET` | `/api/ar/summary`, `/open-claims`, `/snapshots` | claims.read | A/R and ageing |
| `GET` | `/api/reconciliation` | staff | unmatched 835 lines |
| `POST` | `/api/reconciliation/{id}/match`, `/dismiss` | staff | resolve a line |

## Providers, users and integrations

| Method | Path | Policy | Notes |
|---|---|---|---|
| `GET` | `/api/orgs`, `/api/orgs/{id}`, `/api/orgs/{id}/providers` | authenticated | organisations and rendering providers |
| `POST` | `/api/orgs/{id}/providers` | provider-admin | add a rendering provider (NPI check digit enforced) |
| `GET` | `/api/payers` | authenticated | payer directory |
| `GET`/`POST` | `/api/enrollments` | authenticated / provider-admin | enrollments; new ones start `Pending` |
| `POST` | `/api/enrollments/{id}/decision` | staff | approve or reject |
| `GET`/`POST` | `/api/webhooks` | authenticated / provider-admin | subscriptions; the secret is returned once |
| `POST` | `/api/webhooks/{id}/rotate-secret`, `/enable`; `DELETE /api/webhooks/{id}` | provider-admin | manage a subscription |
| `GET` | `/api/webhook-deliveries`, `/{id}`, `/stats` | claims.read | delivery log |
| `POST` | `/api/webhook-deliveries/{id}/redeliver` | claims.read | send again |
| `GET`/`POST` | `/api/clients` | admin, ops, provider-admin | API clients; the secret is returned once |
| `POST` | `/api/clients/{clientId}/rotate-secret`, `/revoke` | as above | manage a client |
| `GET` | `/api/users`, `/api/users/me` | admin, provider-admin | users |
| `GET` | `/api/gateway/me` | authenticated | the caller's claims as the gateway sees them |

## Risk and analytics

| Method | Path | Policy | Notes |
|---|---|---|---|
| `POST` | `/api/risk/score` | claims.read | score an unsaved claim summary |
| `GET` | `/api/risk/claims/{claimId}` | claims.read | latest prediction with factors |
| `GET` | `/api/models` | staff | model versions and metrics |
| `POST` | `/api/models/train` | admin | train a new version |
| `GET` | `/api/analytics/kpis`, `/denials`, `/ageing`, `/scrub-edits`, `/trends`, `/payer-health`, `/model` | claims.read | dashboard data, scoped to the caller's organisation |

## Internal endpoints

Not routed by the gateway and only reachable inside the cluster: `/internal/claims/{id}/submission`,
`/internal/claims/by-number/{number}`, `/internal/enrollments/check`, `/internal/webhooks/by-org/{orgId}`,
`/internal/webhooks/{id}/disable`, `/internal/orgs/{id}`, `/internal/eligibility`, `/internal/status-inquiries`,
`/internal/score`, `/internal/train`, `/internal/jobs/ar-snapshot` and the `/internal/seed/history` endpoints used by
the seed tool. The scrubber's gRPC service (`claimbridge.scrubber.v1.ClaimScrubber/Scrub`) listens on port 8081.
