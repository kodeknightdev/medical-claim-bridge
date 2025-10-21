import { Injectable } from '@angular/core';
import { AbstractSecurityStorage } from 'angular-auth-oidc-client';

/**
 * Tokens live only in memory. A reload goes back through /connect/authorize, which completes silently
 * while the identity session cookie is valid.
 */
@Injectable()
export class MemoryStorage implements AbstractSecurityStorage {
  private readonly store = new Map<string, string>();

  read(key: string): string | null {
    return this.store.get(key) ?? null;
  }

  write(key: string, value: string): void {
    this.store.set(key, value);
  }

  remove(key: string): void {
    this.store.delete(key);
  }

  clear(): void {
    this.store.clear();
  }
}
