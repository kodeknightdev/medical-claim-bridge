# identity

OpenIddict authorization server on ASP.NET Core Identity.

- Authorization code + PKCE with rotating refresh tokens for the portal; client credentials for practice systems
- Tokens carry `role`, `org_id`, `scope` and, for API clients, `cb_rate_limit`
- Seeds the demo users and the `sunrise-pms` client when `Seed:Enabled` is true

## Endpoints

| Method | Path | What |
|---|---|---|
| `GET`/`POST` | `/connect/authorize`, `/connect/token`, `/connect/userinfo`, `/connect/logout` | OpenID Connect |
| `GET`/`POST` | `/account/login`, `/account/logout` | server-rendered sign-in |
| `GET`/`POST` | `/api/clients`, `/api/clients/{clientId}/rotate-secret`, `/api/clients/{clientId}/revoke` | API clients |
| `GET` | `/api/users`, `/api/users/me` | users |

## Events

None published or consumed.

| | |
|---|---|
| Database | `cb_identity` (users, roles, OpenIddict tables, API client profiles, audit) |
| Port | 5101 on the host |
| Keys | `Auth:Keys:*` PKCS#12 certificates; ephemeral keys in Development only |
| Shared state | Data Protection keys in Redis so every replica can read login cookies |

## Run it alone

Start the infrastructure from Docker Compose, then the service (`Development` settings point at the host ports):

```bash
docker compose up -d sqlserver-init kafka-init redis azurite
dotnet run --project src/services/identity/ClaimBridge.Identity
```

It listens on http://localhost:5101; the OpenAPI reference is at http://localhost:5101/scalar/v1. Set `Seed:DemoPassword` (for example in user secrets) to get the demo users.
