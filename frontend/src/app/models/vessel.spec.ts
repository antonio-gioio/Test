import { navStatusLabel } from './vessel';

describe('navStatusLabel', () => {
  it('maps known AIS navigational-status codes', () => {
    expect(navStatusLabel(0)).toBe('Under way using engine');
    expect(navStatusLabel(1)).toBe('At anchor');
    expect(navStatusLabel(5)).toBe('Moored');
  });

  it('formats unknown codes generically', () => {
    expect(navStatusLabel(12)).toBe('Status 12');
  });

  it('returns null for missing status', () => {
    expect(navStatusLabel(null)).toBeNull();
  });
});
