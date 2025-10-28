import { ChangeDetectionStrategy, Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ApiService } from '../../core/api/api.service';
import { Batch } from '../../core/api/models';
import { StatusChipComponent } from '../../shared/status-chip.component';

@Component({
  selector: 'cb-batches',
  imports: [DatePipe, MatButtonModule, MatCardModule, MatIconModule, MatTableModule, StatusChipComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="page-title"><h1>Batch upload</h1></div>
      <mat-card>
        <mat-card-content>
          <p>Upload a CSV (one row per service line, rows with the same <code>claim_ref</code> form one claim) or a simplified X12 837P file.
            The file is stored as-is and fanned out into individual claims in the background.</p>
          <div class="row">
            <input #file type="file" accept=".csv,.x12,.837,.txt" hidden (change)="upload(file.files)">
            <button mat-flat-button (click)="file.click()" [disabled]="uploading()"><mat-icon>upload_file</mat-icon> Choose file</button>
            <a mat-button href="/api/batches/template">Download CSV template</a>
          </div>
        </mat-card-content>
      </mat-card>
      <table mat-table [dataSource]="batches()" class="mt">
        <ng-container matColumnDef="file"><th mat-header-cell *matHeaderCellDef>File</th><td mat-cell *matCellDef="let b">{{ b.fileName }} <span class="muted">({{ b.format }})</span></td></ng-container>
        <ng-container matColumnDef="uploaded"><th mat-header-cell *matHeaderCellDef>Uploaded</th><td mat-cell *matCellDef="let b">{{ b.uploadedAt | date: 'medium' }}</td></ng-container>
        <ng-container matColumnDef="status"><th mat-header-cell *matHeaderCellDef>Status</th><td mat-cell *matCellDef="let b"><cb-status [status]="b.status" /></td></ng-container>
        <ng-container matColumnDef="counts"><th mat-header-cell *matHeaderCellDef>Claims</th>
          <td mat-cell *matCellDef="let b">{{ b.accepted }} accepted / {{ b.rejected }} rejected of {{ b.totalClaims }}</td></ng-container>
        <ng-container matColumnDef="errors"><th mat-header-cell *matHeaderCellDef>Problems</th>
          <td mat-cell *matCellDef="let b">@for (e of b.errors ?? []; track $index) { <div class="edit-error">{{ e.reference }}: {{ e.message }}</div> }</td></ng-container>
        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns"></tr>
      </table>
    </div>
  `,
  styles: `.mt { margin-top: 16px; } code { background: #eef1f5; padding: 0 4px; }`
})
export class BatchesComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly snack = inject(MatSnackBar);
  readonly batches = signal<Batch[]>([]);
  readonly uploading = signal(false);
  readonly columns = ['file', 'uploaded', 'status', 'counts', 'errors'];
  private timer?: ReturnType<typeof setInterval>;

  ngOnInit(): void {
    this.refresh();
    // fan-out is asynchronous, so keep the list fresh while a batch is processing
    this.timer = setInterval(() => {
      if (this.batches().some(b => b.status === 'Uploaded' || b.status === 'Processing')) this.refresh();
    }, 3000);
  }

  ngOnDestroy(): void { clearInterval(this.timer); }

  refresh(): void { this.api.batches().subscribe(b => this.batches.set(b)); }

  upload(files: FileList | null): void {
    const file = files?.[0];
    if (!file) return;
    this.uploading.set(true);
    this.api.uploadBatch(file).subscribe({
      next: () => { this.uploading.set(false); this.refresh(); },
      error: err => {
        this.uploading.set(false);
        this.snack.open(err.error?.detail ?? 'Upload failed', 'Dismiss', { duration: 6000 });
      }
    });
  }
}
