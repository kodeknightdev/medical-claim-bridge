import { computed, inject, Injectable, signal } from '@angular/core';
import { OidcSecurityService } from 'angular-auth-oidc-client';

export type Role = 'Admin' | 'OpsAnalyst' | 'ProviderAdmin' | 'ProviderBiller';

export interface UserInfo {
  name: string;
  email: string;
  roles: Role[];
  orgId: string | null;
}

export function toUserInfo(claims: Record<string, unknown> | null | undefined): UserInfo | null {
  if (!claims) return null;
  const role = claims['role'];
  const roles = (Array.isArray(role) ? role : role ? [role] : []) as Role[];
  return {
    name: String(claims['name'] ?? claims['preferred_username'] ?? ''),
    email: String(claims['email'] ?? ''),
    roles,
    orgId: (claims['org_id'] as string | undefined) ?? null
  };
}

@Injectable({ providedIn: 'root' })
export class CurrentUser {
  private readonly oidc = inject(OidcSecurityService);
  readonly user = signal<UserInfo | null>(null);

  readonly isStaff = computed(() => this.hasAnyRole('Admin', 'OpsAnalyst'));
  readonly isAdmin = computed(() => this.hasAnyRole('Admin'));
  readonly canManageOrg = computed(() => this.hasAnyRole('Admin', 'ProviderAdmin'));

  constructor() {
    this.oidc.userData$.subscribe(({ userData }) => this.user.set(toUserInfo(userData)));
  }

  hasAnyRole(...roles: Role[]): boolean {
    const mine = this.user()?.roles ?? [];
    return roles.some(r => mine.includes(r));
  }

  login(): void {
    this.oidc.authorize();
  }

  logout(): void {
    this.oidc.logoffAndRevokeTokens().subscribe();
  }
}
