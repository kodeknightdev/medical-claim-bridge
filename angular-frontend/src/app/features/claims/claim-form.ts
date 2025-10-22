import { ClaimDetail, ClaimRequest, ScrubEdit } from '../../core/api/models';

export interface LineFormValue {
  procedureCode: string;
  modifiers: string;
  units: number;
  charge: number;
  serviceDate: string;
  placeOfService: string;
  dxPointers: string;
}

export interface ClaimFormValue {
  payerId: string;
  renderingNpi: string;
  memberId: string;
  firstName: string;
  lastName: string;
  dateOfBirth: string;
  gender: 'M' | 'F' | 'U';
  priorAuthNumber: string;
  diagnoses: string[];
  lines: LineFormValue[];
}

const list = (value: string): string[] =>
  value.split(/[\s,]+/).map(v => v.trim().toUpperCase()).filter(v => v.length > 0);

/** Maps the flat form into the API request. Modifiers and pointers are typed as "25, 59" and "1,2". */
export function toClaimRequest(v: ClaimFormValue, billingNpi: string, orgId: string | null,
                               frequencyCode: '1' | '7' | '8' = '1', originalClaimNumber: string | null = null): ClaimRequest {
  return {
    payerId: v.payerId,
    billingNpi,
    renderingNpi: v.renderingNpi || null,
    patient: { memberId: v.memberId.trim(), firstName: v.firstName.trim(), lastName: v.lastName.trim(), dateOfBirth: v.dateOfBirth, gender: v.gender },
    diagnoses: v.diagnoses.map(d => d.trim().toUpperCase()).filter(d => d.length > 0),
    lines: v.lines.map(l => ({
      procedureCode: l.procedureCode.trim().toUpperCase(),
      modifiers: list(l.modifiers),
      units: Number(l.units),
      charge: Number(l.charge),
      serviceDate: l.serviceDate,
      placeOfService: l.placeOfService,
      dxPointers: list(l.dxPointers).map(Number).filter(n => !Number.isNaN(n))
    })),
    priorAuthNumber: v.priorAuthNumber.trim() || null,
    frequencyCode,
    originalClaimNumber,
    orgId
  };
}

export function fromClaimDetail(c: ClaimDetail): ClaimFormValue {
  return {
    payerId: c.summary.payerId,
    renderingNpi: c.renderingNpi ?? '',
    memberId: c.patient.memberId,
    firstName: c.patient.firstName,
    lastName: c.patient.lastName,
    dateOfBirth: c.patient.dateOfBirth,
    gender: c.patient.gender,
    priorAuthNumber: c.priorAuthNumber ?? '',
    diagnoses: [...c.diagnoses],
    lines: c.lines.map(l => ({
      procedureCode: l.procedureCode, modifiers: l.modifiers.join(', '), units: l.units, charge: l.charge, serviceDate: l.serviceDate,
      placeOfService: l.placeOfService, dxPointers: l.dxPointers.join(',')
    }))
  };
}

export function editsForLine(edits: ScrubEdit[], lineNumber: number): ScrubEdit[] {
  return edits.filter(e => e.lineNumber === lineNumber);
}

export function claimLevelEdits(edits: ScrubEdit[]): ScrubEdit[] {
  return edits.filter(e => e.lineNumber === null);
}

/** Enough of the claim is filled in for a scrub preview to be worth a round trip. */
export function readyForPreview(v: ClaimFormValue): boolean {
  return !!v.payerId && !!v.memberId && v.lines.some(l => l.procedureCode.length === 5 && !!l.serviceDate);
}
