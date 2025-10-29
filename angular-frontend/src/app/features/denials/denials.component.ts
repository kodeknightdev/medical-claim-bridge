import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCardModule } from '@angular/material/card';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ApiService, downloadBlob } from '../../core/api/api.service';
import { Appeal, DenialGroup, DenialItem, DenialWorklist } from '../../core/api/models';
import { StatusChipComponent } from '../../shared/status-chip.component';

type GroupBy = 'byCategory' | 'byPayer' | 'byAge';

@Component({
  selector: 'cb-denials',
  imports: [CurrencyPipe, DatePipe, FormsModule, RouterLink, MatTabsModule, MatTableModule, MatButtonModule, MatButtonToggleModule,
    MatFormFieldModule, MatInputModule, MatCardModule, StatusChipComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './denials.component.html',
  styles: `
    .groups { display: flex; gap: 12px; flex-wrap: wrap; margin: 12px 0; }
    .group { background: #fff; border: 1px solid #e2e6ec; border-radius: 6px; padding: 8px 14px; cursor: pointer; }
    .group.active { border-color: #1d6fb8; background: #e3eefb; }
    .group strong { display: block; font-size: 18px; }
    .urgent { color: #a1271b; font-weight: 500; }
    .appeal-form { margin: 12px 0; }
    textarea { min-height: 90px; }
  `
})
export class DenialsComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly snack = inject(MatSnackBar);

  readonly worklist = signal<DenialWorklist | null>(null);
  readonly appeals = signal<Appeal[]>([]);
  readonly groupBy = signal<GroupBy>('byCategory');
  readonly selectedGroup = signal<string | null>(null);
  readonly appealFor = signal<DenialItem | null>(null);
  argument = '';
  enclosures = 'Office note, Authorisation confirmation';

  readonly groups = computed<DenialGroup[]>(() => this.worklist()?.[this.groupBy()] ?? []);
  readonly items = computed(() => {
    const all = this.worklist()?.items ?? [];
    const g = this.selectedGroup();
    if (!g) return all;
    const key: Record<GroupBy, (i: DenialItem) => string> = { byCategory: i => i.category, byPayer: i => i.payerId, byAge: i => i.ageBucket };
    return all.filter(i => key[this.groupBy()](i) === g);
  });

  readonly columns = ['claim', 'payer', 'carc', 'amount', 'denied', 'deadline', 'actions'];
  readonly appealColumns = ['claim', 'payer', 'status', 'deadline', 'actions'];

  ngOnInit(): void { this.refresh(); }

  refresh(): void {
    this.api.denials().subscribe(w => this.worklist.set(w));
    this.api.appeals().subscribe(a => this.appeals.set(a));
  }

  setGroup(by: GroupBy): void {
    this.groupBy.set(by);
    this.selectedGroup.set(null);
  }

  toggle(key: string): void { this.selectedGroup.set(this.selectedGroup() === key ? null : key); }

  startAppeal(item: DenialItem): void {
    this.appealFor.set(item);
    this.argument = '';
  }

  createAppeal(): void {
    const item = this.appealFor();
    if (!item) return;
    const enclosures = this.enclosures.split(',').map(e => e.trim()).filter(e => e);
    this.api.createAppeal(item.claimId, this.argument, enclosures).subscribe({
      next: () => {
        this.snack.open('Appeal drafted with a generated letter. Review it and submit before the deadline.', 'OK', { duration: 5000 });
        this.appealFor.set(null);
        this.refresh();
      },
      error: err => this.snack.open(err.error?.detail ?? err.error?.title ?? 'Could not create the appeal', 'Dismiss', { duration: 6000 })
    });
  }

  letter(a: Appeal): void {
    this.api.appealLetter(a.id).subscribe(b => downloadBlob(b, `appeal-${a.claimNumber}.pdf`));
  }

  submit(a: Appeal): void {
    this.api.submitAppeal(a.id).subscribe({
      next: () => this.refresh(),
      error: err => this.snack.open(err.error?.detail ?? 'Submit failed', 'Dismiss', { duration: 6000 })
    });
  }

  outcome(a: Appeal, won: boolean): void {
    this.api.recordAppealOutcome(a.id, won, won ? 'Payer overturned the denial' : 'Payer upheld the denial').subscribe(() => this.refresh());
  }
}
