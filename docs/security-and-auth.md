# Security and auth

claim-bridge handles protected health information (patient names, dates of birth, member ids). Everything here runs
on synthetic data, but the controls are built as if it did not: least privilege between services, PHI encrypted at
rest and kept out of logs and events, every PHI read audited.

## Identity

The [`identity`](../src/services/identity/ClaimBridge.Identity) service is an OpenIddict 6 server on top of
ASP.NET Core Identity. It issues signed JWT access tokens that every other service validates on its own.

| Client | Flow | Who |
|---|---|---|
| `claim-bridge-portal` | authorization code + PKCE, refresh tokens | people using the Angular portal |
| provider API clients (seeded: `sunrise-pms`) | client credentials | practice management systems submitting claims |

- Access tokens live 15 minutes, refresh tokens 8 hours. Refresh tokens roll on every use; replaying an old one more
  than 30 seconds later revokes the chain.
- Tokens carry `sub`, `role`, `org_id` (provider users and API clients), `scope`, `client_id` and, for API clients,
  `cb_rate_limit`. They are issued for the audience `claim-bridge-api`.
- The issuer is the public gateway URL (`http://localhost:5080/` locally). Services fetch signing keys from the
  internal identity address with
  [`InternalOidcConfigurationRetriever`](../src/shared/ClaimBridge.ServiceDefaults/Auth/InternalOidcConfigurationRetriever.cs),
  so tokens validate the same whether a request came through the gateway or from inside the cluster.
- Signing and encryption keys come from PKCS#12 certificates (`Auth:Keys:*`). In Kubernetes they are mounted from the
  `claim-bridge-identity-keys` secret so that every replica signs with the same key. Only Development falls back to
  ephemeral keys, which is fine for one instance and invalidates tokens on restart.
- Login cookies and antiforgery tokens are protected with a Data Protection key ring stored in Redis, shared by all
  identity replicas.
- The login page is a small server-rendered form with antiforgery tokens; there is no self-registration. The demo
  users are seeded; `GET /api/users` lists an organisation's users.

API clients are created in the portal (Integrations) or with `POST /api/clients`. A client belongs to one
organisation, may only get `claims.submit` and `claims.read`, and its secret is shown once.
`POST /api/clients/{id}/rotate-secret` and `/revoke` do what they say; every change is written to identity's audit
table.

## Roles, scopes and policies

| Role | Who | Can |
|---|---|---|
| `Admin` | clearinghouse administrators | everything, including payer rules and model training |
| `OpsAnalyst` | clearinghouse operations | all organisations' claims, reconciliation, payer health; read-only on configuration |
| `ProviderAdmin` | a practice's administrator | their organisation: users, API clients, webhooks, enrollments, plus biller rights |
| `ProviderBiller` | a practice's billing staff | their organisation's claims, batches, denials, appeals, remittances |

Policies in [`AuthExtensions`](../src/shared/ClaimBridge.ServiceDefaults/Auth/AuthExtensions.cs) combine roles and
scopes: `claims.submit` needs the `claims.submit` scope, `claims.read` needs `claims.read` or the portal's `api` scope,
`staff` needs Admin or OpsAnalyst, and so on. Controllers declare a policy at class level and tighten it per action.

**Organisation scoping** is enforced in every query, not just at the door.
[`PrincipalExtensions.OrgFilter`](../src/shared/ClaimBridge.ServiceDefaults/Auth/PrincipalExtensions.cs) returns the
caller's own `org_id` for provider users and API clients, ignoring any `orgId` they pass; staff may pass one or see
all. A provider asking for another organisation's claim gets `404`, not `403`, so ids cannot be probed.

## Gateway

The [gateway](../src/gateway/ClaimBridge.Gateway/Program.cs) is the only service exposed outside the cluster. It
validates the token before proxying (YARP routes carry an authorization policy), applies rate limits, and forwards the
original `Authorization` header so each service authorises the call again. `/internal/*` routes are never mapped.

Rate limits ([`PerClientRateLimit`](../src/gateway/ClaimBridge.Gateway/RateLimiting/PerClientRateLimit.cs)) are fixed
one-minute windows per caller: API clients get the limit stored on their profile (`cb_rate_limit`, 120/min for the
demo client), portal users 600/min, anonymous calls 120/min per IP. A rejected call is `429` with `Retry-After`.

## PHI handling

| Where | Control |
|---|---|
| Database | member id, first and last name and date of birth are encrypted with AES-256-GCM ([`AesGcmPhiProtector`](../src/shared/ClaimBridge.ServiceDefaults/Phi/AesGcmPhiProtector.cs)); keys are derived with HKDF from one master key, separately for encryption and hashing; values are prefixed `v1:` so the key can be rotated |
| Search | an HMAC-SHA256 of the normalised member id supports exact-match search and duplicate detection without decrypting; the last four characters and the birth year are kept in clear for display and features |
| Events | Kafka payloads carry ids, codes and amounts; never names, member ids or dates of birth |
| Payers | payers-gateway fetches the submission document over the internal API for each attempt and does not store it |
| Logs | [`PhiRedactionEnricher`](../src/shared/ClaimBridge.ServiceDefaults/Logging/PhiRedactionEnricher.cs) replaces known PHI property names and anything shaped like a date of birth with `[REDACTED]` before a log event is written |
| Webhooks | payloads carry claim ids, numbers and amounts only ([webhooks](webhooks.md)) |
| Browser | access and refresh tokens are kept in memory only ([`MemoryStorage`](../angular-frontend/src/app/core/auth/memory-storage.ts)); only the PKCE/state values needed for the redirect touch `sessionStorage` |
| Analytics and ML | de-identified features only (ages, counts, codes) |

Every read that returns PHI is audited: claims writes an `AuditLog` row (time, actor, org, action, claim id)
whenever a claim detail is opened or a submission document is fetched, and the gateway's
[`PhiAuditFilter`](../src/gateway/ClaimBridge.Gateway/Filters/PhiAuditFilter.cs) logs composed claim detail reads to
the `Audit` category and counts them in `gateway.phi.reads`. Audit records hold ids, never the PHI itself.

## Secrets

- Locally, secrets come from `.env` (copied from `.env.example`, git-ignored). The example values are clearly
  development-only, and Stripe is only ever `sk_test_xxx`.
- In Kubernetes, the chart reads an existing secret (`global.existingSecret`) that is synced from Key Vault; the dev
  values create a secret with `REPLACE_ME` placeholders.
- Terraform creates no passwords: Azure SQL uses Entra ID authentication only, and the Key Vault holds the
  application secrets that are added out of band. State lives in a remote backend that is not configured in the
  repository.
- The pipeline runs gitleaks against the repository ([`.gitleaks.toml`](../.gitleaks.toml)).

## Service-to-service

- Every service has its own SQL login with rights on its own database only.
- Internal REST calls (`/internal/*`) and gRPC are reachable only inside the namespace; the Helm network policies deny
  everything by default and open only gateway and portal ingress, in-namespace traffic, Azure PaaS endpoints, and
  external egress for notifications and payers-gateway.
- Payer A's adjudication callback is the one inbound call from outside that does not use our tokens; it is checked
  against a shared `X-Northwind-Token` value compared in constant time.

## Known gaps

- Internal calls are not authenticated with service tokens or mTLS; the network policy is the boundary. A service mesh
  or workload identity would be the next step.
- Container images are not signed.
- MailKit 4.13.0 and OpenTelemetry.Exporter.OpenTelemetryProtocol 1.12.0 have published advisories that are fixed in
  later releases; they stay pinned here because the project's dependency cut-off predates the fixes. Neither is
  reachable from untrusted input in this design (SMTP goes to a local relay, OTLP to a local collector).
