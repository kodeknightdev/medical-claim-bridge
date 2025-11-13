import { ComponentFixture, TestBed } from '@angular/core/testing';
import { bandFor, RiskBadgeComponent } from './risk-badge.component';

describe('RiskBadgeComponent', () => {
  let fixture: ComponentFixture<RiskBadgeComponent>;

  beforeEach(() => {
    fixture = TestBed.createComponent(RiskBadgeComponent);
  });

  it('shows probability, band, factors and the demo label', () => {
    fixture.componentRef.setInput('probability', 0.59);
    fixture.componentRef.setInput('factors', [{ factor: 'missing_prior_auth', label: 'No prior auth', impact: 0.49 }]);
    fixture.detectChanges();
    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('59%');
    expect(text).toContain('high');
    expect(text).toContain('No prior auth');
    expect(text).toContain('synthetic data');
  });

  it('says when no score is available', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('unavailable');
  });

  it('bands probabilities', () => {
    expect(bandFor(0.05)).toBe('low');
    expect(bandFor(0.2)).toBe('medium');
    expect(bandFor(0.5)).toBe('high');
  });
});
