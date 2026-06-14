import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  effect,
  inject,
  viewChild,
} from '@angular/core';
import * as L from 'leaflet';
import { Vessel } from '../../models/vessel';
import { VesselService } from '../../services/vessel.service';

@Component({
  selector: 'app-map',
  standalone: true,
  template: '<div class="map" #mapHost></div>',
  styles: [
    `
      :host,
      .map {
        display: block;
        height: 100%;
        width: 100%;
      }
    `,
  ],
})
export class MapComponent implements AfterViewInit, OnDestroy {
  private readonly vesselService = inject(VesselService);
  private readonly mapHost = viewChild.required<ElementRef<HTMLDivElement>>('mapHost');

  // Below this zoom the map shows server-aggregated clusters instead of individual ships.
  private static readonly ClusterZoom = 9;

  private map: L.Map | null = null;
  private readonly markers = new Map<number, L.Marker>();
  private trackLine: L.Polyline | null = null;
  private clusterLayer: L.LayerGroup | null = null;
  private clusterMode = false;

  constructor() {
    effect(() => {
      const batch = this.vesselService.lastBatch();
      if (this.map && !this.clusterMode) {
        this.syncMarkers(batch);
      }
    });

    effect(() => {
      const vessel = this.vesselService.selectedVessel();
      if (this.map && vessel) {
        this.map.panTo([vessel.latitude, vessel.longitude]);
        this.markers.get(vessel.mmsi)?.openPopup();
        this.loadTrack(vessel.mmsi);
      } else if (!vessel) {
        this.clearTrack();
      }
    });
  }

  ngAfterViewInit(): void {
    this.map = L.map(this.mapHost().nativeElement, {
      center: [50.5, -1.5],
      zoom: 8,
      worldCopyJump: true,
    });

    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution:
        '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
    }).addTo(this.map);

    // Drive the server-side subscription from the visible bounds.
    this.map.on('moveend', () => this.publishViewport());
    this.publishViewport();
    this.syncMarkers(this.vesselService.vessels());
  }

  ngOnDestroy(): void {
    this.map?.remove();
    this.map = null;
  }

  private publishViewport(): void {
    if (!this.map) {
      return;
    }
    const b = this.map.getBounds();
    const bounds = {
      latMin: b.getSouth(),
      lonMin: b.getWest(),
      latMax: b.getNorth(),
      lonMax: b.getEast(),
    };

    if (this.map.getZoom() < MapComponent.ClusterZoom) {
      this.enterClusterMode(bounds);
    } else {
      this.exitClusterMode();
      this.vesselService.setViewport(bounds);
    }
  }

  private enterClusterMode(bounds: {
    latMin: number;
    lonMin: number;
    latMax: number;
    lonMax: number;
  }): void {
    this.clusterMode = true;
    // Remove individual ship markers and any track; clusters take over.
    for (const marker of this.markers.values()) {
      marker.remove();
    }
    this.markers.clear();
    this.clearTrack();
    this.vesselService.clearLiveState();

    const zoom = this.map!.getZoom();
    this.vesselService.getClusters(bounds, zoom).subscribe({
      next: (result) => {
        if (!this.clusterMode) {
          return;
        }
        this.clusterLayer?.remove();
        this.clusterLayer = L.layerGroup();
        for (const cluster of result.clusters) {
          L.marker([cluster.latitude, cluster.longitude], {
            icon: this.clusterIcon(cluster.count),
          }).addTo(this.clusterLayer);
        }
        this.clusterLayer.addTo(this.map!);
      },
      error: () => this.exitClusterMode(),
    });
  }

  private exitClusterMode(): void {
    this.clusterMode = false;
    this.clusterLayer?.remove();
    this.clusterLayer = null;
  }

  private clusterIcon(count: number): L.DivIcon {
    const size = Math.min(56, 26 + Math.round(Math.log10(count + 1) * 14));
    return L.divIcon({
      className: 'cluster-icon',
      iconSize: [size, size],
      iconAnchor: [size / 2, size / 2],
      html: `<div class="cluster-bubble" style="width:${size}px;height:${size}px">${count}</div>`,
    });
  }

  /** Replaces markers to match the current working set, removing vessels no longer present. */
  private syncMarkers(updated: Vessel[]): void {
    if (!this.map) {
      return;
    }

    const live = new Set(this.vesselService.vessels().map((v) => v.mmsi));
    for (const [mmsi, marker] of this.markers) {
      if (!live.has(mmsi)) {
        marker.remove();
        this.markers.delete(mmsi);
      }
    }

    for (const vessel of updated) {
      const position: L.LatLngTuple = [vessel.latitude, vessel.longitude];
      const existing = this.markers.get(vessel.mmsi);
      if (existing) {
        existing.setLatLng(position);
        existing.setIcon(this.vesselIcon(vessel));
        existing.setPopupContent(this.popupHtml(vessel));
      } else {
        const marker = L.marker(position, { icon: this.vesselIcon(vessel) })
          .bindPopup(this.popupHtml(vessel))
          .on('click', () => this.vesselService.selectedMmsi.set(vessel.mmsi))
          .addTo(this.map);
        this.markers.set(vessel.mmsi, marker);
      }
    }
  }

  private loadTrack(mmsi: number): void {
    this.vesselService.getTrack(mmsi).subscribe({
      next: (track) => {
        if (this.vesselService.selectedMmsi() !== mmsi) {
          return;
        }
        this.clearTrack();
        if (track.points.length > 1) {
          const latlngs = track.points.map((p): L.LatLngTuple => [p.latitude, p.longitude]);
          this.trackLine = L.polyline(latlngs, { color: '#dc2626', weight: 2, opacity: 0.8 });
          this.trackLine.addTo(this.map!);
        }
      },
      error: () => this.clearTrack(),
    });
  }

  private clearTrack(): void {
    this.trackLine?.remove();
    this.trackLine = null;
  }

  private vesselIcon(vessel: Vessel): L.DivIcon {
    const rotation = vessel.trueHeading ?? vessel.courseOverGround ?? 0;
    const moving = (vessel.speedOverGround ?? 0) > 0.5;
    const selected = this.vesselService.selectedMmsi() === vessel.mmsi;
    const cssClass = `vessel-arrow${moving ? '' : ' stopped'}${selected ? ' selected' : ''}`;
    return L.divIcon({
      className: 'vessel-icon',
      iconSize: [22, 22],
      iconAnchor: [11, 11],
      html: `<div class="${cssClass}" style="transform: rotate(${rotation}deg)">▲</div>`,
    });
  }

  private popupHtml(vessel: Vessel): string {
    const name = this.escape(vessel.name) || `MMSI ${vessel.mmsi}`;
    const rows = [
      vessel.shipType ? `Type: ${this.escape(vessel.shipType)}` : null,
      vessel.speedOverGround !== null ? `Speed: ${vessel.speedOverGround.toFixed(1)} kn` : null,
      vessel.courseOverGround !== null ? `Course: ${vessel.courseOverGround.toFixed(0)}°` : null,
      vessel.destination ? `Destination: ${this.escape(vessel.destination)}` : null,
    ].filter((row) => row !== null);
    return `<strong>${name}</strong><br>${rows.join('<br>')}`;
  }

  private escape(value: string | null): string {
    return (value ?? '').replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`);
  }
}
