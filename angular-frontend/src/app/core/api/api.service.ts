import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AgeingResult, ApiClient, Appeal, Batch, ClaimDetails, ClaimRequest, ClaimSearchEntry, ClaimSummary, DenialAnalytics,
  DenialWorklist, Eligibility, Enrollment, KpiSummary, Org, Paged, Payer, PayerHealth, RemittanceDetail, RemittanceFile,
  RenderingProvider, ScrubPreview, TrendPoint, UnmatchedLine, Webhook, WebhookDelivery
} from './models';

export interface ClaimSearch {
  status?: string;
  payerId?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

function params(values: object): HttpParams {
  let p = new HttpParams();
  for (const [k, v] of Object.entries(values)) {
    if (v !== undefined && v !== null && v !== '') p = p.set(k, String(v));
  }
  return p;
}

/** Thin typed wrappers over the gateway; every call goes through /api and carries the access token. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBase;

  // claims
  searchClaims(q: ClaimSearch): Observable<Paged<ClaimSearchEntry>> {
    return this.http.get<Paged<ClaimSearchEntry>>(`${this.base}/claims`, { params: params(q) });
  }
  claimDetails(id: string): Observable<ClaimDetails> {
    return this.http.get<ClaimDetails>(`${this.base}/claims/${id}/details`);
  }
  createClaim(body: ClaimRequest): Observable<ClaimSummary> {
    return this.http.post<ClaimSummary>(`${this.base}/claims`, body);
  }
  updateClaim(id: string, body: ClaimRequest): Observable<ClaimSummary> {
    return this.http.put<ClaimSummary>(`${this.base}/claims/${id}`, body);
  }
  correctClaim(id: string, body: ClaimRequest): Observable<ClaimSummary> {
    return this.http.post<ClaimSummary>(`${this.base}/claims/${id}/correct`, body);
  }
  voidClaim(id: string): Observable<ClaimSummary> {
    return this.http.post<ClaimSummary>(`${this.base}/claims/${id}/void`, {});
  }
  scrubPreview(body: ClaimRequest): Observable<ScrubPreview> {
    return this.http.post<ScrubPreview>(`${this.base}/claims/scrub-preview`, body);
  }
  eligibility(body: { payerId: string; memberId: string; dateOfBirth: string; dateOfService: string }): Observable<Eligibility> {
    return this.http.post<Eligibility>(`${this.base}/eligibility`, body);
  }

  // batches
  batches(): Observable<Batch[]> {
    return this.http.get<Batch[]>(`${this.base}/batches`);
  }
  uploadBatch(file: File): Observable<Batch> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<Batch>(`${this.base}/batches`, form);
  }

  // denials and appeals
  denials(status = 'Open'): Observable<DenialWorklist> {
    return this.http.get<DenialWorklist>(`${this.base}/denials`, { params: params({ status }) });
  }
  appeals(): Observable<Appeal[]> {
    return this.http.get<Appeal[]>(`${this.base}/appeals`);
  }
  createAppeal(claimId: string, argument: string, enclosures: string[]): Observable<Appeal> {
    return this.http.post<Appeal>(`${this.base}/appeals`, { claimId, argument, enclosures });
  }
  submitAppeal(id: string): Observable<Appeal> {
    return this.http.post<Appeal>(`${this.base}/appeals/${id}/submit`, {});
  }
  recordAppealOutcome(id: string, won: boolean, note: string): Observable<Appeal> {
    return this.http.post<Appeal>(`${this.base}/appeals/${id}/outcome`, { won, note });
  }
  appealLetter(id: string): Observable<Blob> {
    return this.http.get(`${this.base}/appeals/${id}/letter`, { responseType: 'blob' });
  }

  // remittance
  remittances(): Observable<RemittanceFile[]> {
    return this.http.get<RemittanceFile[]>(`${this.base}/remittances`);
  }
  remittance(id: string): Observable<RemittanceDetail> {
    return this.http.get<RemittanceDetail>(`${this.base}/remittances/${id}`);
  }
  remittancePdf(id: string): Observable<Blob> {
    return this.http.get(`${this.base}/remittances/${id}/pdf`, { responseType: 'blob' });
  }
  reconciliation(): Observable<UnmatchedLine[]> {
    return this.http.get<UnmatchedLine[]>(`${this.base}/reconciliation`);
  }
  matchUnmatched(id: string, claimNumber: string): Observable<void> {
    return this.http.post<void>(`${this.base}/reconciliation/${id}/match`, { claimNumber });
  }
  dismissUnmatched(id: string, note: string): Observable<void> {
    return this.http.post<void>(`${this.base}/reconciliation/${id}/dismiss`, { note });
  }

  // providers and enrollment
  orgs(): Observable<Org[]> {
    return this.http.get<Org[]>(`${this.base}/orgs`);
  }
  renderingProviders(orgId: string): Observable<RenderingProvider[]> {
    return this.http.get<RenderingProvider[]>(`${this.base}/orgs/${orgId}/providers`);
  }
  payers(): Observable<Payer[]> {
    return this.http.get<Payer[]>(`${this.base}/payers`);
  }
  enrollments(): Observable<Enrollment[]> {
    return this.http.get<Enrollment[]>(`${this.base}/enrollments`);
  }
  requestEnrollment(payerId: string, orgId?: string): Observable<unknown> {
    return this.http.post(`${this.base}/enrollments`, { payerId, orgId });
  }
  decideEnrollment(id: string, status: 'Active' | 'Rejected', note: string): Observable<void> {
    return this.http.post<void>(`${this.base}/enrollments/${id}/decision`, { status, note });
  }

  // integrations
  apiClients(): Observable<ApiClient[]> {
    return this.http.get<ApiClient[]>(`${this.base}/clients`);
  }
  createApiClient(displayName: string, scopes: string[], rateLimitPerMinute: number): Observable<{ clientId: string; clientSecret: string }> {
    return this.http.post<{ clientId: string; clientSecret: string }>(`${this.base}/clients`, { displayName, scopes, rateLimitPerMinute });
  }
  rotateClientSecret(clientId: string): Observable<{ clientId: string; clientSecret: string }> {
    return this.http.post<{ clientId: string; clientSecret: string }>(`${this.base}/clients/${clientId}/rotate-secret`, {});
  }
  revokeClient(clientId: string): Observable<void> {
    return this.http.post<void>(`${this.base}/clients/${clientId}/revoke`, {});
  }
  webhooks(): Observable<Webhook[]> {
    return this.http.get<Webhook[]>(`${this.base}/webhooks`);
  }
  createWebhook(url: string, events: string[]): Observable<{ id: string; signingSecret: string }> {
    return this.http.post<{ id: string; signingSecret: string }>(`${this.base}/webhooks`, { url, events });
  }
  enableWebhook(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/webhooks/${id}/enable`, {});
  }
  deliveries(subscriptionId?: string): Observable<WebhookDelivery[]> {
    return this.http.get<WebhookDelivery[]>(`${this.base}/webhook-deliveries`, { params: params({ subscriptionId }) });
  }
  redeliver(id: string): Observable<WebhookDelivery> {
    return this.http.post<WebhookDelivery>(`${this.base}/webhook-deliveries/${id}/redeliver`, {});
  }

  // analytics and ops
  kpis(days = 365): Observable<KpiSummary> {
    return this.http.get<KpiSummary>(`${this.base}/analytics/kpis`, { params: params({ days }) });
  }
  trends(months = 12): Observable<TrendPoint[]> {
    return this.http.get<TrendPoint[]>(`${this.base}/analytics/trends`, { params: params({ months }) });
  }
  ageing(): Observable<AgeingResult> {
    return this.http.get<AgeingResult>(`${this.base}/analytics/ageing`);
  }
  denialAnalytics(): Observable<DenialAnalytics> {
    return this.http.get<DenialAnalytics>(`${this.base}/analytics/denials`);
  }
  payerHealth(): Observable<PayerHealth[]> {
    return this.http.get<PayerHealth[]>(`${this.base}/payers/health`);
  }
  payerHealthHistory(minutes = 60): Observable<{ series: Record<string, { at: string; p95Ms: number | null; errorRate: number }[]> }> {
    return this.http.get<{ series: Record<string, { at: string; p95Ms: number | null; errorRate: number }[]> }>(
      `${this.base}/analytics/payer-health`, { params: params({ minutes }) });
  }
}

export function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  a.click();
  URL.revokeObjectURL(url);
}
