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
import { distanceNm } from '../../models/geo';
import { shipTypeColor, Vessel } from '../../models/vessel';
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
  private historyLayer: L.LayerGroup | null = null;
  private measureLayer: L.LayerGroup | null = null;
  private measurePoints: L.LatLng[] = [];
  private clusterMode = false;

  constructor() {
    effect(() => {
      const batch = this.vesselService.lastBatch();
      if (this.map && !this.clusterMode && !this.vesselService.playbackActive()) {
        this.syncMarkers(batch);
      }
    });

    // Historical playback overlay: render the snapshot at the scrubbed time; hide live markers.
    effect(() => {
      const active = this.vesselService.playbackActive();
      const vessels = this.vesselService.playbackVessels();
      if (this.map) {
        this.renderHistory(active, vessels);
      }
    });

    // Clear any measurement when measure mode is turned off.
    effect(() => {
      if (!this.vesselService.measureMode()) {
        this.clearMeasure();
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
    this.map.on('click', (e: L.LeafletMouseEvent) => this.onMapClick(e));
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
    this.vesselService.currentBounds.set(bounds);

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

  private renderHistory(active: boolean, vessels: Vessel[]): void {
    if (!this.map) {
      return;
    }
    this.historyLayer?.remove();
    this.historyLayer = null;

    if (!active) {
      // Restore live markers when leaving playback.
      if (!this.clusterMode) {
        this.syncMarkers(this.vesselService.vessels());
      }
      return;
    }

    // Hide live markers while scrubbing history.
    for (const marker of this.markers.values()) {
      marker.remove();
    }
    this.markers.clear();

    this.historyLayer = L.layerGroup();
    for (const vessel of vessels) {
      const rotation = vessel.trueHeading ?? vessel.courseOverGround ?? 0;
      const icon = L.divIcon({
        className: 'vessel-icon',
        iconSize: [22, 22],
        iconAnchor: [11, 11],
        html: `<div class="vessel-arrow history" style="transform: rotate(${rotation}deg)">▲</div>`,
      });
      L.marker([vessel.latitude, vessel.longitude], { icon })
        .bindPopup(this.popupHtml(vessel))
        .addTo(this.historyLayer);
    }
    this.historyLayer.addTo(this.map);
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

  private onMapClick(e: L.LeafletMouseEvent): void {
    if (!this.map || !this.vesselService.measureMode()) {
      return;
    }

    // Start a fresh measurement once two points are already placed.
    if (this.measurePoints.length >= 2) {
      this.clearMeasure();
    }

    this.measurePoints.push(e.latlng);
    this.measureLayer ??= L.layerGroup().addTo(this.map);
    L.circleMarker(e.latlng, { radius: 4, color: '#0c4a6e', fillOpacity: 1 }).addTo(this.measureLayer);

    if (this.measurePoints.length === 2) {
      const [a, b] = this.measurePoints;
      const nm = distanceNm(a.lat, a.lng, b.lat, b.lng);
      L.polyline(this.measurePoints, { color: '#0c4a6e', dashArray: '6 4', weight: 2 }).addTo(this.measureLayer);
      L.marker(b, {
        icon: L.divIcon({
          className: 'measure-label',
          html: `<span>${nm.toFixed(1)} NM</span>`,
          iconSize: [80, 20],
          iconAnchor: [-6, 10],
        }),
      }).addTo(this.measureLayer);
    }
  }

  private clearMeasure(): void {
    this.measureLayer?.remove();
    this.measureLayer = null;
    this.measurePoints = [];
  }

  private vesselIcon(vessel: Vessel): L.DivIcon {
    const rotation = vessel.trueHeading ?? vessel.courseOverGround ?? 0;
    const moving = (vessel.speedOverGround ?? 0) > 0.5;
    const selected = this.vesselService.selectedMmsi() === vessel.mmsi;
    // Colour by ship type (selected vessels stay red); stopped vessels are dimmed.
    const color = selected ? '#dc2626' : shipTypeColor(vessel.shipType);
    const style = `transform: rotate(${rotation}deg); color:${color}; opacity:${moving ? 1 : 0.55}`;
    return L.divIcon({
      className: 'vessel-icon',
      iconSize: [22, 22],
      iconAnchor: [11, 11],
      html: `<div class="vessel-arrow" style="${style}">▲</div>`,
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
