import { ChangeDetectionStrategy, Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTabsModule } from '@angular/material/tabs';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ApiService } from '../../core/api/api.service';
import { ClaimDetails } from '../../core/api/models';
import { StatusChipComponent } from '../../shared/status-chip.component';
import { RiskBadgeComponent } from '../../shared/risk-badge.component';

const CORRECTABLE = ['Denied', 'Posted', 'RejectedByPayer', 'PartiallyPaid', 'Closed', 'Paid'];

@Component({
  selector: 'cb-claim-detail',
  imports: [RouterLink, CurrencyPipe, DatePipe, MatCardModule, MatButtonModule, MatIconModule, MatTabsModule, StatusChipComponent,
    RiskBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './claim-detail.component.html',
  styleUrl: './claim-detail.component.scss'
})
export class ClaimDetailComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly snack = inject(MatSnackBar);

  readonly id = input.required<string>();
  readonly details = signal<ClaimDetails | null>(null);
  readonly claim = computed(() => this.details()?.claim ?? null);
  readonly canFix = computed(() => this.claim()?.summary.status === 'ScrubFailed');
  readonly canCorrect = computed(() => CORRECTABLE.includes(this.claim()?.summary.status ?? ''));
  readonly latestScrub = computed(() => this.details()?.scrubResults?.[0] ?? null);

  ngOnInit(): void { this.load(); }

  load(): void {
    this.api.claimDetails(this.id()).subscribe(d => this.details.set(d));
  }

  voidClaim(): void {
    this.api.voidClaim(this.id()).subscribe({
      next: v => {
        this.snack.open(`Void claim ${v.claimNumber} sent.`, 'OK', { duration: 4000 });
        this.router.navigate(['/claims', v.id]);
      },
      error: err => this.snack.open(err.error?.detail ?? 'Void failed', 'Dismiss', { duration: 6000 })
    });
  }
}
