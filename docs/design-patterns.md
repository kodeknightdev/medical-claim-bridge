# Design patterns

Each section names the pattern, the problem it solves here, and where it lives. Paths are relative to the repository
root.

## API Gateway

**Problem.** Browsers and practice systems should see one host, one token check and one rate limit, not eleven
services.
**Here.** [`src/gateway/ClaimBridge.Gateway`](../src/gateway/ClaimBridge.Gateway/Program.cs) runs YARP with routes and
clusters in [`appsettings.json`](../src/gateway/ClaimBridge.Gateway/appsettings.json). Each route names an
authorization policy and the `per-client` rate limiter; `/internal/*` is never routed. Services still validate the
forwarded token themselves, so the gateway is a convenience boundary, not the only one.

### Why the gateway uses Minimal APIs and the services use controllers

The gateway has almost no endpoints of its own: a proxy, a composed claim view, `/api/gateway/me` and a route list.
Minimal APIs ([`GatewayEndpoints`](../src/gateway/ClaimBridge.Gateway/Endpoints/GatewayEndpoints.cs)) keep those as a
few lines each, with endpoint filters for validation and PHI auditing, and start faster with a smaller footprint,
which matters for the one service that sits in front of every request. The domain services have many endpoints that
share authorization, org scoping and validation per resource; controllers group them by resource, put the policy on
the class and the tighter rules on actions, and give filters such as
[`IdempotencyFilter`](../src/services/claims/ClaimBridge.Claims/Api/IdempotencyFilter.cs) a natural place to attach.

## API Composition

**Problem.** The claim detail page needs data owned by four services.
**Here.** [`ClaimDetailsComposer`](../src/gateway/ClaimBridge.Gateway/Composition/ClaimDetailsComposer.cs) calls claims
(required), and payers-gateway, remittance, scrubber and denial-predictor (optional) in parallel with the caller's
token. If an optional source fails or times out, the response is still returned with `partial: true` and the list of
missing sources, and `gateway.composition.partial` is counted. Exposed as `GET /api/claims/{id}/details`.

## Database per service

**Problem.** Shared tables couple deployments and leak data across boundaries.
**Here.** One SQL Server database and login per service ([`deploy/sqlserver/init.sh`](../deploy/sqlserver/init.sh));
each service owns its EF Core migrations (for example
[`ClaimsDb`](../src/services/claims/ClaimBridge.Claims/Data/ClaimsDb.cs)) or Alembic migrations for the Python
services. See [architecture](architecture.md#data-ownership).

## Saga: orchestration (process manager)

**Problem.** A claim's journey from intake to payment spans scrubber, payers-gateway and remittance over days, with
out-of-order and missing replies.
**Here.** [`ClaimProcessManager`](../src/services/claims/ClaimBridge.Claims/Lifecycle/ClaimProcessManager.cs) consumes
every event about a claim and decides the next step; the allowed moves are the pure table in
[`ClaimLifecycle`](../src/services/claims/ClaimBridge.Claims/Domain/ClaimLifecycle.cs), applied through
[`ClaimTransitions`](../src/services/claims/ClaimBridge.Claims/Application/ClaimTransitions.cs).

### Durable timeouts

**Problem.** "If the payer has not acknowledged in 15 minutes, ask" cannot live in memory.
**Here.** Deadlines are rows (`ClaimLifecycleState`), scanned by
[`LifecycleTimeoutJob`](../src/services/claims/ClaimBridge.Claims/Lifecycle/LifecycleTimeoutJob.cs) and decided by
[`LifecycleTimeoutPolicy`](../src/services/claims/ClaimBridge.Claims/Lifecycle/LifecycleTimeoutPolicy.cs): status
inquiry, one resubmission, then escalation.

## Saga: choreography

**Problem.** Posting a remittance touches remittance, claims, notifications and analytics, none of which needs to
command the others.
**Here.** payers-gateway publishes `payers.remittance-received`;
[`RemittanceReceivedHandler`](../src/services/remittance/ClaimBridge.Remittance/Consumers/RemittanceReceivedHandler.cs)
posts and publishes `remittance.*`; claims, notifications and analytics react independently. See
[remittance and A/R](remittance-and-ar.md#choreography).

## Anti-corruption layer

**Problem.** Payer formats, status words and error codes must not leak into the domain.
**Here.** One adapter per payer behind
[`IPayerAdapter`](../src/services/payers-gateway/ClaimBridge.PayersGateway/Adapters/IPayerAdapter.cs)
([A](../src/services/payers-gateway/ClaimBridge.PayersGateway/Adapters/PayerA/PayerAAdapter.cs),
[B](../src/services/payers-gateway/ClaimBridge.PayersGateway/Adapters/PayerB/PayerBAdapter.cs),
[C](../src/services/payers-gateway/ClaimBridge.PayersGateway/Adapters/PayerC/PayerCAdapter.cs)), and the X12 library
[`ClaimBridge.X12`](../src/shared/ClaimBridge.X12) that turns files into neutral records.

## Specification and rules engine

**Problem.** Scrub rules change often and payers add their own; they must be composable and testable one by one.
**Here.** Built-in rules are classes implementing
[`IScrubRule`](../src/services/scrubber/ClaimBridge.Scrubber/Rules/IScrubRule.cs) run by
[`ScrubEngine`](../src/services/scrubber/ClaimBridge.Scrubber/Rules/ScrubEngine.cs). Payer rules are JSON conditions
compiled by [`PayerRuleCompiler`](../src/services/scrubber/ClaimBridge.Scrubber/Rules/PayerRules/PayerRuleCompiler.cs)
into composite specifications ([`Specification.cs`](../src/services/scrubber/ClaimBridge.Scrubber/Rules/Specifications/Specification.cs):
`All`, `Any`, `Not`, `Where`).

## Transactional outbox

**Problem.** Saving a claim and publishing its event must both happen or neither.
**Here.** Handlers write events with [`EventOutbox`](../src/shared/ClaimBridge.ServiceDefaults/Messaging/EventOutbox.cs)
into an `OutboxMessages` table in the same `SaveChanges`.
[`OutboxDispatcher`](../src/shared/ClaimBridge.ServiceDefaults/Messaging/OutboxDispatcher.cs) relays rows in batches
of 50 with `UPDLOCK, READPAST`, so several replicas can relay without sending the same row twice at once; the
producer ([`KafkaProducer`](../src/shared/ClaimBridge.ServiceDefaults/Messaging/KafkaProducer.cs)) is idempotent.

## Idempotent consumer

**Problem.** Kafka and the outbox are at-least-once.
**Here.** [`KafkaConsumerHost`](../src/shared/ClaimBridge.ServiceDefaults/Messaging/KafkaConsumerHost.cs) records the
envelope id per consumer in `ProcessedEvents` inside the same transaction as the handler's changes, and skips ids it
has seen. Offsets are committed only after the transaction. The Python services do the same in
[`consumer.py`](../src/services/analytics/app/consumer.py).

## Retry topics and dead letter queue

**Problem.** A failing message must neither block its partition nor be lost.
**Here.** On failure `KafkaConsumerHost` republishes to `<topic>.retry` with an `x-attempt` header and an exponential
delay (5 s base); a separate consumer group handles the retry lane; after three attempts, or immediately for an
unreadable message, it goes to `<topic>.dlq` with the error. Topics are created by
[`create-topics.sh`](../deploy/kafka/create-topics.sh).

## Idempotency key and duplicate detection

**Problem.** A practice system that times out will resend; the same claim keyed twice must not be paid twice.
**Here.** Two layers. [`IdempotencyFilter`](../src/services/claims/ClaimBridge.Claims/Api/IdempotencyFilter.cs) stores
`Idempotency-Key` per client with a hash of the body: a repeat returns the stored response with
`Idempotent-Replayed: true`, a different body with the same key is `422`, a repeat while the first is running is
`409`. Independently, [`DuplicateDetector`](../src/services/claims/ClaimBridge.Claims/Application/DuplicateDetector.cs)
compares the business fingerprint ([`ClaimFingerprint`](../src/services/claims/ClaimBridge.Claims/Domain/ClaimFingerprint.cs))
with live claims, and the scrubber's CB011 rule turns a match into an edit.

## Circuit breaker, retry, timeout and bulkhead

**Problem.** One slow or failing payer must not take the others, or our threads, down with it.
**Here.** [`PayerResilience`](../src/services/payers-gateway/ClaimBridge.PayersGateway/Resilience/PayerResilience.cs)
builds a separate pipeline per payer: rate limiter, concurrency limiter (bulkhead), total timeout, retry with jitter,
circuit breaker, attempt timeout. Calls between our own services use the standard resilience handler with short
timeouts (claims in [`Program.cs`](../src/services/claims/ClaimBridge.Claims/Program.cs)). Submissions that hit an
open circuit are parked by
[`SubmissionService`](../src/services/payers-gateway/ClaimBridge.PayersGateway/Consumers/SubmissionService.cs) and
retried by [`DeferredSubmissionJob`](../src/services/payers-gateway/ClaimBridge.PayersGateway/Jobs/DeferredSubmissionJob.cs).

## Rate limiting

**Problem.** Protect ourselves from a runaway client, and protect payers from us.
**Here.** Inbound, [`PerClientRateLimit`](../src/gateway/ClaimBridge.Gateway/RateLimiting/PerClientRateLimit.cs) gives
each API client the limit stored on its profile (`cb_rate_limit` token claim). Outbound, a token bucket per payer in
`PayerResilience` honours each payer's published request rate.

## Claim check

**Problem.** Batch uploads and 835 files are megabytes; Kafka messages should stay small and free of PHI.
**Here.** The payload goes to blob storage through [`BlobStore`](../src/shared/ClaimBridge.ServiceDefaults/Storage/BlobStore.cs)
and the event carries only the reference: `claims.batch-uploaded`
(read by [`BatchFanOutHandler`](../src/services/claims/ClaimBridge.Claims/Batches/BatchFanOutHandler.cs)) and
`payers.remittance-received`. `claims.claim-ready` goes one step further and carries no document at all; the consumer
fetches it over the internal API.

## Polling consumer

**Problem.** Bluepeak drops files in a folder and Cascade never calls back.
**Here.** Quartz jobs in payers-gateway:
[`PayerFilePollingJob`](../src/services/payers-gateway/ClaimBridge.PayersGateway/Files/PayerFilePollingJob.cs) for
files, [`ClaimStatusInquiryJob`](../src/services/payers-gateway/ClaimBridge.PayersGateway/Jobs/ClaimStatusInquiryJob.cs)
for status, [`PayerBBatchWriterJob`](../src/services/payers-gateway/ClaimBridge.PayersGateway/Files/PayerBBatchWriterJob.cs)
for batching outgoing claims.

## Signed webhooks

**Problem.** Receivers must be able to trust and de-duplicate what we send.
**Here.** [`WebhookSignature`](../src/shared/ClaimBridge.Contracts/Webhooks/WebhookSignature.cs) (HMAC over timestamp
and body), [`WebhookDispatcher`](../src/services/notifications/ClaimBridge.Notifications/Webhooks/WebhookDispatcher.cs)
with [`WebhookBackoff`](../src/services/notifications/ClaimBridge.Notifications/Webhooks/WebhookBackoff.cs) and
auto-disable. See [webhooks](webhooks.md).

## CQRS read models

**Problem.** The claim list needs filters and sorting over data that is encrypted or spread over several tables; the
dashboard needs aggregates over everything.
**Here.** [`ClaimReadModelProjector`](../src/services/claims/ClaimBridge.Claims/Application/ClaimReadModelProjector.cs)
keeps a flat [`ClaimSearchEntry`](../src/services/claims/ClaimBridge.Claims/Data/ClaimSearchEntry.cs) per claim,
updated in the same transaction as each status change. The denial worklist
([`DenialWorklist`](../src/services/claims/ClaimBridge.Claims/Denials/DenialWorklist.cs)) is another read model, and
analytics builds its own facts from events in [`projections.py`](../src/services/analytics/app/projections.py).

## Event-sourced history (light)

**Problem.** "Why is this claim denied and who touched it?" needs a complete, trustworthy timeline.
**Here.** The claim stores its current state, and every transition also appends a `ClaimStatusEvent`
([`Claim.Fire`](../src/services/claims/ClaimBridge.Claims/Domain/Claim.cs)) that is never updated or deleted. The
timeline endpoint reads only those rows. It is not full event sourcing: state is not rebuilt from events.

## Cache-aside

**Problem.** Code sets, payer rules, enrollment checks and eligibility answers are read constantly and change rarely.
**Here.** `HybridCache` (in-process plus Redis) with explicit expirations:
[`CodeSetProvider`](../src/services/scrubber/ClaimBridge.Scrubber/CodeSetsCache/CodeSetSnapshot.cs) (1 h),
[`PayerRuleProvider`](../src/services/scrubber/ClaimBridge.Scrubber/Rules/PayerRules/PayerRuleProvider.cs) (15 min,
invalidated on change), enrollment in claims'
[`ProvidersClient`](../src/services/claims/ClaimBridge.Claims/Application/Clients/ProvidersClient.cs) (5 min,
invalidated by `providers.enrollment-changed`), eligibility in
[`EligibilityService`](../src/services/payers-gateway/ClaimBridge.PayersGateway/Api/EligibilityService.cs) (5 min).

## Distributed tracing, health checks and externalised configuration

- **Tracing.** [`TelemetryExtensions`](../src/shared/ClaimBridge.ServiceDefaults/Telemetry/TelemetryExtensions.cs)
  sets up OpenTelemetry for ASP.NET Core, outgoing HTTP (which also covers gRPC calls), YARP and the messaging layer,
  and strips query strings from spans because search queries can hold member ids; the Kafka producer writes `traceparent` into
  message headers and the consumer continues the trace, so one claim shows as one trace across services in Jaeger.
- **Health checks.** [`HealthCheckExtensions`](../src/shared/ClaimBridge.ServiceDefaults/Health/HealthCheckExtensions.cs):
  `/alive` (process only, for liveness) and `/health` (database, Redis, Kafka, blob, for readiness).
- **Configuration.** Every setting has a default in `appsettings.json` and is overridden by environment variables
  (`ConnectionStrings__ClaimsDb`, `Payers__PAYER-C__BreakSeconds`), set by Compose locally and by the Helm configmap
  and secrets in Kubernetes.
