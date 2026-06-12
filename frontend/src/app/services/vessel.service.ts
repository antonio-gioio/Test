import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { FeedStatus, Vessel } from '../models/vessel';

export type ConnectionState = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

/**
 * Holds the live vessel state for the whole app. Loads an initial snapshot over
 * REST, then applies incremental batches pushed by the backend over SignalR.
 */
@Injectable({ providedIn: 'root' })
export class VesselService {
  private readonly http = inject(HttpClient);
  private readonly vesselMap = signal<Map<number, Vessel>>(new Map());

  readonly connectionState = signal<ConnectionState>('connecting');
  readonly feedMode = signal<'live' | 'simulation' | null>(null);
  readonly vessels = computed(() => [...this.vesselMap().values()]);
  readonly vesselCount = computed(() => this.vesselMap().size);

  /** Set to the batch most recently received, so the map can update incrementally. */
  readonly lastBatch = signal<Vessel[]>([]);

  /** MMSI of the vessel selected in the list or on the map, shared across components. */
  readonly selectedMmsi = signal<number | null>(null);
  readonly selectedVessel = computed(() => {
    const mmsi = this.selectedMmsi();
    return mmsi === null ? null : (this.vesselMap().get(mmsi) ?? null);
  });

  start(): void {
    this.http.get<FeedStatus>('/api/status').subscribe({
      next: (status) => this.feedMode.set(status.mode),
      error: () => this.feedMode.set(null),
    });

    this.http.get<Vessel[]>('/api/vessels').subscribe({
      next: (vessels) => this.applyBatch(vessels),
      error: (err) => console.error('Failed to load vessel snapshot', err),
    });

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/vessels')
      .withAutomaticReconnect()
      .build();

    connection.on('VesselsUpdated', (batch: Vessel[]) => this.applyBatch(batch));
    connection.onreconnecting(() => this.connectionState.set('reconnecting'));
    connection.onreconnected(() => this.connectionState.set('connected'));
    connection.onclose(() => this.connectionState.set('disconnected'));

    connection
      .start()
      .then(() => this.connectionState.set('connected'))
      .catch((err) => {
        console.error('SignalR connection failed', err);
        this.connectionState.set('disconnected');
      });
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
