export interface Vessel {
  mmsi: number;
  name: string | null;
  latitude: number;
  longitude: number;
  speedOverGround: number | null;
  courseOverGround: number | null;
  trueHeading: number | null;
  navigationalStatus: number | null;
  shipType: string | null;
  destination: string | null;
  callSign: string | null;
  lastUpdate: string;
}

export interface FeedStatus {
  mode: 'live' | 'simulation';
  vesselCount: number;
}

const NAV_STATUS: Record<number, string> = {
  0: 'Under way using engine',
  1: 'At anchor',
  2: 'Not under command',
  3: 'Restricted manoeuvrability',
  4: 'Constrained by draught',
  5: 'Moored',
  6: 'Aground',
  7: 'Engaged in fishing',
  8: 'Under way sailing',
  15: 'Undefined',
};

export function navStatusLabel(status: number | null): string | null {
  if (status === null || status === undefined) {
    return null;
  }
  return NAV_STATUS[status] ?? `Status ${status}`;
}
