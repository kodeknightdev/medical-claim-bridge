import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { CurrentUser } from '../../core/auth/current-user';

@Component({
  selector: 'cb-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="page-title"><h1>Welcome, {{ user.user()?.name }}</h1></div>
      <p class="muted">Dashboards arrive with the analytics work.</p>
    </div>
  `
})
export class DashboardComponent {
  readonly user = inject(CurrentUser);
}
