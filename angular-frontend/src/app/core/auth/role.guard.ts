import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { filter, map, take } from 'rxjs';
import { Role, toUserInfo } from './current-user';

/** Waits for the user's claims (they arrive after the code exchange) before deciding. */
export function roleGuard(...roles: Role[]): CanActivateFn {
  return () => {
    const router = inject(Router);
    return inject(OidcSecurityService).userData$.pipe(
      map(({ userData }) => toUserInfo(userData)),
      filter(user => user !== null),
      take(1),
      map(user => roles.some(r => user!.roles.includes(r)) ? true : router.createUrlTree(['/']))
    );
  };
}
