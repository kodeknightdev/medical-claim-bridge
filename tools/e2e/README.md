# e2e

End-to-end scenarios against a running compose stack.

```bash
pip install -r requirements.txt
python scenarios.py
```

Covers a paid Payer A claim with verified webhooks, idempotent replay, a scrub failure fixed and paid, a Payer B X12
batch with a partial payment, a Payer C outage with the circuit opening and recovering, and a denial corrected and
paid. Uses the gateway on 5080, the simulator on 5109 and the webhook receiver on 5190.
