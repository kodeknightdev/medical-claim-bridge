import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiService } from './api.service';

describe('ApiService', () => {
  let api: ApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('drops empty search parameters', () => {
    api.searchClaims({ status: 'Denied', payerId: undefined, search: '', page: 1 }).subscribe();
    const req = http.expectOne(r => r.url === '/api/claims');
    expect(req.request.params.get('status')).toBe('Denied');
    expect(req.request.params.has('payerId')).toBeFalse();
    expect(req.request.params.has('search')).toBeFalse();
    req.flush({ items: [], total: 0, page: 1, pageSize: 25 });
  });

  it('asks the gateway composition endpoint for claim details', () => {
    api.claimDetails('abc').subscribe(d => expect(d.partial).toBeTrue());
    http.expectOne('/api/claims/abc/details').flush({ partial: true, unavailable: ['remittance'], timeline: [] });
  });

  it('posts the scrub preview and corrected claims to the right routes', () => {
    api.scrubPreview({} as never).subscribe();
    expect(http.expectOne('/api/claims/scrub-preview').request.method).toBe('POST');
    api.correctClaim('id1', {} as never).subscribe();
    expect(http.expectOne('/api/claims/id1/correct').request.method).toBe('POST');
    http.match(() => true).forEach(r => r.flush({}));
  });

  it('uploads batches as multipart form data', () => {
    api.uploadBatch(new File(['claim_ref'], 'batch.csv')).subscribe();
    const req = http.expectOne('/api/batches');
    expect(req.request.body instanceof FormData).toBeTrue();
    req.flush({});
  });

  it('downloads pdfs as blobs', () => {
    api.remittancePdf('r1').subscribe(b => expect(b.size).toBe(3));
    const req = http.expectOne('/api/remittances/r1/pdf');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['pdf']));
  });
});
