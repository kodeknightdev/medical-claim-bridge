import type { EChartsCoreOption } from 'echarts/core';
import { AgeingResult, DenialAnalytics, TrendPoint } from '../../core/api/models';

const PAYER_NAMES: Record<string, string> = { 'PAYER-A': 'Northwind', 'PAYER-B': 'Bluepeak', 'PAYER-C': 'Cascade' };
export const BUCKETS = ['0-30', '31-60', '61-90', '90+'];

export function payerName(id: string): string {
  return PAYER_NAMES[id] ?? id;
}

export function trendsChart(points: TrendPoint[]): EChartsCoreOption {
  return {
    tooltip: { trigger: 'axis' },
    legend: { data: ['Billed', 'Collected', 'Denial rate'] },
    grid: { left: 60, right: 50, bottom: 30 },
    xAxis: { type: 'category', data: points.map(p => p.month) },
    yAxis: [
      { type: 'value', name: 'USD' },
      { type: 'value', name: 'Denial rate', axisLabel: { formatter: (v: number) => `${Math.round(v * 100)}%` }, max: 0.4 }
    ],
    series: [
      { name: 'Billed', type: 'bar', data: points.map(p => p.billed) },
      { name: 'Collected', type: 'bar', data: points.map(p => p.collected) },
      { name: 'Denial rate', type: 'line', yAxisIndex: 1, smooth: true, data: points.map(p => p.denialRate ?? 0) }
    ]
  };
}

export function denialByPayerChart(d: DenialAnalytics): EChartsCoreOption {
  return {
    tooltip: { trigger: 'axis', valueFormatter: (v: number) => `${(v * 100).toFixed(1)}%` },
    grid: { left: 50, right: 20, bottom: 30 },
    xAxis: { type: 'category', data: d.byPayer.map(p => payerName(p.payerId)) },
    yAxis: { type: 'value', axisLabel: { formatter: (v: number) => `${Math.round(v * 100)}%` } },
    series: [{ type: 'bar', data: d.byPayer.map(p => p.denialRate), itemStyle: { color: '#c0504d' } }]
  };
}

export function denialReasonChart(d: DenialAnalytics): EChartsCoreOption {
  return {
    tooltip: { trigger: 'item' },
    series: [{ type: 'pie', radius: ['40%', '70%'], data: d.byCategory.map(c => ({ name: c.category, value: c.count })) }]
  };
}

export function ageingChart(a: AgeingResult): EChartsCoreOption {
  const payers = Object.keys(a.byPayer).sort();
  return {
    tooltip: { trigger: 'axis' },
    legend: { data: payers.map(payerName) },
    grid: { left: 70, right: 20, bottom: 30 },
    xAxis: { type: 'category', data: BUCKETS },
    yAxis: { type: 'value', name: 'USD outstanding' },
    series: payers.map(p => ({ name: payerName(p), type: 'bar', stack: 'ar', data: BUCKETS.map(b => a.byPayer[p][b] ?? 0) }))
  };
}

export function topEditsChart(edits: { ruleCode: string; count: number }[]): EChartsCoreOption {
  const sorted = [...edits].sort((x, y) => x.count - y.count);
  return {
    tooltip: { trigger: 'axis' },
    grid: { left: 70, right: 20, bottom: 30 },
    xAxis: { type: 'value' },
    yAxis: { type: 'category', data: sorted.map(e => e.ruleCode) },
    series: [{ type: 'bar', data: sorted.map(e => e.count), itemStyle: { color: '#1d6fb8' } }]
  };
}
