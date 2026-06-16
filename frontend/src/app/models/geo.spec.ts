import { distanceNm } from './geo';

describe('distanceNm', () => {
  it('treats one degree of latitude as ~60 NM', () => {
    expect(distanceNm(50, 0, 51, 0)).toBeCloseTo(60, 0);
  });

  it('is zero for identical points', () => {
    expect(distanceNm(50.5, -1.2, 50.5, -1.2)).toBe(0);
  });

  it('is symmetric', () => {
    const ab = distanceNm(50, -1, 51, 1);
    const ba = distanceNm(51, 1, 50, -1);
    expect(ab).toBeCloseTo(ba, 6);
  });
});
