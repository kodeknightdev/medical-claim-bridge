"""Python equivalent of the receiver's signature check, for provider teams that don't use .NET."""
import hashlib
import hmac
import time


def verify(secret: str, header: str, body: bytes, tolerance_seconds: int = 300) -> bool:
    parts = dict(p.split("=", 1) for p in header.split(",") if "=" in p)
    timestamp = int(parts.get("t", "0"))
    if abs(time.time() - timestamp) > tolerance_seconds:
        return False
    expected = hmac.new(secret.encode(), f"{timestamp}.".encode() + body, hashlib.sha256).hexdigest()
    return hmac.compare_digest(expected, parts.get("v1", ""))
