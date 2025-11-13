import { MemoryStorage, stripTokens } from './memory-storage';

describe('MemoryStorage', () => {
  afterEach(() => sessionStorage.clear());

  it('keeps tokens in memory but out of sessionStorage', () => {
    const storage = new MemoryStorage();
    const value = JSON.stringify({ authnResult: { access_token: 'secret' }, authzData: 'secret', authStateControl: 'state-1', codeVerifier: 'v' });
    storage.write('cfg', value);

    expect(storage.read('cfg')).toBe(value);
    const persisted = JSON.parse(sessionStorage.getItem('cfg')!);
    expect(persisted.authnResult).toBeUndefined();
    expect(persisted.authzData).toBeUndefined();
    expect(persisted.authStateControl).toBe('state-1');
    expect(persisted.codeVerifier).toBe('v');
  });

  it('falls back to the redirect state after a full page load', () => {
    sessionStorage.setItem('cfg', JSON.stringify({ authStateControl: 'state-2' }));
    expect(JSON.parse(new MemoryStorage().read('cfg')!).authStateControl).toBe('state-2');
  });

  it('clears both places', () => {
    const storage = new MemoryStorage();
    storage.write('cfg', '{}');
    storage.clear();
    expect(storage.read('cfg')).toBeNull();
  });

  it('leaves non-JSON values alone', () => expect(stripTokens('plain')).toBe('plain'));
});
