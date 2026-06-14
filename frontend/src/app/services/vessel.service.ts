import { HttpClient } from '@angular/common/http';
import { Injectable, computed, effect, inject, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Observable } from 'rxjs';
import { FeedStatus, Vessel, VesselTrack } from '../models/vessel';
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

  constructor() {
    // Reconnect whenever the auth token changes so the hub sees the new tier.
    effect(() => {
      this.auth.tokenVersion();
      this.reconnect();
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
    void this.subscribeViewport();
  }

  getTrack(mmsi: number, hours?: number): Observable<VesselTrack> {
    const query = hours ? `?hours=${hours}` : '';
    return this.http.get<VesselTrack>(`/api/vessels/${mmsi}/track${query}`);
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
