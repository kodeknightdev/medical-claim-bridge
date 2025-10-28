import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { ApiService } from '../../core/api/api.service';
import { Eligibility } from '../../core/api/models';
import { StatusChipComponent } from '../../shared/status-chip.component';

@Component({
  selector: 'cb-eligibility',
  imports: [ReactiveFormsModule, CurrencyPipe, MatCardModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule, StatusChipComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="page-title"><h1>Eligibility check</h1></div>
      <mat-card>
        <mat-card-content>
          <form [formGroup]="form" (ngSubmit)="check()" class="row">
            <mat-form-field><mat-label>Payer</mat-label>
              <mat-select formControlName="payerId">
                <mat-option value="PAYER-A">Northwind Health Plan</mat-option>
                <mat-option value="PAYER-B">Bluepeak Mutual</mat-option>
                <mat-option value="PAYER-C">Cascade Care Insurance</mat-option>
              </mat-select></mat-form-field>
            <mat-form-field><mat-label>Member ID</mat-label><input matInput formControlName="memberId"></mat-form-field>
            <mat-form-field><mat-label>Date of birth</mat-label><input matInput type="date" formControlName="dateOfBirth"></mat-form-field>
            <mat-form-field><mat-label>Date of service</mat-label><input matInput type="date" formControlName="dateOfService"></mat-form-field>
            <button mat-flat-button type="submit" [disabled]="form.invalid || busy()">Check</button>
          </form>
          <p class="muted">A 270/271-style request through the payer's adapter; answers are cached for a few minutes.</p>
        </mat-card-content>
      </mat-card>
      @if (result(); as r) {
        <mat-card class="mt">
          <mat-card-content>
            <h2><cb-status [status]="r.status === 'Active' ? 'Active' : r.status === 'Inactive' ? 'Rejected' : 'Pending'" /> {{ r.status }}</h2>
            <dl>
              <dt>Plan</dt><dd>{{ r.planName ?? '–' }}</dd>
              <dt>Coverage</dt><dd>{{ r.coverageStart ?? '?' }} to {{ r.coverageEnd ?? 'open' }}</dd>
              <dt>Office copay</dt><dd>{{ r.copay === null ? '–' : (r.copay | currency) }}</dd>
              <dt>Deductible remaining</dt><dd>{{ r.deductibleRemaining === null ? '–' : (r.deductibleRemaining | currency) }}</dd>
              <dt>Source</dt><dd>{{ r.fromCache ? 'cache' : 'payer' }}</dd>
            </dl>
          </mat-card-content>
        </mat-card>
      }
    </div>
  `,
  styles: `.mt { margin-top: 16px; } dl { display: grid; grid-template-columns: 180px 1fr; gap: 4px; } dt { color: #66707d; } dd { margin: 0; }`
})
export class EligibilityComponent {
  private readonly api = inject(ApiService);
  readonly result = signal<Eligibility | null>(null);
  readonly busy = signal(false);
  readonly form = inject(FormBuilder).nonNullable.group({
    payerId: ['PAYER-A', Validators.required],
    memberId: ['', Validators.required],
    dateOfBirth: ['', Validators.required],
    dateOfService: [new Date().toISOString().slice(0, 10), Validators.required]
  });

  check(): void {
    this.busy.set(true);
    this.api.eligibility(this.form.getRawValue()).subscribe({
      next: r => { this.result.set(r); this.busy.set(false); },
      error: () => this.busy.set(false)
    });
  }
}
