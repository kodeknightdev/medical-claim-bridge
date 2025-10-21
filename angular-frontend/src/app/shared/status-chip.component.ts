import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

const TONES: Record<string, string> = {
  Closed: 'good', Paid: 'good', Posted: 'good', AcceptedByPayer: 'info', Active: 'good', Succeeded: 'good', Won: 'good',
  Submitted: 'info', ReadyToSubmit: 'info', Scrubbing: 'info', Received: 'neutral', Pended: 'warn', PartiallyPaid: 'warn',
  Escalated: 'warn', Appealed: 'warn', Pending: 'warn', Open: 'warn', Draft: 'neutral', HalfOpen: 'warn',
  Denied: 'bad', RejectedByPayer: 'bad', ScrubFailed: 'bad', Rejected: 'bad', Abandoned: 'bad', Lost: 'bad',
  Superseded: 'neutral', Voided: 'neutral', Corrected: 'neutral', Resolved: 'good', WrittenOff: 'neutral'
};

/** Splits PascalCase statuses for display: "AcceptedByPayer" becomes "Accepted by payer". */
export function humanize(status: string): string {
  const words = status.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

export function toneFor(status: string): string {
  return TONES[status] ?? 'neutral';
}

@Component({
  selector: 'cb-status',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="chip" [class]="'chip ' + tone()">{{ label() }}</span>`,
  styles: `
    .chip { display: inline-block; padding: 2px 10px; border-radius: 12px; font-size: 12px; font-weight: 500; white-space: nowrap; }
    .good { background: #e3f4e8; color: #1e6b35; }
    .info { background: #e3eefb; color: #1d4f8c; }
    .warn { background: #fff3d6; color: #7a5200; }
    .bad { background: #fde4e1; color: #a1271b; }
    .neutral { background: #eceff3; color: #4a5462; }
  `
})
export class StatusChipComponent {
  readonly status = input.required<string>();
  readonly tone = computed(() => toneFor(this.status()));
  readonly label = computed(() => humanize(this.status()));
}
