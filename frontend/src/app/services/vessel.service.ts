import { HttpClient } from '@angular/common/http';
import { Injectable, computed, effect, inject, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Observable } from 'rxjs';
import {
  AreaAlert,
  ClusterResult,
  FeedStatus,
  FleetStats,
  Vessel,
  VesselTrack,
} from '../models/vessel';
import { AuthService } from './auth.service';

export type ConnectionState = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

export interface Bounds {
  latMin: number;
  lonMin: number;
  latMax: number;
  lonMax: number;
}

interface ViewportResult {
  accepted: boolean;
  message: string | null;
  vessels: Vessel[];
}

export interface NearestVessel {
  mmsi: number;
  name: string | null;
  latitude: number;
  longitude: number;
  shipType: string | null;
  distanceMeters: number;
}

/**
 * Live vessel state for the app. Connects to the SignalR hub (with the auth token when
 * present), subscribes to the current map viewport, and applies the warm snapshot plus
 * incremental per-tile batches. Reconnects when the token/tier changes.
 */
@Injectable({ providedIn: 'root' })
export class VesselService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  private connection: signalR.HubConnection | null = null;
  private lastBounds: Bounds | null = null;

  private readonly vesselMap = signal<Map<number, Vessel>>(new Map());

  readonly connectionState = signal<ConnectionState>('disconnected');
  readonly feedMode = signal<'live' | 'simulation' | null>(null);
  readonly viewportMessage = signal<string | null>(null);
  readonly vessels = computed(() => [...this.vesselMap().values()]);
  readonly vesselCount = computed(() => this.vesselMap().size);
  readonly lastBatch = signal<Vessel[]>([]);

  readonly selectedMmsi = signal<number | null>(null);
  readonly selectedVessel = computed(() => {
    const mmsi = this.selectedMmsi();
    return mmsi === null ? null : (this.vesselMap().get(mmsi) ?? null);
  });

  /** Recent geofence alerts (most recent first), pushed over SignalR. */
  readonly alerts = signal<AreaAlert[]>([]);
  /** Live positions of the current user's followed vessels. */
  readonly followed = signal<Vessel[]>([]);
  /** The map's current visible bounds (for "watch this area" etc.). */
  readonly currentBounds = signal<Bounds | null>(null);

  // ---- Historical playback ----
  readonly playbackActive = signal(false);
  readonly playbackPlaying = signal(false);
  readonly playbackAt = signal<number>(Date.now()); // epoch ms
  readonly playbackVessels = signal<Vessel[]>([]);
  private playbackTimer: ReturnType<typeof setInterval> | undefined;
  private static readonly PlaybackStepMs = 60_000; // 60s of history per tick
  private static readonly PlaybackWindowMs = 60 * 60 * 1000; // scrub the last hour

  get playbackWindowMs(): number {
    return VesselService.PlaybackWindowMs;
  }

  constructor() {
    // Reconnect whenever the auth token changes so the hub sees the new tier.
    effect(() => {
      this.auth.tokenVersion();
      this.reconnect();
      this.loadFollowed();
    });
  }

  start(): void {
    this.http.get<FeedStatus>('/api/status').subscribe({
      next: (status) => this.feedMode.set(status.mode),
      error: () => this.feedMode.set(null),
    });
    this.reconnect();
  }

  /** Called by the map when the visible bounds change; re-subscribes to the new area. */
  setViewport(bounds: Bounds): void {
    this.lastBounds = bounds;
    this.currentBounds.set(bounds);
    void this.subscribeViewport();
  }

  getTrack(mmsi: number, hours?: number): Observable<VesselTrack> {
    const query = hours ? `?hours=${hours}` : '';
    return this.http.get<VesselTrack>(`/api/vessels/${mmsi}/track${query}`);
  }

  getClusters(bounds: Bounds, zoom: number): Observable<ClusterResult> {
    const q = `latMin=${bounds.latMin}&lonMin=${bounds.lonMin}&latMax=${bounds.latMax}&lonMax=${bounds.lonMax}&zoom=${zoom}`;
    return this.http.get<ClusterResult>(`/api/vessels/clusters?${q}`);
  }

  /** Global server-side search across all tracked vessels (not just the viewport). */
  searchGlobal(q: string): Observable<Vessel[]> {
    return this.http.get<Vessel[]>(`/api/vessels/search?q=${encodeURIComponent(q)}`);
  }

  /** Nearest vessels to a point (PostGIS KNN), with distance in metres. */
  getNearest(lat: number, lon: number, limit = 10): Observable<NearestVessel[]> {
    return this.http.get<NearestVessel[]>(`/api/vessels/nearest?lat=${lat}&lon=${lon}&limit=${limit}`);
  }

  /** Selects a vessel found via global search, adding it to the working set so the map can show it. */
  selectFromSearch(vessel: Vessel): void {
    const next = new Map(this.vesselMap());
    next.set(vessel.mmsi, vessel);
    this.vesselMap.set(next);
    this.lastBatch.set([vessel]);
    this.selectedMmsi.set(vessel.mmsi);
  }

  /** Clears any live working set and tier message when the map switches to cluster mode. */
  clearLiveState(): void {
    this.vesselMap.set(new Map());
    this.lastBatch.set([]);
    this.viewportMessage.set(null);
  }

  readonly stats = signal<FleetStats | null>(null);

  refreshStats(): void {
    this.http.get<FleetStats>('/api/vessels/stats').subscribe({
      next: (s) => this.stats.set(s),
      error: () => undefined,
    });
  }

  loadFollowed(): void {
    if (!this.auth.isLoggedIn()) {
      this.followed.set([]);
      return;
    }
    this.http.get<Vessel[]>('/api/account/followed').subscribe({
      next: (v) => this.followed.set(v),
      error: () => this.followed.set([]),
    });
  }

  follow(mmsi: number): void {
    this.http.put<number[]>(`/api/account/followed/${mmsi}`, {}).subscribe({
      next: () => {
        this.loadFollowed();
        this.auth.refreshAccount();
      },
    });
  }

  unfollow(mmsi: number): void {
    this.http.delete<number[]>(`/api/account/followed/${mmsi}`).subscribe({
      next: () => {
        this.loadFollowed();
        this.auth.refreshAccount();
      },
    });
  }

  dismissAlert(index: number): void {
    this.alerts.update((list) => list.filter((_, i) => i !== index));
  }

  startPlayback(): void {
    this.playbackActive.set(true);
    this.seek(Date.now() - VesselService.PlaybackWindowMs);
  }

  stopPlayback(): void {
    this.pausePlayback();
    this.playbackActive.set(false);
    this.playbackVessels.set([]);
  }

  togglePlayback(): void {
    this.playbackPlaying() ? this.pausePlayback() : this.playPlayback();
  }

  seek(atMs: number): void {
    this.playbackAt.set(atMs);
    this.loadHistory(atMs);
  }

  private playPlayback(): void {
    this.playbackPlaying.set(true);
    this.playbackTimer = setInterval(() => {
      let next = this.playbackAt() + VesselService.PlaybackStepMs;
      if (next >= Date.now()) {
        next = Date.now();
        this.pausePlayback();
      }
      this.seek(next);
    }, 700);
  }

  private pausePlayback(): void {
    this.playbackPlaying.set(false);
    clearInterval(this.playbackTimer);
  }

  private loadHistory(atMs: number): void {
    const b = this.currentBounds();
    if (!b) {
      return;
    }
    const at = new Date(atMs).toISOString();
    const q = `latMin=${b.latMin}&lonMin=${b.lonMin}&latMax=${b.latMax}&lonMax=${b.lonMax}&at=${at}`;
    this.http.get<{ vessels: Vessel[] }>(`/api/vessels/history?${q}`).subscribe({
      next: (res) => this.playbackVessels.set(res.vessels ?? []),
      error: () => this.playbackVessels.set([]),
    });
  }

  /** Downloads the current viewport as CSV (auth header attached by the interceptor). */
  exportViewportCsv(bounds: Bounds): void {
    const q = `latMin=${bounds.latMin}&lonMin=${bounds.lonMin}&latMax=${bounds.latMax}&lonMax=${bounds.lonMax}`;
    this.http.get(`/api/vessels/export?${q}`, { responseType: 'blob' }).subscribe({
      next: (blob) => this.triggerDownload(blob, 'vessels.csv'),
    });
  }

  exportTrackGeoJson(mmsi: number): void {
    this.http.get(`/api/vessels/${mmsi}/track?format=geojson`, { responseType: 'blob' }).subscribe({
      next: (blob) => this.triggerDownload(blob, `track-${mmsi}.geojson`),
    });
  }

  private triggerDownload(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
  }

  private async reconnect(): Promise<void> {
    if (this.connection) {
      await this.connection.stop().catch(() => undefined);
      this.connection = null;
    }

    this.connectionState.set('connecting');
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/vessels', { accessTokenFactory: () => this.auth.token() ?? '' })
      .withAutomaticReconnect()
      .build();

    connection.on('VesselsUpdated', (batch: Vessel[]) => this.applyBatch(batch));
    connection.on('FollowedUpdated', (vessel: Vessel) => this.applyFollowedUpdate(vessel));
    connection.on('AreaAlert', (alert: AreaAlert) =>
      this.alerts.update((list) => [alert, ...list].slice(0, 20)),
    );
    connection.onreconnecting(() => this.connectionState.set('reconnecting'));
    connection.onreconnected(async () => {
      this.connectionState.set('connected');
      await this.subscribeViewport();
    });
    connection.onclose(() => this.connectionState.set('disconnected'));

    this.connection = connection;
    try {
      await connection.start();
      this.connectionState.set('connected');
      await this.subscribeViewport();
    } catch (err) {
      console.error('SignalR connection failed', err);
      this.connectionState.set('disconnected');
    }
  }

  private async subscribeViewport(): Promise<void> {
    if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
      return;
    }
    const b = this.lastBounds;
    if (!b) {
      return;
    }

    try {
      const result = await this.connection.invoke<ViewportResult>(
        'SubscribeViewport',
        b.latMin,
        b.lonMin,
        b.latMax,
        b.lonMax,
      );
      this.viewportMessage.set(result.accepted ? null : result.message);
      if (result.accepted) {
        // Replace the working set with the warm snapshot for the new area.
        const next = new Map<number, Vessel>();
        for (const vessel of result.vessels) {
          next.set(vessel.mmsi, vessel);
        }
        this.vesselMap.set(next);
        this.lastBatch.set(result.vessels);
      }
    } catch (err) {
      console.error('SubscribeViewport failed', err);
    }
  }

  /** Live update for a followed vessel (arrives even when it's outside the viewport). */
  private applyFollowedUpdate(vessel: Vessel): void {
    this.followed.update((list) => {
      const idx = list.findIndex((v) => v.mmsi === vessel.mmsi);
      if (idx < 0) {
        return list;
      }
      const next = [...list];
      next[idx] = vessel;
      return next;
    });
    // If the vessel is also on the map, refresh its marker.
    if (this.vesselMap().has(vessel.mmsi)) {
      this.applyBatch([vessel]);
    }
  }

  private applyBatch(batch: Vessel[]): void {
    if (!batch.length) {
      return;
    }
    const next = new Map(this.vesselMap());
    for (const vessel of batch) {
      next.set(vessel.mmsi, vessel);
    }
    this.vesselMap.set(next);
    this.lastBatch.set(batch);
  }
}
