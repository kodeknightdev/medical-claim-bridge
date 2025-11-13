import { ScrubEdit } from '../../core/api/models';
import { claimLevelEdits, ClaimFormValue, editsForLine, readyForPreview, toClaimRequest } from './claim-form';

const form: ClaimFormValue = {
  payerId: 'PAYER-A', renderingNpi: '1999000114', memberId: ' nwh1000002 ', firstName: 'Jamie', lastName: 'Example',
  dateOfBirth: '1958-02-12', gender: 'F', priorAuthNumber: '  ', diagnoses: ['e11.9', '', 'I10'],
  lines: [{ procedureCode: '99214', modifiers: '25, rt', units: 1, charge: 175, serviceDate: '2025-11-03', placeOfService: '11', dxPointers: '1,2' }]
};

describe('claim form mapping', () => {
  it('normalises codes, lists and blanks into the API request', () => {
    const r = toClaimRequest(form, '1999000015', null);
    expect(r.patient.memberId).toBe('nwh1000002');
    expect(r.diagnoses).toEqual(['E11.9', 'I10']);
    expect(r.lines[0].modifiers).toEqual(['25', 'RT']);
    expect(r.lines[0].dxPointers).toEqual([1, 2]);
    expect(r.priorAuthNumber).toBeNull();
    expect(r.frequencyCode).toBe('1');
  });

  it('carries the original claim number for a corrected claim', () => {
    const r = toClaimRequest(form, '1999000015', 'org-1', '7', 'CB250000123');
    expect(r.frequencyCode).toBe('7');
    expect(r.originalClaimNumber).toBe('CB250000123');
    expect(r.orgId).toBe('org-1');
  });

  it('only previews once the payer, member and a usable line are present', () => {
    expect(readyForPreview(form)).toBeTrue();
    expect(readyForPreview({ ...form, memberId: '' })).toBeFalse();
    expect(readyForPreview({ ...form, lines: [{ ...form.lines[0], procedureCode: '992' }] })).toBeFalse();
  });

  it('splits edits into claim level and per line', () => {
    const edits: ScrubEdit[] = [
      { ruleCode: 'CB010', severity: 'Warning', message: 'm', field: null, fixSuggestion: null, lineNumber: null },
      { ruleCode: 'CB004', severity: 'Error', message: 'm', field: 'dxPointers', fixSuggestion: null, lineNumber: 1 },
      { ruleCode: 'CB005', severity: 'Error', message: 'm', field: 'units', fixSuggestion: null, lineNumber: 2 }
    ];
    expect(claimLevelEdits(edits).map(e => e.ruleCode)).toEqual(['CB010']);
    expect(editsForLine(edits, 1).map(e => e.ruleCode)).toEqual(['CB004']);
    expect(editsForLine(edits, 3)).toEqual([]);
  });
});
