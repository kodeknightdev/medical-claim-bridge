# scrubber

Claim edits before submission.

- 13 built-in rules (`Rules/BuiltIn`), each a small class
- Payer rules stored as JSON conditions, compiled into composite specifications and cached per payer
- gRPC `ClaimScrubber.Scrub` for claims (preview without saving, or final scrub with a stored run)
- Code set lookups for the portal form

| | |
|---|---|
| Database | `cb_scrubber` (code sets, payer rules, scrub runs and edits) |
| Ports | HTTP 8080 (5104 on the host), gRPC 8081 (5114) |
| Tests | `tests/ClaimBridge.Scrubber.Tests` |
