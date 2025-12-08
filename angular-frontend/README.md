# portal

Angular 19 portal for practices and clearinghouse staff, with Angular Material and ECharts.

- Sign-in with OpenID Connect (code + PKCE); tokens are kept in memory only
- Claim entry with inline scrub edits, eligibility and denial risk while typing
- Claim list, detail timeline, batches, eligibility checks, denials and appeals, remittances, enrollments,
  integrations (API clients and webhooks), dashboard, ops screens for payer health and reconciliation

```bash
npm ci
npm start          # http://localhost:4200, proxies /api to the gateway on 5080
npm run test:ci    # Karma in headless Chrome
```

In Docker the built app is served by nginx on 8080, which also proxies `/api` to the gateway.
