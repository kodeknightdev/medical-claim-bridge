import { ComponentFixture, TestBed } from '@angular/core/testing';
import { humanize, StatusChipComponent, toneFor } from './status-chip.component';

describe('StatusChipComponent', () => {
  let fixture: ComponentFixture<StatusChipComponent>;

  beforeEach(() => {
    fixture = TestBed.createComponent(StatusChipComponent);
  });

  it('renders a readable label with a tone class', () => {
    fixture.componentRef.setInput('status', 'AcceptedByPayer');
    fixture.detectChanges();
    const chip: HTMLElement = fixture.nativeElement.querySelector('.chip');
    expect(chip.textContent?.trim()).toBe('Accepted by payer');
    expect(chip.classList).toContain('info');
  });

  it('maps lifecycle states to tones', () => {
    expect(toneFor('Closed')).toBe('good');
    expect(toneFor('Denied')).toBe('bad');
    expect(toneFor('Pended')).toBe('warn');
    expect(toneFor('SomethingNew')).toBe('neutral');
    expect(humanize('ScrubFailed')).toBe('Scrub failed');
  });
});
