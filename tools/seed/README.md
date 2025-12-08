# seed

Generates the demo history and loads it into the running stack. Runs once as the `seed` container.

- 5,000 synthetic claims across three practices and three payers, with payments, denials and appeals
- Uploads the history to blob storage and calls each service's `/internal/seed/history`
- Trains the first denial model

Generation is deterministic for a given seed value, so every fresh stack has the same history.
