import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { PercentPipe } from '@angular/common';
import { RiskFactor } from '../core/api/models';

export function bandFor(probability: number): 'low' | 'medium' | 'high' {
  return probability >= 0.5 ? 'high' : probability >= 0.2 ? 'medium' : 'low';
}

@Component({
  selector: 'cb-risk',
  imports: [PercentPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (probability() !== null) {
      <div class="risk" [class]="'risk ' + band()">
        <div class="score">{{ probability() | percent: '1.0-0' }} <span>denial risk ({{ band() }})</span></div>
        @if (factors().length) {
          <ul>
            @for (f of factors(); track f.factor) {
              <li>{{ f.label }} <span class="impact">+{{ f.impact | percent: '1.0-0' }}</span></li>
            }
          </ul>
        }
        <div class="demo-note">Demo model trained on synthetic data; not for real billing decisions.</div>
      </div>
    } @else {
      <div class="muted">Risk score unavailable.</div>
    }
  `,
  styles: `
    .risk { border-left: 4px solid #9aa5b1; padding: 8px 12px; background: #fff; border-radius: 4px; }
    .risk.low { border-color: #2e7d32; } .risk.medium { border-color: #ed8b00; } .risk.high { border-color: #c62828; }
    .score { font-size: 20px; font-weight: 500; } .score span { font-size: 13px; color: #66707d; font-weight: 400; }
    ul { margin: 6px 0; padding-left: 18px; } .impact { color: #a1271b; font-size: 12px; }
  `
})
export class RiskBadgeComponent {
  readonly probability = input<number | null>(null);
  readonly factors = input<RiskFactor[]>([]);
  readonly band = computed(() => bandFor(this.probability() ?? 0));
}
