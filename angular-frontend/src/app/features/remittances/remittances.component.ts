import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { ApiService, downloadBlob } from '../../core/api/api.service';
import { RemittanceFile } from '../../core/api/models';
import { StatusChipComponent } from '../../shared/status-chip.component';

@Component({
  selector: 'cb-remittances',
  imports: [CurrencyPipe, DatePipe, RouterLink, MatTableModule, MatButtonModule, StatusChipComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="page-title"><h1>Remittances (835)</h1></div>
      <table mat-table [dataSource]="files()">
        <ng-container matColumnDef="eft"><th mat-header-cell *matHeaderCellDef>EFT trace</th>
          <td mat-cell *matCellDef="let f"><a [routerLink]="['/remittances', f.id]">{{ f.eftTraceNumber ?? f.id.slice(0, 8) }}</a></td></ng-container>
        <ng-container matColumnDef="payer"><th mat-header-cell *matHeaderCellDef>Payer</th><td mat-cell *matCellDef="let f">{{ f.payerId }}</td></ng-container>
        <ng-container matColumnDef="date"><th mat-header-cell *matHeaderCellDef>Payment date</th><td mat-cell *matCellDef="let f">{{ f.paymentDate }}</td></ng-container>
        <ng-container matColumnDef="paid"><th mat-header-cell *matHeaderCellDef>Paid</th><td mat-cell *matCellDef="let f">{{ f.totalPaid | currency }}</td></ng-container>
        <ng-container matColumnDef="claims"><th mat-header-cell *matHeaderCellDef>Claims</th>
          <td mat-cell *matCellDef="let f">{{ f.matchedCount }} posted @if (f.unmatchedCount) { <span class="edit-error">· {{ f.unmatchedCount }} unmatched</span> }</td></ng-container>
        <ng-container matColumnDef="status"><th mat-header-cell *matHeaderCellDef>Status</th><td mat-cell *matCellDef="let f"><cb-status [status]="f.status" /></td></ng-container>
        <ng-container matColumnDef="received"><th mat-header-cell *matHeaderCellDef>Received</th><td mat-cell *matCellDef="let f">{{ f.receivedAt | date: 'short' }}</td></ng-container>
        <ng-container matColumnDef="pdf"><th mat-header-cell *matHeaderCellDef></th>
          <td mat-cell *matCellDef="let f"><button mat-button (click)="pdf(f)">PDF</button></td></ng-container>
        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns"></tr>
      </table>
    </div>
  `
})
export class RemittancesComponent implements OnInit {
  private readonly api = inject(ApiService);
  readonly files = signal<RemittanceFile[]>([]);
  readonly columns = ['eft', 'payer', 'date', 'paid', 'claims', 'status', 'received', 'pdf'];

  ngOnInit(): void { this.api.remittances().subscribe(f => this.files.set(f)); }

  pdf(f: RemittanceFile): void {
    this.api.remittancePdf(f.id).subscribe(b => downloadBlob(b, `remittance-${f.eftTraceNumber ?? f.id}.pdf`));
  }
}
