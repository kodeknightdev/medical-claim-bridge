import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideEchartsCore } from 'ngx-echarts';
import * as echarts from 'echarts/core';
import { circuitStatus, PayerHealthComponent } from './payer-health.component';

describe('PayerHealthComponent', () => {
  let fixture: ComponentFixture<PayerHealthComponent>;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideEchartsCore({ echarts })]
    });
    fixture = TestBed.createComponent(PayerHealthComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => fixture.destroy());

  it('shows each payer with its circuit state', () => {
    fixture.detectChanges();
    http.expectOne('/api/payers/health').flush([
      { payerId: 'PAYER-C', name: 'Cascade Care Insurance', channel: 'json-api', queued: 0, deferred: 2, lastFileAt: null,
        health: { circuitState: 'Open', requests: 12, errorRate: 0.75, p50Ms: 900, p95Ms: 2100, lastSuccessAt: null, lastFailureAt: null, circuitOpenedAt: null } }
    ]);
    http.expectOne(r => r.url === '/api/analytics/payer-health').flush({ series: {} });
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('Cascade Care Insurance');
    expect(text).toContain('Open');
    expect(text).toContain('0 / 2');
  });

  it('maps circuit states onto chip tones', () => {
    expect(circuitStatus('Closed')).toBe('Active');
    expect(circuitStatus('Open')).toBe('Rejected');
    expect(circuitStatus('HalfOpen')).toBe('HalfOpen');
  });
});
