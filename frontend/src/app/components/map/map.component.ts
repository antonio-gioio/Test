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

  private map: L.Map | null = null;
  private readonly markers = new Map<number, L.Marker>();
  private hasAutoFitted = false;

  constructor() {
    effect(() => {
      const batch = this.vesselService.lastBatch();
      if (this.map) {
        this.updateMarkers(batch);
      }
    });

    effect(() => {
      const vessel = this.vesselService.selectedVessel();
      if (this.map && vessel) {
        this.map.panTo([vessel.latitude, vessel.longitude]);
        this.markers.get(vessel.mmsi)?.openPopup();
      }
    });
  }

  ngAfterViewInit(): void {
    this.map = L.map(this.mapHost().nativeElement, {
      center: [50.5, -1.0],
      zoom: 7,
      worldCopyJump: true,
    });

    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution:
        '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
    }).addTo(this.map);

    // Render anything that arrived before the map was ready.
    this.updateMarkers(this.vesselService.vessels());
  }

  ngOnDestroy(): void {
    this.map?.remove();
    this.map = null;
  }

  private updateMarkers(batch: Vessel[]): void {
    if (!this.map) {
      return;
    }

    for (const vessel of batch) {
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

    // Zoom to the data the first time something shows up.
    if (!this.hasAutoFitted && this.markers.size > 0) {
      this.hasAutoFitted = true;
      const bounds = L.latLngBounds([...this.markers.values()].map((m) => m.getLatLng()));
      this.map.fitBounds(bounds.pad(0.2), { maxZoom: 9 });
    }
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
