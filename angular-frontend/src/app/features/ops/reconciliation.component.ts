import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ApiService } from '../../core/api/api.service';
import { UnmatchedLine } from '../../core/api/models';

@Component({
  selector: 'cb-reconciliation',
  imports: [CurrencyPipe, DatePipe, FormsModule, MatTableModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="page-title"><h1>Remittance reconciliation</h1>
        <span class="muted">835 lines that could not be posted automatically; the rest of each file was posted.</span></div>
      <table mat-table [dataSource]="lines()">
        <ng-container matColumnDef="payer"><th mat-header-cell *matHeaderCellDef>Payer</th><td mat-cell *matCellDef="let l">{{ l.payerId }}</td></ng-container>
        <ng-container matColumnDef="claim"><th mat-header-cell *matHeaderCellDef>CLP01 / payer ref</th>
          <td mat-cell *matCellDef="let l">{{ l.claimNumber }} / {{ l.payerClaimId ?? '–' }}</td></ng-container>
        <ng-container matColumnDef="amounts"><th mat-header-cell *matHeaderCellDef>Charge / paid</th>
          <td mat-cell *matCellDef="let l">{{ l.charge | currency }} / {{ l.paid | currency }}</td></ng-container>
        <ng-container matColumnDef="reason"><th mat-header-cell *matHeaderCellDef>Why</th><td mat-cell *matCellDef="let l">{{ l.reason }}</td></ng-container>
        <ng-container matColumnDef="created"><th mat-header-cell *matHeaderCellDef>Parked</th><td mat-cell *matCellDef="let l">{{ l.createdAt | date: 'short' }}</td></ng-container>
        <ng-container matColumnDef="actions"><th mat-header-cell *matHeaderCellDef></th>
          <td mat-cell *matCellDef="let l">
            <mat-form-field class="match"><mat-label>Match to claim #</mat-label><input matInput #num></mat-form-field>
            <button mat-button (click)="match(l, num.value)" [disabled]="!num.value">Post</button>
            <button mat-button (click)="dismiss(l)">Dismiss</button>
          </td></ng-container>
        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns"></tr>
      </table>
      @if (!lines().length) { <p class="muted">Nothing waiting for reconciliation.</p> }
    </div>
  `,
  styles: `.match { width: 160px; }`
})
export class ReconciliationComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly snack = inject(MatSnackBar);
  readonly lines = signal<UnmatchedLine[]>([]);
  readonly columns = ['payer', 'claim', 'amounts', 'reason', 'created', 'actions'];

  ngOnInit(): void { this.refresh(); }

  refresh(): void { this.api.reconciliation().subscribe(l => this.lines.set(l)); }

  match(line: UnmatchedLine, claimNumber: string): void {
    this.api.matchUnmatched(line.id, claimNumber.trim()).subscribe({
      next: () => { this.snack.open('Posted.', 'OK', { duration: 3000 }); this.refresh(); },
      error: err => this.snack.open(err.error?.detail ?? 'Could not post', 'Dismiss', { duration: 6000 })
    });
  }

  dismiss(line: UnmatchedLine): void {
    this.api.dismissUnmatched(line.id, 'Not ours; returned to payer').subscribe(() => this.refresh());
  }
}
