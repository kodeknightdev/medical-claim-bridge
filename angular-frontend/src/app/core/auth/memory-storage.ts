import { Injectable } from '@angular/core';
import { AbstractSecurityStorage } from 'angular-auth-oidc-client';

/** Keys holding tokens or user claims; these never leave memory. */
export const SENSITIVE_KEYS = ['authnResult', 'authzData', 'reusable_refresh_token', 'userData', 'access_token_expires_at'];

/**
 * Tokens live only in memory. The redirect-flow values (state, nonce, PKCE verifier) have to survive the
 * round trip to the identity server, so a copy without tokens goes to sessionStorage. A reload goes back
 * through /connect/authorize, which completes silently while the identity session cookie is valid.
 */
@Injectable()
export class MemoryStorage implements AbstractSecurityStorage {
  private readonly store = new Map<string, string>();

  read(key: string): string | null {
    return this.store.get(key) ?? sessionStorage.getItem(key);
  }

  write(key: string, value: string): void {
    this.store.set(key, value);
    sessionStorage.setItem(key, stripTokens(value));
  }

  remove(key: string): void {
    this.store.delete(key);
    sessionStorage.removeItem(key);
  }

  clear(): void {
    for (const key of this.store.keys()) sessionStorage.removeItem(key);
    this.store.clear();
  }
}

export function stripTokens(value: string): string {
  try {
    const parsed = JSON.parse(value) as Record<string, unknown>;
    if (parsed && typeof parsed === 'object') {
      for (const k of SENSITIVE_KEYS) delete parsed[k];
      return JSON.stringify(parsed);
    }
  } catch {
    // not JSON, nothing token-shaped to strip
  }
  return value;
}
