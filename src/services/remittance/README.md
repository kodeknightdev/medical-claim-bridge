# remittance

Turns 835 files into postings and keeps accounts receivable.

- Reads each 835 by reference, posts every claim that matches and balances, parks the rest for reconciliation
- Classifies payment, partial payment, denial and reversal; maps CARCs to denial categories
- A/R balance per claim, ageing buckets, days in A/R, nightly snapshots
- Remittance PDFs with QuestPDF

| | |
|---|---|
| Database | `cb_remittance` |
| Port | 5106 on the host |
| Consumes | `payers.remittance-received`, `claims.status-changed` |
| Publishes | `remittance.payment-posted`, `remittance.claim-denied`, `remittance.reversal-posted` |
| Tests | `tests/ClaimBridge.Remittance.Tests` |
