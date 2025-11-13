import { ageingChart, denialByPayerChart, payerName, topEditsChart, trendsChart } from './dashboard-charts';

describe('dashboard charts', () => {
  it('stacks ageing buckets per payer', () => {
    const option = ageingChart({ buckets: {}, byPayer: { 'PAYER-B': { '0-30': 10, '90+': 5 }, 'PAYER-A': { '31-60': 7 } } }) as any;
    expect(option.series.map((s: any) => s.name)).toEqual(['Northwind', 'Bluepeak']);
    expect(option.series[1].data).toEqual([10, 0, 0, 5]);
    expect(option.series[0].stack).toBe('ar');
  });

  it('plots billed, collected and denial rate on two axes', () => {
    const option = trendsChart([{ month: '2025-10', claims: 1, billed: 100, collected: 60, denialRate: 0.1, cleanClaimRate: 0.9 }]) as any;
    expect(option.series.length).toBe(3);
    expect(option.series[2].yAxisIndex).toBe(1);
  });

  it('sorts scrub edits so the biggest bar is on top', () => {
    const option = topEditsChart([{ ruleCode: 'CB007', count: 9 }, { ruleCode: 'CB004', count: 3 }]) as any;
    expect(option.yAxis.data).toEqual(['CB004', 'CB007']);
  });

  it('names payers and keeps unknown ids', () => {
    expect(payerName('PAYER-C')).toBe('Cascade');
    expect(payerName('PAYER-Z')).toBe('PAYER-Z');
    expect((denialByPayerChart({ byPayer: [{ payerId: 'PAYER-A', adjudicated: 10, denied: 1, denialRate: 0.1 }], byCategory: [] }) as any)
      .series[0].data).toEqual([0.1]);
  });
});
