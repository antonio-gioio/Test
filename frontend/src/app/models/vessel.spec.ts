import { navStatusLabel, shipTypeColor } from './vessel';

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

describe('shipTypeColor', () => {
  it('gives distinct colours to the main categories', () => {
    const cargo = shipTypeColor('Cargo');
    const tanker = shipTypeColor('Tanker');
    expect(cargo).toMatch(/^#[0-9a-f]{6}$/i);
    expect(cargo).not.toBe(tanker);
  });

  it('falls back to a neutral colour for unknown/null types', () => {
    expect(shipTypeColor(null)).toBe(shipTypeColor('Something else'));
  });
});
