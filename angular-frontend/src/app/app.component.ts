import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { CurrentUser, Role } from './core/auth/current-user';

const RETURN_URL_KEY = 'cb.returnUrl';

interface NavItem {
  path: string;
  label: string;
  icon: string;
  roles?: Role[];
}

export const NAV: NavItem[] = [
  { path: '/', label: 'Dashboard', icon: 'insights' },
  { path: '/claims', label: 'Claims', icon: 'receipt_long' },
  { path: '/claims/new', label: 'New claim', icon: 'note_add', roles: ['Admin', 'ProviderAdmin', 'ProviderBiller'] },
  { path: '/batches', label: 'Batch upload', icon: 'upload_file' },
  { path: '/eligibility', label: 'Eligibility', icon: 'verified_user' },
  { path: '/denials', label: 'Denials & appeals', icon: 'report' },
  { path: '/remittances', label: 'Remittances', icon: 'payments' },
  { path: '/enrollment', label: 'Providers & enrollment', icon: 'badge' },
  { path: '/integrations', label: 'API clients & webhooks', icon: 'hub', roles: ['Admin', 'OpsAnalyst', 'ProviderAdmin'] },
  { path: '/ops/payers', label: 'Payer health', icon: 'monitor_heart', roles: ['Admin', 'OpsAnalyst'] },
  { path: '/ops/reconciliation', label: 'Reconciliation', icon: 'rule', roles: ['Admin', 'OpsAnalyst'] }
];

@Component({
  selector: 'cb-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatSidenavModule, MatListModule, MatIconModule, MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (ready()) {
      <mat-sidenav-container class="shell">
        <mat-sidenav mode="side" opened class="nav app-nav">
          <div class="brand">claim-bridge</div>
          <mat-nav-list>
            @for (item of visibleNav(); track item.path) {
              <a mat-list-item [routerLink]="item.path" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: item.path === '/' || item.path === '/claims' }">
                <mat-icon matListItemIcon>{{ item.icon }}</mat-icon>
                <span matListItemTitle>{{ item.label }}</span>
              </a>
            }
          </mat-nav-list>
          <div class="disclaimer">Demo only · synthetic data · simplified X12, not certified</div>
        </mat-sidenav>
        <mat-sidenav-content>
          <mat-toolbar class="top">
            <span class="spacer"></span>
            <span class="who">{{ user.user()?.name }} · {{ user.user()?.roles?.join(', ') }}</span>
            <button mat-button (click)="user.logout()"><mat-icon>logout</mat-icon> Sign out</button>
          </mat-toolbar>
          <router-outlet />
        </mat-sidenav-content>
      </mat-sidenav-container>
    } @else if (failed()) {
      <div class="loading">Sign-in failed: {{ failed() }} <button mat-button (click)="user.login()">Try again</button></div>
    } @else {
      <div class="loading">Signing in…</div>
    }
  `,
  styles: `
    .shell { height: 100vh; }
    .nav { width: 248px; background: #0f2742; color: #fff; }
    .nav a { color: #d5e2f1; }
    .nav a.active { background: rgba(255,255,255,.12); color: #fff; }
    .brand { font-size: 20px; font-weight: 500; padding: 18px 20px 10px; letter-spacing: .3px; }
    .disclaimer { position: absolute; bottom: 10px; left: 16px; right: 16px; font-size: 11px; color: #8fa6c0; }
    .top { background: #fff; border-bottom: 1px solid #e2e6ec; position: sticky; top: 0; z-index: 2; }
    .who { font-size: 13px; color: #4a5462; margin-right: 8px; }
    .loading { padding: 40px; font-size: 16px; color: #4a5462; }
  `
})
export class AppComponent implements OnInit {
  private readonly oidc = inject(OidcSecurityService);
  private readonly router = inject(Router);
  readonly user = inject(CurrentUser);
  readonly ready = signal(false);
  readonly failed = signal<string | null>(null);

  visibleNav(): NavItem[] {
    return NAV.filter(n => !n.roles || this.user.hasAnyRole(...n.roles));
  }

  ngOnInit(): void {
    this.oidc.checkAuth().subscribe({
      next: ({ isAuthenticated, errorMessage }) => {
        if (!isAuthenticated) {
          // never bounce straight back to the identity server from a failed callback, or a bad state loops forever
          if (window.location.pathname.startsWith('/callback') || errorMessage) {
            this.failed.set(errorMessage ?? 'Sign-in did not complete.');
            return;
          }
          sessionStorage.setItem(RETURN_URL_KEY, window.location.pathname + window.location.search);
          this.oidc.authorize();
          return;
        }
        this.ready.set(true);
        if (window.location.pathname.startsWith('/callback')) {
          const back = sessionStorage.getItem(RETURN_URL_KEY) ?? '/';
          sessionStorage.removeItem(RETURN_URL_KEY);
          this.router.navigateByUrl(back.startsWith('/callback') ? '/' : back);
        }
      },
      error: (err: Error) => this.failed.set(err.message)
    });
  }
}
