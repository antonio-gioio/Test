import { relativeTime } from './time';

describe('relativeTime', () => {
  const now = new Date('2026-06-16T12:00:00Z').getTime();

  it('formats seconds, minutes, hours and days', () => {
    expect(relativeTime('2026-06-16T11:59:50Z', now)).toBe('10s ago');
    expect(relativeTime('2026-06-16T11:57:00Z', now)).toBe('3m ago');
    expect(relativeTime('2026-06-16T10:00:00Z', now)).toBe('2h ago');
    expect(relativeTime('2026-06-14T12:00:00Z', now)).toBe('2d ago');
  });

  it('never returns a negative time for future timestamps', () => {
    expect(relativeTime('2026-06-16T12:00:30Z', now)).toBe('0s ago');
  });
});
