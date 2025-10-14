"""Bearer token validation against the identity service. Keys come from the internal JWKS, the issuer is public."""
from __future__ import annotations

from dataclasses import dataclass
from functools import lru_cache

import jwt
from fastapi import Depends, HTTPException, Request, status

from .config import get_settings

STAFF_ROLES = {"Admin", "OpsAnalyst"}


@dataclass(frozen=True)
class Principal:
    subject: str
    roles: frozenset[str]
    org_id: str | None
    scopes: frozenset[str]

    @property
    def is_staff(self) -> bool:
        return bool(self.roles & STAFF_ROLES)

    def org_filter(self, requested: str | None = None) -> str | None:
        """None means unrestricted (staff); provider users are pinned to their own org."""
        return requested if self.is_staff else (self.org_id or "00000000-0000-0000-0000-000000000000")


@lru_cache
def _jwks() -> jwt.PyJWKClient:
    authority = get_settings().auth_authority.rstrip("/")
    return jwt.PyJWKClient(f"{authority}/.well-known/jwks", cache_keys=True, lifespan=600)


def current_principal(request: Request) -> Principal:
    settings = get_settings()
    if settings.auth_disabled:
        return Principal("dev", frozenset({"Admin"}), None, frozenset({"api", "claims.read"}))

    header = request.headers.get("Authorization", "")
    if not header.startswith("Bearer "):
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Missing bearer token")
    token = header.removeprefix("Bearer ")
    try:
        key = _jwks().get_signing_key_from_jwt(token)
        claims = jwt.decode(token, key.key, algorithms=["RS256"], audience=settings.auth_audience, issuer=settings.auth_issuer,
                            leeway=30)
    except jwt.PyJWTError as exc:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, f"Invalid token: {exc}") from exc

    roles = claims.get("role", [])
    roles = [roles] if isinstance(roles, str) else roles
    return Principal(claims.get("sub", ""), frozenset(roles), claims.get("org_id"), frozenset(claims.get("scope", "").split()))


def require_read(principal: Principal = Depends(current_principal)) -> Principal:
    if not ({"api", "claims.read"} & principal.scopes) and not principal.is_staff:
        raise HTTPException(status.HTTP_403_FORBIDDEN, "claims.read scope required")
    return principal


def require_staff(principal: Principal = Depends(current_principal)) -> Principal:
    if not principal.is_staff:
        raise HTTPException(status.HTTP_403_FORBIDDEN, "Clearinghouse staff only")
    return principal


def require_admin(principal: Principal = Depends(current_principal)) -> Principal:
    if "Admin" not in principal.roles:
        raise HTTPException(status.HTTP_403_FORBIDDEN, "Admin only")
    return principal
