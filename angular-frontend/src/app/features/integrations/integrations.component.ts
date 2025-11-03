import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ApiService } from '../../core/api/api.service';
import { ApiClient, Webhook, WebhookDelivery } from '../../core/api/models';
import { CurrentUser } from '../../core/auth/current-user';
import { StatusChipComponent } from '../../shared/status-chip.component';

export const WEBHOOK_EVENTS = ['claim.accepted', 'claim.rejected', 'claim.paid', 'claim.denied', 'remittance.posted'];

@Component({
  selector: 'cb-integrations',
  imports: [DatePipe, FormsModule, MatTabsModule, MatTableModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule,
    MatCheckboxModule, StatusChipComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './integrations.component.html',
  styles: `
    .secret { background: #fff3d6; padding: 10px 14px; border-radius: 4px; margin: 12px 0; font-family: monospace; word-break: break-all; }
    .mt { margin-top: 16px; }
  `
})
export class IntegrationsComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly snack = inject(MatSnackBar);
  readonly user = inject(CurrentUser);

  readonly clients = signal<ApiClient[]>([]);
  readonly webhooks = signal<Webhook[]>([]);
  readonly deliveries = signal<WebhookDelivery[]>([]);
  readonly revealed = signal<{ label: string; value: string } | null>(null);
  readonly events = WEBHOOK_EVENTS;

  newClient = { displayName: '', submit: true, read: true, rate: 60 };
  newHook = { url: '', events: new Set<string>(WEBHOOK_EVENTS) };

  readonly clientColumns = ['name', 'clientId', 'scopes', 'rate', 'status', 'actions'];
  readonly hookColumns = ['url', 'events', 'status', 'actions'];
  readonly deliveryColumns = ['event', 'status', 'attempts', 'last', 'created', 'actions'];

  ngOnInit(): void { this.refresh(); }

  refresh(): void {
    this.api.apiClients().subscribe(c => this.clients.set(c));
    this.api.webhooks().subscribe(w => this.webhooks.set(w));
    this.api.deliveries().subscribe(d => this.deliveries.set(d));
  }

  createClient(): void {
    const scopes = [this.newClient.submit ? 'claims.submit' : '', this.newClient.read ? 'claims.read' : ''].filter(s => s);
    this.api.createApiClient(this.newClient.displayName, scopes, this.newClient.rate).subscribe({
      next: r => {
        this.revealed.set({ label: `Client ${r.clientId} secret (shown once)`, value: r.clientSecret });
        this.newClient.displayName = '';
        this.refresh();
      },
      error: err => this.snack.open(err.error?.detail ?? err.error?.title ?? 'Could not create client', 'Dismiss', { duration: 5000 })
    });
  }

  rotate(c: ApiClient): void {
    this.api.rotateClientSecret(c.clientId).subscribe(r => {
      this.revealed.set({ label: `New secret for ${r.clientId} (the old one stopped working)`, value: r.clientSecret });
      this.refresh();
    });
  }

  revoke(c: ApiClient): void { this.api.revokeClient(c.clientId).subscribe(() => this.refresh()); }

  toggleEvent(e: string, on: boolean): void {
    if (on) this.newHook.events.add(e);
    else this.newHook.events.delete(e);
  }

  createHook(): void {
    this.api.createWebhook(this.newHook.url, [...this.newHook.events]).subscribe({
      next: r => {
        this.revealed.set({ label: 'Webhook signing secret (shown once)', value: r.signingSecret });
        this.newHook.url = '';
        this.refresh();
      },
      error: err => this.snack.open(err.error?.detail ?? err.error?.title ?? 'Could not add webhook', 'Dismiss', { duration: 5000 })
    });
  }

  enable(w: Webhook): void { this.api.enableWebhook(w.id).subscribe(() => this.refresh()); }

  redeliver(d: WebhookDelivery): void {
    this.api.redeliver(d.id).subscribe({
      next: r => { this.snack.open(`Redelivery ${r.status === 'Succeeded' ? 'succeeded' : 'failed: ' + r.lastError}`, 'OK', { duration: 4000 }); this.refresh(); },
      error: () => this.snack.open('Redelivery failed', 'Dismiss', { duration: 4000 })
    });
  }
}
