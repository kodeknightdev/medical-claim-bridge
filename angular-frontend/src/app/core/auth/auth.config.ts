import { LogLevel, OpenIdConfiguration } from 'angular-auth-oidc-client';
import { environment } from '../../../environments/environment';

export const authConfig: OpenIdConfiguration = {
  authority: environment.authority,
  redirectUrl: `${window.location.origin}/callback`,
  postLogoutRedirectUri: `${window.location.origin}/`,
  clientId: environment.clientId,
  scope: 'openid profile email roles offline_access api claims.read claims.submit',
  responseType: 'code',
  // refresh tokens rotate on every use; renew a little before the 15 minute access token expires
  silentRenew: true,
  useRefreshToken: true,
  renewTimeBeforeTokenExpiresInSeconds: 60,
  ignoreNonceAfterRefresh: true,
  triggerRefreshWhenIdTokenExpired: false,
  secureRoutes: [environment.apiBase],
  logLevel: environment.production ? LogLevel.Warn : LogLevel.Error
};
