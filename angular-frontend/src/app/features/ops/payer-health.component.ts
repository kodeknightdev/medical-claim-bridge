import { ChangeDetectionStrategy, Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { DatePipe, DecimalPipe, PercentPipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { NgxEchartsDirective } from 'ngx-echarts';
import type { EChartsCoreOption } from 'echarts/core';
import { ApiService } from '../../core/api/api.service';
import { PayerHealth } from '../../core/api/models';
import { StatusChipComponent } from '../../shared/status-chip.component';
import { payerName } from '../dashboard/dashboard-charts';

export function circuitStatus(state: string): string {
  return state === 'Closed' ? 'Active' : state === 'HalfOpen' ? 'HalfOpen' : 'Rejected';
}

@Component({
  selector: 'cb-payer-health',
  imports: [DatePipe, DecimalPipe, PercentPipe, MatCardModule, MatTableModule, NgxEchartsDirective, StatusChipComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="page-title"><h1>Payer health</h1><span class="muted">Live from payers-gateway, refreshed every 10 s</span></div>
      <table mat-table [dataSource]="payers()">
        <ng-container matColumnDef="payer"><th mat-header-cell *matHeaderCellDef>Payer</th>
          <td mat-cell *matCellDef="let p">{{ p.name }} <span class="muted">({{ p.channel }})</span></td></ng-container>
        <ng-container matColumnDef="circuit"><th mat-header-cell *matHeaderCellDef>Circuit</th>
          <td mat-cell *matCellDef="let p"><cb-status [status]="circuit(p.health.circuitState)" /> {{ p.health.circuitState }}</td></ng-container>
        <ng-container matColumnDef="latency"><th mat-header-cell *matHeaderCellDef>p50 / p95</th>
          <td mat-cell *matCellDef="let p">{{ p.health.p50Ms | number: '1.0-0' }} / {{ p.health.p95Ms | number: '1.0-0' }} ms</td></ng-container>
        <ng-container matColumnDef="errors"><th mat-header-cell *matHeaderCellDef>Error rate (5 min)</th>
          <td mat-cell *matCellDef="let p">{{ p.health.errorRate | percent: '1.0-1' }} of {{ p.health.requests }}</td></ng-container>
        <ng-container matColumnDef="queue"><th mat-header-cell *matHeaderCellDef>Queued / deferred</th>
          <td mat-cell *matCellDef="let p">{{ p.queued }} / {{ p.deferred }}</td></ng-container>
        <ng-container matColumnDef="last"><th mat-header-cell *matHeaderCellDef>Last success</th>
          <td mat-cell *matCellDef="let p">{{ p.health.lastSuccessAt | date: 'mediumTime' }}</td></ng-container>
        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns"></tr>
      </table>
      <mat-card class="mt"><mat-card-header><mat-card-title>p95 latency, last hour</mat-card-title></mat-card-header>
        <mat-card-content><div echarts [options]="latency()" class="chart"></div></mat-card-content></mat-card>
    </div>
  `,
  styles: `.mt { margin-top: 16px; }`
})
export class PayerHealthComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  readonly payers = signal<PayerHealth[]>([]);
  readonly latency = signal<EChartsCoreOption>({});
  readonly columns = ['payer', 'circuit', 'latency', 'errors', 'queue', 'last'];
  readonly circuit = circuitStatus;
  private timer?: ReturnType<typeof setInterval>;

  ngOnInit(): void {
    this.refresh();
    this.timer = setInterval(() => this.refresh(), 10000);
  }

  ngOnDestroy(): void { clearInterval(this.timer); }

  refresh(): void {
    this.api.payerHealth().subscribe(p => this.payers.set(p));
    this.api.payerHealthHistory(60).subscribe(h => this.latency.set({
      tooltip: { trigger: 'axis' },
      legend: { data: Object.keys(h.series).map(payerName) },
      xAxis: { type: 'time' },
      yAxis: { type: 'value', name: 'ms' },
      series: Object.entries(h.series).map(([payer, points]) => ({
        name: payerName(payer), type: 'line', showSymbol: false, data: points.map(p => [p.at, p.p95Ms])
      }))
    }));
  }
}
