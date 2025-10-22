import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, input, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormArray, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { CurrencyPipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { catchError, debounceTime, filter, of, switchMap, tap } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import { Eligibility, Payer, RenderingProvider, ScrubPreview } from '../../core/api/models';
import { CurrentUser } from '../../core/auth/current-user';
import { RiskBadgeComponent } from '../../shared/risk-badge.component';
import { ClaimFormValue, claimLevelEdits, editsForLine, fromClaimDetail, readyForPreview, toClaimRequest } from './claim-form';

type Mode = 'new' | 'edit' | 'correct';

@Component({
  selector: 'cb-claim-entry',
  imports: [ReactiveFormsModule, CurrencyPipe, MatCardModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule,
    MatIconModule, MatProgressBarModule, RiskBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './claim-entry.component.html',
  styleUrl: './claim-entry.component.scss'
})
export class ClaimEntryComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder).nonNullable;
  private readonly router = inject(Router);
  private readonly snack = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);
  readonly user = inject(CurrentUser);

  readonly id = input<string>();
  readonly mode = signal<Mode>('new');
  readonly payers = signal<Payer[]>([]);
  readonly providers = signal<RenderingProvider[]>([]);
  readonly billingNpi = signal('');
  readonly orgId = signal<string | null>(null);
  readonly originalClaimNumber = signal<string | null>(null);
  readonly preview = signal<ScrubPreview | null>(null);
  readonly previewing = signal(false);
  readonly eligibility = signal<Eligibility | null>(null);
  readonly saving = signal(false);

  readonly claimEdits = computed(() => claimLevelEdits(this.preview()?.edits ?? []));
  readonly errorCount = computed(() => (this.preview()?.edits ?? []).filter(e => e.severity === 'Error').length);

  readonly form = this.fb.group({
    payerId: ['', Validators.required],
    renderingNpi: [''],
    memberId: ['', Validators.required],
    firstName: ['', Validators.required],
    lastName: ['', Validators.required],
    dateOfBirth: ['', Validators.required],
    gender: this.fb.control<'M' | 'F' | 'U'>('U'),
    priorAuthNumber: [''],
    diagnoses: this.fb.array([this.fb.control('', Validators.required)]),
    lines: this.fb.array([this.newLine()])
  });

  get diagnoses(): FormArray { return this.form.controls.diagnoses; }
  get lines(): FormArray { return this.form.controls.lines; }

  ngOnInit(): void {
    const url = this.router.url;
    this.mode.set(url.endsWith('/edit') ? 'edit' : url.endsWith('/correct') ? 'correct' : 'new');

    this.api.payers().subscribe(p => this.payers.set(p));
    this.api.orgs().subscribe(orgs => {
      const org = orgs[0];
      if (!org) return;
      this.billingNpi.set(org.groupNpi);
      this.orgId.set(this.user.isStaff() ? org.id : null);
      this.api.renderingProviders(org.id).subscribe(p => this.providers.set(p));
    });

    const id = this.id();
    if (id && this.mode() !== 'new') {
      this.api.claimDetails(id).subscribe(d => {
        const value = fromClaimDetail(d.claim);
        this.diagnoses.clear();
        value.diagnoses.forEach(() => this.addDiagnosis());
        this.lines.clear();
        value.lines.forEach(() => this.addLine());
        this.form.patchValue(value);
        this.originalClaimNumber.set(d.claim.summary.claimNumber);
      });
    }

    // debounced inline scrub while typing; nothing is saved
    this.form.valueChanges.pipe(
      debounceTime(700),
      filter(() => readyForPreview(this.value())),
      tap(() => this.previewing.set(true)),
      switchMap(() => this.api.scrubPreview(this.request()).pipe(catchError(() => of(null)))),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(result => {
      this.previewing.set(false);
      if (result) {
        this.preview.set(result);
        this.eligibility.set(result.eligibility);
      }
    });
  }

  newLine() {
    return this.fb.group({
      procedureCode: ['', [Validators.required, Validators.minLength(5), Validators.maxLength(5)]],
      modifiers: [''],
      units: [1, [Validators.required, Validators.min(1)]],
      charge: [0, [Validators.required, Validators.min(0.01)]],
      serviceDate: ['', Validators.required],
      placeOfService: ['11', Validators.required],
      dxPointers: ['1', Validators.required]
    });
  }

  addLine(): void { this.lines.push(this.newLine()); }
  removeLine(i: number): void { if (this.lines.length > 1) this.lines.removeAt(i); }
  addDiagnosis(): void { if (this.diagnoses.length < 12) this.diagnoses.push(this.fb.control('')); }
  removeDiagnosis(i: number): void { if (this.diagnoses.length > 1) this.diagnoses.removeAt(i); }

  lineEdits(i: number) { return editsForLine(this.preview()?.edits ?? [], i + 1); }

  value(): ClaimFormValue { return this.form.getRawValue() as ClaimFormValue; }

  request() {
    const frequency = this.mode() === 'correct' ? '7' : '1';
    return toClaimRequest(this.value(), this.billingNpi(), this.orgId(), frequency, this.mode() === 'correct' ? this.originalClaimNumber() : null);
  }

  checkEligibility(): void {
    const v = this.value();
    const dos = v.lines[0]?.serviceDate || new Date().toISOString().slice(0, 10);
    this.api.eligibility({ payerId: v.payerId, memberId: v.memberId, dateOfBirth: v.dateOfBirth, dateOfService: dos })
      .subscribe(e => this.eligibility.set(e));
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    const id = this.id();
    const call = this.mode() === 'edit' && id ? this.api.updateClaim(id, this.request())
      : this.mode() === 'correct' && id ? this.api.correctClaim(id, this.request())
      : this.api.createClaim(this.request());
    call.subscribe({
      next: claim => {
        this.snack.open(`Claim ${claim.claimNumber} received; scrubbing and submission continue in the background.`, 'OK', { duration: 5000 });
        this.router.navigate(['/claims', claim.id]);
      },
      error: err => {
        this.saving.set(false);
        this.snack.open(err.error?.detail ?? err.error?.title ?? 'The claim could not be saved.', 'Dismiss', { duration: 8000 });
      }
    });
  }
}
