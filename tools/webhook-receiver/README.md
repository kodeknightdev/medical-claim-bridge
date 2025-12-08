# webhook-receiver

A small receiver that verifies `X-ClaimBridge-Signature` and keeps what it got in memory.

- `POST /webhooks/{tenant}` verifies with the secret configured for that tenant (`Receiver:Secrets:<tenant>`)
- `GET /webhooks` lists received deliveries with a `signatureValid` flag

`ClaimBridge.WebhookReceiver/verify_signature.py` shows the same check in plain Python for receivers outside .NET.
