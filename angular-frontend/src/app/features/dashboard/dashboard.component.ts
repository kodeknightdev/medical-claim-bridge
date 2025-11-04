import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { CurrencyPipe, DecimalPipe, PercentPipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { NgxEchartsDirective } from 'ngx-echarts';
import type { EChartsCoreOption } from 'echarts/core';
import { ApiService } from '../../core/api/api.service';
import { KpiSummary } from '../../core/api/models';
import { CurrentUser } from '../../core/auth/current-user';
import { ageingChart, denialByPayerChart, denialReasonChart, topEditsChart, trendsChart } from './dashboard-charts';

@Component({
  selector: 'cb-dashboard',
  imports: [CurrencyPipe, DecimalPipe, PercentPipe, MatCardModule, NgxEchartsDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="page-title">
        <h1>{{ user.isStaff() ? 'Clearinghouse operations' : 'Practice dashboard' }}</h1>
        <span class="muted">Last 12 months · synthetic data</span>
      </div>
      @if (kpis(); as k) {
        <div class="grid kpis">
          <div class="tile"><span>Clean claim rate</span><strong>{{ k.cleanClaimRate | percent: '1.1-1' }}</strong></div>
          <div class="tile"><span>First-pass acceptance</span><strong>{{ k.firstPassAcceptanceRate | percent: '1.1-1' }}</strong></div>
          <div class="tile"><span>Denial rate</span><strong>{{ k.denialRate | percent: '1.1-1' }}</strong></div>
          <div class="tile"><span>Days in A/R</span><strong>{{ k.daysInAr | number: '1.0-1' }}</strong></div>
          <div class="tile"><span>Payment turnaround</span><strong>{{ k.medianPaymentTurnaroundDays | number: '1.0-1' }} d</strong></div>
          <div class="tile"><span>Collected / billed</span><strong>{{ k.collectionRate | percent: '1.0-0' }}</strong>
            <small>{{ k.collected | currency: 'USD':'symbol':'1.0-0' }} of {{ k.billed | currency: 'USD':'symbol':'1.0-0' }}</small></div>
        </div>
      }
      <div class="grid two charts">
        <mat-card><mat-card-header><mat-card-title>Billed vs collected, denial rate</mat-card-title></mat-card-header>
          <mat-card-content><div echarts [options]="trends()" class="chart"></div></mat-card-content></mat-card>
        <mat-card><mat-card-header><mat-card-title>A/R ageing by payer</mat-card-title></mat-card-header>
          <mat-card-content><div echarts [options]="ageing()" class="chart"></div></mat-card-content></mat-card>
        <mat-card><mat-card-header><mat-card-title>Denial rate by payer</mat-card-title></mat-card-header>
          <mat-card-content><div echarts [options]="denialsByPayer()" class="chart"></div></mat-card-content></mat-card>
        <mat-card><mat-card-header><mat-card-title>Denials by reason</mat-card-title></mat-card-header>
          <mat-card-content><div echarts [options]="denialReasons()" class="chart"></div></mat-card-content></mat-card>
        <mat-card><mat-card-header><mat-card-title>Top scrubber edits</mat-card-title></mat-card-header>
          <mat-card-content><div echarts [options]="topEdits()" class="chart"></div></mat-card-content></mat-card>
      </div>
    </div>
  `,
  styles: `
    .tile { background: #fff; border-radius: 8px; padding: 14px 16px; box-shadow: 0 1px 3px rgba(0,0,0,.06); }
    .tile span { display: block; color: #66707d; font-size: 13px; }
    .tile strong { font-size: 26px; font-weight: 500; }
    .tile small { display: block; color: #66707d; }
    .charts { margin-top: 16px; }
  `
})
export class DashboardComponent implements OnInit {
  private readonly api = inject(ApiService);
  readonly user = inject(CurrentUser);
  readonly kpis = signal<KpiSummary | null>(null);
  readonly trends = signal<EChartsCoreOption>({});
  readonly ageing = signal<EChartsCoreOption>({});
  readonly denialsByPayer = signal<EChartsCoreOption>({});
  readonly denialReasons = signal<EChartsCoreOption>({});
  readonly topEdits = signal<EChartsCoreOption>({});

  ngOnInit(): void {
    this.api.kpis().subscribe(k => {
      this.kpis.set(k);
      this.topEdits.set(topEditsChart(k.topScrubEdits ?? []));
    });
    this.api.trends(12).subscribe(t => this.trends.set(trendsChart(t)));
    this.api.ageing().subscribe(a => this.ageing.set(ageingChart(a)));
    this.api.denialAnalytics().subscribe(d => {
      this.denialsByPayer.set(denialByPayerChart(d));
      this.denialReasons.set(denialReasonChart(d));
    });
  }
}
