"""End-to-end scenarios against a running stack (docker compose or local). Synthetic data only.

    python tools/e2e/scenarios.py --gateway http://localhost:5000 --sim http://localhost:5109 --receiver http://localhost:5190
"""
from __future__ import annotations

import argparse
import random
import sys
import time
import uuid
from datetime import date, timedelta
from pathlib import Path

import requests

SAMPLES = Path(__file__).resolve().parents[1] / "samples"
SUNRISE_NPI, RENDERING_NPI = "1999000015", "1999000114"
MEMBERS = {
    "A": ("PAYER-A", "NWH1000002", "Jamie", "Example", "1958-02-12", "F"),
    "B": ("PAYER-B", "BPK1000003", "Taylor", "Testwell", "1962-03-13", "M"),
    "C": ("PAYER-C", "CCI1000004", "Morgan", "Demoson", "1966-04-14", "F"),
}


class Stack:
    def __init__(self, args):
        self.gw, self.sim, self.receiver = args.gateway.rstrip("/"), args.sim.rstrip("/"), args.receiver.rstrip("/")
        self.secret = args.client_secret
        self.token = self._token()
        self.used_days: set[int] = set()

    def _token(self) -> str:
        r = requests.post(f"{self.gw}/connect/token", auth=("sunrise-pms", self.secret),
                          data={"grant_type": "client_credentials", "scope": "claims.submit claims.read"}, timeout=10)
        r.raise_for_status()
        return r.json()["access_token"]

    @property
    def h(self) -> dict:
        return {"Authorization": f"Bearer {self.token}"}

    def dos(self) -> str:
        # distinct service dates keep scenarios from tripping the duplicate check on each other
        while True:
            d = random.randint(2, 60)
            if d not in self.used_days:
                self.used_days.add(d)
                return (date.today() - timedelta(days=d)).isoformat()

    def claim(self, member: str, lines: list[tuple], dx: list[str], prior_auth: str | None = None) -> dict:
        payer, member_id, first, last, dob, sex = MEMBERS[member]
        dos = self.dos()
        return {"payerId": payer, "billingNpi": SUNRISE_NPI, "renderingNpi": RENDERING_NPI,
                "patient": {"memberId": member_id, "firstName": first, "lastName": last, "dateOfBirth": dob, "gender": sex},
                "diagnoses": dx, "priorAuthNumber": prior_auth,
                "lines": [{"procedureCode": code, "modifiers": mods, "units": units, "charge": charge, "serviceDate": dos,
                           "placeOfService": "11", "dxPointers": ptrs} for code, mods, units, charge, ptrs in lines]}

    def submit(self, body: dict, key: str | None = None) -> requests.Response:
        return requests.post(f"{self.gw}/api/v1/claims", json=body, timeout=15,
                             headers={**self.h, "Idempotency-Key": key or str(uuid.uuid4())})

    def status(self, claim_id: str) -> dict:
        return requests.get(f"{self.gw}/api/v1/claims/{claim_id}", headers=self.h, timeout=10).json()

    def wait(self, claim_id: str, statuses: set[str], timeout: int = 240) -> dict:
        deadline = time.time() + timeout
        while time.time() < deadline:
            s = self.status(claim_id)
            if s["status"] in statuses:
                return s
            time.sleep(3)
        raise AssertionError(f"claim {claim_id} stuck in {self.status(claim_id)['status']}, wanted {statuses}")

    def scenario(self, **settings) -> None:
        current = requests.get(f"{self.sim}/sim/scenarios", timeout=5).json()
        current.update(settings)
        requests.post(f"{self.sim}/sim/scenarios", json=current, timeout=5).raise_for_status()


def check(name: str, condition: bool, detail: str = "") -> None:
    print(f"  {'ok ' if condition else 'FAIL'} {name} {detail}")
    if not condition:
        raise AssertionError(name)


def payer_a_paid_with_signed_webhook(s: Stack) -> None:
    claim = s.submit(s.claim("A", [("99213", [], 1, 120.0, [1])], ["I10"])).json()
    final = s.wait(claim["claimId"], {"Closed", "Posted", "Denied"})
    check("claim paid and closed", final["status"] == "Closed", final["status"])
    hooks = requests.get(f"{s.receiver}/webhooks", timeout=5).json()
    mine = [w for w in hooks if w["body"]["data"].get("claimId") == claim["claimId"]]
    check("webhooks delivered", {"claim.accepted", "claim.paid", "remittance.posted"} <= {w["eventType"] for w in mine})
    check("signatures verified by receiver", all(w["signatureValid"] for w in mine))


def payer_b_batch_with_partial_payment(s: Stack) -> None:
    text = (SAMPLES / "payer-b-batch.837.x12").read_text().replace("20251020", (date.today() - timedelta(days=4)).strftime("%Y%m%d"))
    r = requests.post(f"{s.gw}/api/batches", headers=s.h, files={"file": ("payer-b-batch.837.x12", text, "application/edi-x12")}, timeout=15)
    check("batch accepted", r.status_code == 202, str(r.status_code))
    batch_id = r.json()["id"]
    for _ in range(40):
        batch = requests.get(f"{s.gw}/api/batches/{batch_id}", headers=s.h, timeout=5).json()
        if batch["status"] == "Completed":
            break
        time.sleep(2)
    check("batch fanned out", batch["accepted"] == 2, f"{batch['accepted']} accepted, errors={batch.get('errors')}")

    claims = requests.get(f"{s.gw}/api/claims", params={"search": "", "pageSize": 50}, headers=s.h, timeout=5).json()["items"]
    ours = [c for c in claims if c["source"] == "Batch" and c["payerId"] == "PAYER-B"][:2]
    finals = [s.wait(c["claimId"], {"Closed", "Posted", "Denied"}, timeout=300) for c in ours]
    check("both claims posted via 999/277CA/835", all(f["status"] in ("Closed", "Posted") for f in finals), str([f["status"] for f in finals]))
    postings = [requests.get(f"{s.gw}/api/remittances/by-claim/{c['claimId']}", headers=s.h, timeout=5).json() for c in ours]
    kinds = {p[0]["kind"] for p in postings if p}
    check("partial payment posted", "PartialPayment" in kinds, str(kinds))


def payer_c_outage_and_recovery(s: Stack) -> None:
    s.scenario(payerCOutage=True, payerCFailureRate=0.0, payerCTimeoutRate=0.0)
    claim = s.submit(s.claim("C", [("99213", [], 1, 120.0, [1])], ["J02.9"])).json()
    opened = False
    for _ in range(40):
        health = requests.get(f"{s.gw}/api/payers/health", headers=s.h, timeout=5).json()
        c = next(p for p in health if p["payerId"] == "PAYER-C")
        if c["health"]["circuitState"] == "Open" or c["deferred"] > 0:
            opened = c["health"]["circuitState"] == "Open" or opened
            if opened:
                break
        # eligibility checks during the outage also count toward the breaker
        requests.post(f"{s.gw}/api/eligibility", headers=s.h, timeout=10,
                      json={"payerId": "PAYER-C", "memberId": "CCI1000004", "dateOfBirth": "1966-04-14", "dateOfService": s.dos()})
        time.sleep(2)
    check("circuit opened during outage", opened)
    check("claim parked, not lost", s.status(claim["claimId"])["status"] == "ReadyToSubmit")
    s.scenario(payerCOutage=False)
    final = s.wait(claim["claimId"], {"AcceptedByPayer", "Closed", "Posted", "Pended"}, timeout=300)
    check("submitted after recovery", final["status"] in ("AcceptedByPayer", "Closed", "Posted", "Pended"), final["status"])


def scrub_failure_fixed(s: Stack) -> None:
    body = s.claim("A", [("99214", [], 1, 175.0, [3])], ["E11.9"])
    claim = s.submit(body).json()
    failed = s.wait(claim["claimId"], {"ScrubFailed", "ReadyToSubmit", "Submitted"})
    check("scrub failed on bad pointer", failed["status"] == "ScrubFailed")
    body["lines"][0]["dxPointers"] = [1]
    r = requests.put(f"{s.gw}/api/claims/{claim['claimId']}", json=body, headers=s.h, timeout=10)
    check("claim corrected", r.status_code == 200, str(r.status_code))
    final = s.wait(claim["claimId"], {"Closed", "Posted", "Denied"})
    check("resubmitted and paid", final["status"] == "Closed", final["status"])


def denial_corrected_and_paid(s: Stack) -> None:
    s.scenario(payerCFailureRate=0.0, payerCTimeoutRate=0.0, payerCOutage=False)
    body = s.claim("C", [("99204", [], 1, 250.0, [1]), ("73721", ["RT"], 1, 1350.0, [1])], ["M23.221"])
    claim = s.submit(body).json()
    denied = s.wait(claim["claimId"], {"Denied", "Closed"}, timeout=300)
    check("denied for missing prior auth", denied["status"] == "Denied", denied["status"])
    body["priorAuthNumber"] = "PA778899"
    r = requests.post(f"{s.gw}/api/claims/{claim['claimId']}/correct", json=body, headers=s.h, timeout=10)
    check("corrected claim created", r.status_code == 201, r.text[:200])
    corrected = r.json()
    check("original superseded", s.status(claim["claimId"])["status"] == "Superseded")
    final = s.wait(corrected["id"], {"Closed", "Posted", "Denied"}, timeout=300)
    check("corrected claim paid", final["status"] == "Closed", final["status"])


def idempotent_api_submission(s: Stack) -> None:
    body = s.claim("A", [("99212", [], 1, 75.0, [1])], ["J06.9"])
    key = f"e2e-{uuid.uuid4()}"
    first, second = s.submit(body, key), s.submit(body, key)
    check("same response replayed", first.json() == second.json() and second.headers.get("Idempotent-Replayed") == "true")
    body["lines"][0]["charge"] = 80.0
    check("different body with same key rejected", s.submit(body, key).status_code == 422)


SCENARIOS = [payer_a_paid_with_signed_webhook, idempotent_api_submission, scrub_failure_fixed, payer_b_batch_with_partial_payment,
             payer_c_outage_and_recovery, denial_corrected_and_paid]


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--gateway", default="http://localhost:5000")
    p.add_argument("--sim", default="http://localhost:5109")
    p.add_argument("--receiver", default="http://localhost:5190")
    p.add_argument("--client-secret", default="demo-sunrise-secret-change-me")
    p.add_argument("--only", default=None)
    args = p.parse_args()

    stack = Stack(args)
    stack.scenario(randomDenialRate=0.0, payerCFailureRate=0.0, payerCTimeoutRate=0.0, adjudicationDelaySeconds=15)
    failures = 0
    for scenario in SCENARIOS:
        if args.only and args.only not in scenario.__name__:
            continue
        print(scenario.__name__.replace("_", " "))
        try:
            scenario(stack)
        except AssertionError as exc:
            failures += 1
            print(f"  -> failed: {exc}")
    stack.scenario(randomDenialRate=0.03, payerCFailureRate=0.15, payerCTimeoutRate=0.05, adjudicationDelaySeconds=20)
    print("all scenarios passed" if failures == 0 else f"{failures} scenario(s) failed")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
