import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { CurrentUser, Role } from './current-user';

export function roleGuard(...roles: Role[]): CanActivateFn {
  return () => {
    const user = inject(CurrentUser);
    return user.hasAnyRole(...roles) ? true : inject(Router).createUrlTree(['/']);
  };
}
