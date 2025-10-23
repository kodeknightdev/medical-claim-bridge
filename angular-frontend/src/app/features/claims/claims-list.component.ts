import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { CurrencyPipe, DatePipe, PercentPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { ApiService, ClaimSearch } from '../../core/api/api.service';
import { ClaimSearchEntry } from '../../core/api/models';
import { StatusChipComponent } from '../../shared/status-chip.component';

export const CLAIM_STATUSES = ['Received', 'ScrubFailed', 'ReadyToSubmit', 'Submitted', 'AcceptedByPayer', 'RejectedByPayer', 'Pended',
  'Escalated', 'Paid', 'PartiallyPaid', 'Denied', 'Appealed', 'Posted', 'Closed', 'Superseded', 'Voided'];

@Component({
  selector: 'cb-claims-list',
  imports: [FormsModule, RouterLink, CurrencyPipe, DatePipe, PercentPipe, MatTableModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatButtonModule, MatPaginatorModule, StatusChipComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="page-title">
        <h1>Claims</h1>
        <a mat-flat-button routerLink="/claims/new">New claim</a>
      </div>
      <div class="row">
        <mat-form-field><mat-label>Claim #, member last 4 or procedure</mat-label>
          <input matInput [(ngModel)]="query.search" (keyup.enter)="load(1)"></mat-form-field>
        <mat-form-field><mat-label>Status</mat-label>
          <mat-select [(ngModel)]="query.status" (selectionChange)="load(1)">
            <mat-option [value]="undefined">Any</mat-option>
            @for (s of statuses; track s) { <mat-option [value]="s">{{ s }}</mat-option> }
          </mat-select></mat-form-field>
        <mat-form-field><mat-label>Payer</mat-label>
          <mat-select [(ngModel)]="query.payerId" (selectionChange)="load(1)">
            <mat-option [value]="undefined">Any</mat-option>
            <mat-option value="PAYER-A">Northwind</mat-option><mat-option value="PAYER-B">Bluepeak</mat-option><mat-option value="PAYER-C">Cascade</mat-option>
          </mat-select></mat-form-field>
        <button mat-stroked-button (click)="load(1)">Search</button>
      </div>
      <table mat-table [dataSource]="rows()">
        <ng-container matColumnDef="claimNumber"><th mat-header-cell *matHeaderCellDef>Claim</th>
          <td mat-cell *matCellDef="let c"><a [routerLink]="['/claims', c.claimId]">{{ c.claimNumber }}</a>
            @if (c.frequencyCode !== '1') { <span class="muted"> (freq {{ c.frequencyCode }})</span> }</td></ng-container>
        <ng-container matColumnDef="payer"><th mat-header-cell *matHeaderCellDef>Payer</th><td mat-cell *matCellDef="let c">{{ c.payerId }}</td></ng-container>
        <ng-container matColumnDef="member"><th mat-header-cell *matHeaderCellDef>Member</th><td mat-cell *matCellDef="let c">****{{ c.memberIdLast4 }}</td></ng-container>
        <ng-container matColumnDef="dos"><th mat-header-cell *matHeaderCellDef>Service date</th><td mat-cell *matCellDef="let c">{{ c.firstServiceDate | date }}</td></ng-container>
        <ng-container matColumnDef="procedures"><th mat-header-cell *matHeaderCellDef>Codes</th><td mat-cell *matCellDef="let c">{{ c.procedureCodes }}</td></ng-container>
        <ng-container matColumnDef="charge"><th mat-header-cell *matHeaderCellDef>Charge</th><td mat-cell *matCellDef="let c">{{ c.totalCharge | currency }}</td></ng-container>
        <ng-container matColumnDef="paid"><th mat-header-cell *matHeaderCellDef>Paid</th><td mat-cell *matCellDef="let c">{{ c.paidAmount ?? 0 | currency }}</td></ng-container>
        <ng-container matColumnDef="risk"><th mat-header-cell *matHeaderCellDef>Risk</th><td mat-cell *matCellDef="let c">{{ c.denialRisk === null ? '–' : (c.denialRisk | percent) }}</td></ng-container>
        <ng-container matColumnDef="status"><th mat-header-cell *matHeaderCellDef>Status</th><td mat-cell *matCellDef="let c"><cb-status [status]="c.status" /></td></ng-container>
        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns"></tr>
      </table>
      <mat-paginator [length]="total()" [pageSize]="query.pageSize" [pageIndex]="(query.page ?? 1) - 1" [pageSizeOptions]="[25, 50, 100]"
                     (page)="page($event)" />
    </div>
  `
})
export class ClaimsListComponent implements OnInit {
  private readonly api = inject(ApiService);
  readonly statuses = CLAIM_STATUSES;
  readonly columns = ['claimNumber', 'payer', 'member', 'dos', 'procedures', 'charge', 'paid', 'risk', 'status'];
  readonly rows = signal<ClaimSearchEntry[]>([]);
  readonly total = signal(0);
  query: ClaimSearch = { page: 1, pageSize: 25 };

  ngOnInit(): void { this.load(1); }

  load(page: number): void {
    this.query.page = page;
    this.api.searchClaims(this.query).subscribe(r => {
      this.rows.set(r.items);
      this.total.set(r.total);
    });
  }

  page(e: PageEvent): void {
    this.query.pageSize = e.pageSize;
    this.load(e.pageIndex + 1);
  }
}
