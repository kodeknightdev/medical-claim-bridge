# identity

OpenIddict authorization server on ASP.NET Core Identity.

- Authorization code + PKCE with rotating refresh tokens for the portal; client credentials for practice systems
- Tokens carry `role`, `org_id`, `scope` and, for API clients, `cb_rate_limit`
- Server-rendered login page; API clients and users under `/api/clients` and `/api/users`
- Seeds the demo users and the `sunrise-pms` client when `Seed:Enabled` is true

| | |
|---|---|
| Database | `cb_identity` (users, roles, OpenIddict tables, API client profiles, audit) |
| Port | 5101 on the host |
| Keys | `Auth:Keys:*` PKCS#12 certificates; ephemeral keys in Development only |
| Shared state | Data Protection keys in Redis so every replica can read login cookies |
