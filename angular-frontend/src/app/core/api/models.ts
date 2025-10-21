export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface PatientRequest {
  memberId: string;
  firstName: string;
  lastName: string;
  dateOfBirth: string;
  gender: 'M' | 'F' | 'U';
}

export interface ServiceLineRequest {
  procedureCode: string;
  modifiers: string[];
  units: number;
  charge: number;
  serviceDate: string;
  placeOfService: string;
  dxPointers: number[];
}

export interface ClaimRequest {
  payerId: string;
  billingNpi: string;
  renderingNpi: string | null;
  patient: PatientRequest;
  diagnoses: string[];
  lines: ServiceLineRequest[];
  priorAuthNumber: string | null;
  frequencyCode: '1' | '7' | '8';
  originalClaimNumber: string | null;
  orgId?: string | null;
}

export interface ScrubEdit {
  ruleCode: string;
  severity: 'Error' | 'Warning';
  message: string;
  field: string | null;
  fixSuggestion: string | null;
  lineNumber: number | null;
}

export interface Eligibility {
  status: 'Active' | 'Inactive' | 'Unknown';
  coverageStart: string | null;
  coverageEnd: string | null;
  planName: string | null;
  copay: number | null;
  deductibleRemaining: number | null;
  fromCache?: boolean;
}

export interface RiskFactor {
  factor: string;
  label: string;
  impact: number;
}

export interface RiskScore {
  probability: number;
  riskBand: 'low' | 'medium' | 'high';
  modelVersion: string;
  factors: RiskFactor[];
  demo: boolean;
}

export interface ScrubPreview {
  passed: boolean;
  edits: ScrubEdit[];
  eligibility: Eligibility;
  risk: RiskScore | null;
}

export interface ClaimSearchEntry {
  claimId: string;
  claimNumber: string;
  orgId: string;
  payerId: string;
  status: string;
  frequencyCode: string;
  version: number;
  memberIdLast4: string;
  procedureCodes: string;
  totalCharge: number;
  paidAmount: number | null;
  balance: number;
  firstServiceDate: string;
  createdAt: string;
  statusChangedAt: string;
  submittedAt: string | null;
  denialRisk: number | null;
  denialCategory: string | null;
  source: string;
  hasOpenEdits: boolean;
}

export interface ClaimSummary {
  id: string;
  claimNumber: string;
  orgId: string;
  payerId: string;
  status: string;
  frequencyCode: string;
  version: number;
  memberIdMasked: string;
  totalCharge: number;
  paidAmount: number | null;
  firstServiceDate: string | null;
  createdAt: string;
  statusChangedAt: string;
  denialRisk: number | null;
  source: string;
}

export interface HistoryEntry {
  sequence: number;
  fromStatus: string;
  toStatus: string;
  trigger: string;
  reason: string | null;
  source: string;
  occurredAt: string;
}

export interface ClaimDetail {
  summary: ClaimSummary;
  patient: PatientRequest;
  billingNpi: string;
  renderingNpi: string | null;
  priorAuthNumber: string | null;
  originalClaimNumber: string | null;
  previousClaimId: string | null;
  supersededByClaimId: string | null;
  diagnoses: string[];
  lines: (ServiceLineRequest & { lineNumber: number })[];
  history: HistoryEntry[];
  payerClaimId: string | null;
  allowedAmount: number | null;
  patientResponsibility: number | null;
  adjustmentAmount: number | null;
  denialCategory: string | null;
  denialCarc: string | null;
  possibleDuplicateOf: string | null;
  denialRiskFactors: RiskFactor[] | null;
}

export interface TimelineEntry {
  at: string;
  source: 'claims' | 'payer' | 'remittance';
  kind: string;
  text: string;
}

export interface ScrubRun {
  id: string;
  ranAt: string;
  passed: boolean;
  errorCount: number;
  warningCount: number;
  edits: ScrubEdit[];
}

export interface PayerSubmission {
  id: string;
  attempt: number;
  payerId: string;
  channel: string;
  status: string;
  payerClaimId: string | null;
  fileName: string | null;
  sentAt: string | null;
  ackAt: string | null;
  rejectReasons: string | null;
  lastError: string | null;
}

export interface Adjustment {
  groupCode: string;
  reasonCode: string;
  amount: number;
  explanation: string | null;
}

export interface Posting {
  id: string;
  claimId: string;
  claimNumber: string;
  kind: string;
  clpStatus: string;
  payerClaimId: string | null;
  charge: number;
  paid: number;
  allowed: number;
  patientResponsibility: number;
  contractualAdjustments: number;
  otherAdjustments: number;
  denialCarc: string | null;
  denialCategory: string | null;
  remarkCodes: string[];
  adjustments: Adjustment[];
  postedAt: string;
}

export interface ClaimDetails {
  claim: ClaimDetail;
  scrubResults: ScrubRun[] | null;
  payerHistory: { submissions: PayerSubmission[]; history: { kind: string; status: string; detail: string | null; at: string }[] } | null;
  remittance: Posting[] | null;
  risk: (RiskScore & { scoredAt: string }) | null;
  timeline: TimelineEntry[];
  partial: boolean;
  unavailable: string[];
}

export interface Batch {
  id: string;
  fileName: string;
  format: string;
  status: string;
  totalClaims: number;
  accepted: number;
  rejected: number;
  uploadedAt: string;
  completedAt: string | null;
  errors: { reference: string; message: string }[] | null;
}

export interface DenialItem {
  claimId: string;
  claimNumber: string;
  payerId: string;
  carcCode: string;
  category: string;
  deniedAmount: number;
  deniedAt: string;
  appealDeadline: string;
  daysToDeadline: number;
  ageBucket: string;
  status: string;
  correctedByClaimId: string | null;
  appealId: string | null;
}

export interface DenialGroup {
  key: string;
  count: number;
  amount: number;
}

export interface DenialWorklist {
  items: DenialItem[];
  byCategory: DenialGroup[];
  byPayer: DenialGroup[];
  byAge: DenialGroup[];
}

export interface Appeal {
  id: string;
  claimId: string;
  claimNumber: string;
  payerId: string;
  carcCode: string;
  status: string;
  deadline: string;
  daysLeft: number;
  createdAt: string;
  submittedAt: string | null;
  outcomeAt: string | null;
  outcomeNote: string | null;
  enclosures: string[];
}

export interface RemittanceFile {
  id: string;
  payerId: string;
  eftTraceNumber: string | null;
  paymentDate: string | null;
  totalPaid: number;
  claimCount: number;
  matchedCount: number;
  unmatchedCount: number;
  status: string;
  receivedAt: string;
}

export interface RemittanceDetail {
  file: RemittanceFile;
  postings: Posting[];
  remarkExplanations: Record<string, string>;
}

export interface UnmatchedLine {
  id: string;
  payerId: string;
  claimNumber: string;
  payerClaimId: string | null;
  charge: number;
  paid: number;
  reason: string;
  createdAt: string;
}

export interface Org {
  id: string;
  name: string;
  groupNpi: string;
  taxId: string;
  city: string;
  state: string;
  providerCount: number;
}

export interface RenderingProvider {
  id: string;
  npi: string;
  firstName: string;
  lastName: string;
  taxonomy: string;
  active: boolean;
}

export interface Enrollment {
  id: string;
  orgId: string;
  orgName: string;
  payerId: string;
  payerName: string;
  status: 'Pending' | 'Active' | 'Rejected';
  requestedAt: string;
  decidedAt: string | null;
  note: string | null;
}

export interface Payer {
  id: string;
  name: string;
  channel: string;
  timelyFilingDays: number;
}

export interface ApiClient {
  clientId: string;
  orgId: string;
  displayName: string;
  scopes: string[];
  rateLimitPerMinute: number;
  createdAt: string;
  secretRotatedAt: string | null;
  revokedAt: string | null;
}

export interface Webhook {
  id: string;
  url: string;
  events: string[];
  active: boolean;
  createdAt: string;
  disabledAt: string | null;
  disabledReason: string | null;
}

export interface WebhookDelivery {
  id: string;
  subscriptionId: string;
  eventType: string;
  url: string;
  status: string;
  attempts: number;
  lastStatusCode: number | null;
  lastError: string | null;
  createdAt: string;
  deliveredAt: string | null;
  nextAttemptAt: string;
}

export interface KpiSummary {
  claims: number;
  billed?: number;
  collected?: number;
  collectionRate?: number | null;
  firstPassAcceptanceRate?: number | null;
  cleanClaimRate?: number | null;
  denialRate?: number | null;
  medianPaymentTurnaroundDays?: number | null;
  outstanding?: number;
  daysInAr?: number | null;
  topScrubEdits?: { ruleCode: string; severity: string; count: number }[];
}

export interface TrendPoint {
  month: string;
  claims: number;
  billed: number;
  collected: number;
  denialRate: number | null;
  cleanClaimRate: number | null;
}

export interface AgeingResult {
  buckets: Record<string, number>;
  byPayer: Record<string, Record<string, number>>;
}

export interface DenialAnalytics {
  byPayer: { payerId: string; adjudicated: number; denied: number; denialRate: number }[];
  byCategory: { category: string; count: number; amount: number }[];
}

export interface PayerHealth {
  payerId: string;
  name: string;
  channel: string;
  queued: number;
  deferred: number;
  lastFileAt: string | null;
  health: {
    circuitState: string;
    requests: number;
    errorRate: number;
    p50Ms: number | null;
    p95Ms: number | null;
    lastSuccessAt: string | null;
    lastFailureAt: string | null;
    circuitOpenedAt: string | null;
  };
}
