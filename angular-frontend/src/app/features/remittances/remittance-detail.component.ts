import { ChangeDetectionStrategy, Component, inject, input, OnInit, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { ApiService, downloadBlob } from '../../core/api/api.service';
import { RemittanceDetail } from '../../core/api/models';
import { StatusChipComponent } from '../../shared/status-chip.component';

@Component({
  selector: 'cb-remittance-detail',
  imports: [CurrencyPipe, RouterLink, MatCardModule, MatButtonModule, StatusChipComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      @if (detail(); as d) {
        <div class="page-title">
          <h1>Remittance {{ d.file.eftTraceNumber }} <cb-status [status]="d.file.status" /></h1>
          <button mat-stroked-button (click)="pdf()">Download PDF</button>
        </div>
        <p class="muted">{{ d.file.payerId }} · paid {{ d.file.paymentDate }} · {{ d.file.totalPaid | currency }} for {{ d.postings.length }} claim(s)</p>
        @for (p of d.postings; track p.id) {
          <mat-card class="posting">
            <mat-card-content>
              <div class="row">
                <a [routerLink]="['/claims', p.claimId]"><strong>{{ p.claimNumber }}</strong></a>
                <cb-status [status]="kindStatus(p.kind)" /><span class="muted">{{ p.kind }}</span>
                <span class="spacer"></span>
                <span>Billed {{ p.charge | currency }} · allowed {{ p.allowed | currency }} · paid <strong>{{ p.paid | currency }}</strong>
                  · patient {{ p.patientResponsibility | currency }}</span>
              </div>
              <table class="adj">
                @for (a of p.adjustments; track $index) {
                  <tr><td>{{ a.groupCode }}-{{ a.reasonCode }}</td><td>{{ a.amount | currency }}</td><td class="muted">{{ a.explanation }}</td></tr>
                }
              </table>
              @if (p.remarkCodes.length) {
                <div class="muted">Remarks: @for (r of p.remarkCodes; track r) { {{ r }} – {{ d.remarkExplanations[r] }} }</div>
              }
            </mat-card-content>
          </mat-card>
        }
        <h3>Group codes</h3>
        <p class="muted">CO = contractual write-off the patient can't be billed for · PR = patient responsibility · OA = other adjustment.</p>
      }
    </div>
  `,
  styles: `.posting { margin-bottom: 12px; } .adj { margin-top: 8px; font-size: 13px; } .adj td { padding: 2px 12px 2px 0; }`
})
export class RemittanceDetailComponent implements OnInit {
  private readonly api = inject(ApiService);
  readonly id = input.required<string>();
  readonly detail = signal<RemittanceDetail | null>(null);

  ngOnInit(): void { this.api.remittance(this.id()).subscribe(d => this.detail.set(d)); }

  kindStatus(kind: string): string {
    return { Payment: 'Paid', PartialPayment: 'PartiallyPaid', Denial: 'Denied', Reversal: 'Voided' }[kind] ?? kind;
  }

  pdf(): void {
    const d = this.detail();
    if (d) this.api.remittancePdf(d.file.id).subscribe(b => downloadBlob(b, `remittance-${d.file.eftTraceNumber}.pdf`));
  }
}
