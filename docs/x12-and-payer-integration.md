# X12 and payer integration

Every payer speaks a different dialect. claim-bridge keeps all of that in one service,
[`payers-gateway`](../src/services/payers-gateway/ClaimBridge.PayersGateway), behind one adapter per payer. The rest of
the system only ever sees neutral events (`payers.claim-submitted`, `payers.ack-received`, `payers.status-updated`,
`payers.remittance-received`).

## The three payers

All three are mocked by [`payer-sim`](../tools/payer-sim/ClaimBridge.PayerSim), which runs one simulated payer per
route prefix and writes files into Azurite blob containers that stand in for SFTP folders.

| | Northwind Health Plan (`PAYER-A`) | Bluepeak Mutual (`PAYER-B`) | Cascade Care (`PAYER-C`) |
|---|---|---|---|
| Submission | JSON API, synchronous accept/reject | 837P batch file dropped in `payer-b/inbound/` | JSON API, slow and unreliable on purpose |
| Claim acknowledgement | in the submit response | 999 (file level) and 277CA (claim level) files, in either order | in the submit response |
| Status | push: callback to `/api/payers/payer-a/callbacks/adjudication` with an `X-Northwind-Token` header | 277-style answers from a small JSON API | pull: scheduled status inquiries |
| Remittance | 835 file in `payer-a/outbound/` | 835 file in `payer-b/outbound/` | 835 file in `payer-c/outbound/` |
| Timely filing | 90 days | 180 days | 365 days |
| Rate limit / concurrency | 20 req/s, 8 | 10 req/s, 4 | 5 req/s, 2, 2 s attempt timeout |

The simulator's adjudication rules ([`Engine`](../tools/payer-sim/ClaimBridge.PayerSim/Engine)) are simple and
deterministic enough to build the demo on: the allowed amount is a share of the charge, the member's deductible
applies first and then copay or coinsurance; Northwind covers office visits with a copay before the deductible;
Bluepeak bundles venipuncture (CO-97), caps therapy at four units (CO-151) and refuses modifier 59 (CO-4); and a
configurable share of new claims is denied, chosen by a hash of the claim number so runs are reproducible. The control
endpoints under `/sim` (`/sim/scenarios` for denial rate and latency, `/sim/payer-c/outage`, `/sim/members`) are what
the e2e scenarios use.

## Adapters as an anti-corruption layer

[`IPayerAdapter`](../src/services/payers-gateway/ClaimBridge.PayersGateway/Adapters/IPayerAdapter.cs) has four
operations: submit, check eligibility, ask for status, and say which channel the payer uses. Each adapter translates in
both directions:

- outgoing, from the neutral [`SubmissionDocument`](../src/shared/ClaimBridge.Contracts/Submission/SubmissionDocument.cs)
  to the payer's JSON models or 837P;
- incoming, from the payer's words to our vocabulary. Northwind's `RECEIVED / PENDED / IN_REVIEW / FINALIZED` become
  the 277-style `received / pended / in-process / finalized` with a status code
  ([`PayerAAdapter.Translate`](../src/services/payers-gateway/ClaimBridge.PayersGateway/Adapters/PayerA/PayerAAdapter.cs)).
  Rejection codes are rendered as `code: text` strings before they leave the adapter.

Nothing payer-specific crosses the adapter boundary. A transport problem (timeout, 5xx, open circuit) is always
raised as `PayerUnavailableException`, which the caller handles the same way for every payer.

## Submission flow

1. claims publishes `claims.claim-ready` with only the claim id and attempt. The document is not in the event; it
   contains PHI.
2. [`ClaimReadyHandler`](../src/services/payers-gateway/ClaimBridge.PayersGateway/Consumers/ClaimReadyHandler.cs)
   fetches it from `GET /internal/claims/{id}/submission` and records a `Submission` row.
3. [`SubmissionService`](../src/services/payers-gateway/ClaimBridge.PayersGateway/Consumers/SubmissionService.cs)
   calls the adapter:
   - JSON payers answer straight away; the result becomes `payers.claim-submitted` and `payers.ack-received`.
   - Bluepeak returns `Queued`. [`PayerBBatchWriterJob`](../src/services/payers-gateway/ClaimBridge.PayersGateway/Files/PayerBBatchWriterJob.cs)
     collects queued submissions every 15 seconds into one 837P file, uploads it to `payer-b/inbound/` and keeps a
     copy in `x12-archive`.
   - If the payer is unavailable the submission is parked as `Deferred` with a backoff of 15 s, 30 s, 1 min, 2 min,
     then 5 min. [`DeferredSubmissionJob`](../src/services/payers-gateway/ClaimBridge.PayersGateway/Jobs/DeferredSubmissionJob.cs)
     retries due ones, 20 at a time. Parking in the database instead of retrying the Kafka message keeps a long outage
     from filling the retry topics.
4. [`PayerFilePollingJob`](../src/services/payers-gateway/ClaimBridge.PayersGateway/Files/PayerFilePollingJob.cs)
   lists every payer's `outbound/` folder every 10 seconds. Each file is identified by sniffing its transaction set
   and handled on its own, because Bluepeak sends the 277CA before the 999 about half the time. Processed files move to
   `outbound/processed/`, files that fail to parse move to `outbound/error/` and show up on the ops screen.
5. [`ClaimStatusInquiryJob`](../src/services/payers-gateway/ClaimBridge.PayersGateway/Jobs/ClaimStatusInquiryJob.cs)
   sends status inquiries for claims that were accepted but have been silent for 10 minutes or are pended. claims can
   also ask for one through `POST /internal/status-inquiries` when its acknowledgement deadline expires (see
   [claim lifecycle](claim-lifecycle.md#durable-timeouts)).

## Resilience per payer

Each adapter's `HttpClient` gets its own pipeline built in
[`PayerResilience`](../src/services/payers-gateway/ClaimBridge.PayersGateway/Resilience/PayerResilience.cs), outermost
first:

1. token bucket rate limiter (`RequestsPerSecond`, queue of five seconds' worth);
2. concurrency limiter (`MaxConcurrency`), the bulkhead that stops one slow payer from using every connection;
3. total timeout covering all attempts;
4. retry with exponential backoff and jitter (`RetryCount`, 300 ms base);
5. circuit breaker: opens when half the calls in 20 seconds fail with at least 4 calls, stays open for `BreakSeconds`;
6. per-attempt timeout (`AttemptTimeoutSeconds`).

[`PayerHealthTracker`](../src/services/payers-gateway/ClaimBridge.PayersGateway/Resilience/PayerHealthTracker.cs)
records latency, error rate and circuit state per payer. `GET /api/payers/health` returns it for the ops screen, and
analytics samples it for the payer health chart. All values come from the `Payers` section in
[`appsettings.json`](../src/services/payers-gateway/ClaimBridge.PayersGateway/appsettings.json).

## The X12 subset

[`ClaimBridge.X12`](../src/shared/ClaimBridge.X12) is a small reader and writer for the transactions this project
needs. It follows the 005010 structure but is not a full implementation guide: there is no TA1, no 276 request file,
no institutional or dental claims, and loops are only as deep as the demo needs.

| Transaction | Class | Direction | Segments used |
|---|---|---|---|
| 837P professional claim | [`Claim837P.cs`](../src/shared/ClaimBridge.X12/Transactions/Claim837P.cs) | out (and in, for batch upload) | BHT, NM1, PER, HL, N3, N4, REF, SBR, DMG, CLM, DTP, HI, LX, SV1 |
| 999 implementation ack | [`Ack999.cs`](../src/shared/ClaimBridge.X12/Transactions/Ack999.cs) | in | AK1, AK2, IK3, IK4, IK5, AK9 |
| 277CA claim ack | [`ClaimAck277CA.cs`](../src/shared/ClaimBridge.X12/Transactions/ClaimAck277CA.cs) | in | BHT, HL, NM1, TRN, STC, REF |
| 835 remittance | [`Remittance835.cs`](../src/shared/ClaimBridge.X12/Transactions/Remittance835.cs) | in | BPR, TRN, DTM, N1, CLP, CAS, NM1, MIA/MOA, AMT, SVC, DTP |

[`X12Reader`](../src/shared/ClaimBridge.X12/Core/X12Reader.cs) takes the delimiters from the ISA segment and checks
the envelopes: ISA/IEA control numbers, GS/GE counts and control numbers, ST/SE counts. An envelope error raises
`X12ParseException` with the segment position, which is what sends a file to `outbound/error/`.
[`X12Writer`](../src/shared/ClaimBridge.X12/Core/X12Writer.cs) fills in counts and control numbers, strips characters
that would collide with delimiters, and writes ICD-10 codes without the dot (`M17.11` becomes `M1711`).

Example files live in [`tools/samples`](../tools/samples): a Bluepeak batch, accepted and rejected 999s, a 277CA, a
normal 835 and a deliberately malformed one. The X12 tests snapshot the writer's output with Verify, so any change to
the generated 837P shows up as a diff in review.

In production Bluepeak's folders would be the SFTP-enabled storage account from
[`infra/terraform/modules/storage`](../infra/terraform/modules/storage/main.tf); the code only sees blob containers
either way.
