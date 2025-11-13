import { toUserInfo } from './current-user';

describe('toUserInfo', () => {
  it('reads a single role, org and name', () => {
    expect(toUserInfo({ name: 'Sunrise Billing', role: 'ProviderBiller', org_id: 'org-1', email: 'b@x' }))
      .toEqual({ name: 'Sunrise Billing', email: 'b@x', roles: ['ProviderBiller'], orgId: 'org-1' });
  });

  it('reads multiple roles and staff without an org', () => {
    const u = toUserInfo({ preferred_username: 'admin', role: ['Admin', 'OpsAnalyst'] });
    expect(u?.roles).toEqual(['Admin', 'OpsAnalyst']);
    expect(u?.orgId).toBeNull();
    expect(u?.name).toBe('admin');
  });

  it('returns null before sign-in', () => expect(toUserInfo(null)).toBeNull());
});
