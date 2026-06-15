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
  tier: string;
}

export interface TrackPoint {
  latitude: number;
  longitude: number;
  speedOverGround: number | null;
  courseOverGround: number | null;
  timestamp: string;
}

export interface VesselTrack {
  mmsi: number;
  windowHours: number;
  points: TrackPoint[];
}

export interface Cluster {
  latitude: number;
  longitude: number;
  count: number;
}

export interface ClusterResult {
  cellDegrees: number;
  clusters: Cluster[];
}

export interface AreaAlert {
  areaId: number;
  areaName: string;
  mmsi: number;
  name: string | null;
  shipType: string | null;
  latitude: number;
  longitude: number;
  at: string;
}

export interface FleetStats {
  total: number;
  moving: number;
  stopped: number;
  byShipType: Record<string, number>;
  withDestination: number;
}

export interface WatchArea {
  id: number;
  name: string;
  latMin: number;
  lonMin: number;
  latMax: number;
  lonMax: number;
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
