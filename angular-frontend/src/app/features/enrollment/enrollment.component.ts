import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ApiService } from '../../core/api/api.service';
import { Enrollment, Org, Payer, RenderingProvider } from '../../core/api/models';
import { CurrentUser } from '../../core/auth/current-user';
import { StatusChipComponent } from '../../shared/status-chip.component';

@Component({
  selector: 'cb-enrollment',
  imports: [MatCardModule, MatTableModule, MatButtonModule, StatusChipComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="page-title"><h1>Providers &amp; enrollment</h1></div>
      <div class="grid two">
        @for (org of orgs(); track org.id) {
          <mat-card>
            <mat-card-header><mat-card-title>{{ org.name }}</mat-card-title>
              <mat-card-subtitle>Group NPI {{ org.groupNpi }} · TIN {{ org.taxId }} · {{ org.city }}, {{ org.state }}</mat-card-subtitle></mat-card-header>
            <mat-card-content>
              <h3>Rendering providers</h3>
              @for (p of providers()[org.id] ?? []; track p.id) { <div>{{ p.lastName }}, {{ p.firstName }} · NPI {{ p.npi }} · {{ p.taxonomy }}</div> }
              <h3>Payer enrollment</h3>
              @for (payer of payers(); track payer.id) {
                <div class="row enr">
                  <span class="payer">{{ payer.name }}</span>
                  @if (enrollmentFor(org.id, payer.id); as e) {
                    <cb-status [status]="e.status" />
                    @if (e.note) { <span class="muted">{{ e.note }}</span> }
                    @if (e.status === 'Pending' && user.isStaff()) {
                      <button mat-button (click)="decide(e, 'Active')">Approve</button>
                      <button mat-button (click)="decide(e, 'Rejected')">Reject</button>
                    }
                    @if (e.status === 'Rejected' && user.canManageOrg()) { <button mat-button (click)="request(payer.id, org.id)">Request again</button> }
                  } @else {
                    <span class="muted">Not enrolled</span>
                    @if (user.canManageOrg()) { <button mat-button (click)="request(payer.id, org.id)">Request enrollment</button> }
                  }
                </div>
              }
              <p class="muted">Claims to a payer without an active enrollment are blocked at intake.</p>
            </mat-card-content>
          </mat-card>
        }
      </div>
    </div>
  `,
  styles: `h3 { font-size: 14px; margin: 12px 0 6px; } .enr { min-height: 36px; } .payer { width: 190px; }`
})
export class EnrollmentComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly snack = inject(MatSnackBar);
  readonly user = inject(CurrentUser);
  readonly orgs = signal<Org[]>([]);
  readonly payers = signal<Payer[]>([]);
  readonly enrollments = signal<Enrollment[]>([]);
  readonly providers = signal<Record<string, RenderingProvider[] | undefined>>({});
  private readonly index = computed(() => new Map(this.enrollments().map(e => [`${e.orgId}|${e.payerId}`, e])));

  ngOnInit(): void {
    this.api.payers().subscribe(p => this.payers.set(p));
    this.loadEnrollments();
    this.api.orgs().subscribe(orgs => {
      this.orgs.set(orgs);
      for (const org of orgs)
        this.api.renderingProviders(org.id).subscribe(p => this.providers.update(all => ({ ...all, [org.id]: p })));
    });
  }

  enrollmentFor(orgId: string, payerId: string): Enrollment | undefined { return this.index().get(`${orgId}|${payerId}`); }

  loadEnrollments(): void { this.api.enrollments().subscribe(e => this.enrollments.set(e)); }

  request(payerId: string, orgId: string): void {
    this.api.requestEnrollment(payerId, this.user.isStaff() ? orgId : undefined).subscribe({
      next: () => { this.snack.open('Enrollment requested; clearinghouse staff will review it.', 'OK', { duration: 4000 }); this.loadEnrollments(); },
      error: err => this.snack.open(err.error?.detail ?? 'Request failed', 'Dismiss', { duration: 5000 })
    });
  }

  decide(e: Enrollment, status: 'Active' | 'Rejected'): void {
    this.api.decideEnrollment(e.id, status, status === 'Active' ? 'Approved by clearinghouse' : 'Paperwork incomplete')
      .subscribe(() => this.loadEnrollments());
  }
}
