# scrubber

Claim edits before submission.

- 13 built-in rules (`Rules/BuiltIn`), each a small class
- Payer rules stored as JSON conditions, compiled into composite specifications and cached per payer
- Code set lookups for the portal form

## Endpoints

| Method | Path | What |
|---|---|---|
| gRPC | `claimbridge.scrubber.v1.ClaimScrubber/Scrub` (port 8081) | preview without saving, or final scrub with a stored run |
| `GET` | `/api/scrub-results/{claimId}`, `/api/scrub-results/top-edits` | scrub runs and edit counts |
| `GET`/`POST`/`PUT` | `/api/payer-rules`, `/api/payer-rules/built-in`, `/api/payer-rules/{id}` | payer rules |
| `GET` | `/api/codesets/icd10`, `/procedures`, `/modifiers`, `/adjustment-reasons` | code sets |

## Events

None; claims calls it over gRPC.

| | |
|---|---|
| Database | `cb_scrubber` (code sets, payer rules, scrub runs and edits) |
| Ports | HTTP 8080 (5104 on the host), gRPC 8081 (5114) |
| Tests | `tests/ClaimBridge.Scrubber.Tests` |

## Run it alone

Start the infrastructure from Docker Compose, then the service (`Development` settings point at the host ports):

```bash
docker compose up -d sqlserver-init kafka-init redis azurite
dotnet run --project src/services/scrubber/ClaimBridge.Scrubber
```

It listens on http://localhost:5104; the OpenAPI reference is at http://localhost:5104/scalar/v1. gRPC listens on 5114.
