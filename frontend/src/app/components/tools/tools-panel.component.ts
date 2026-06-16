import { Component, OnDestroy, OnInit, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { AuthService } from '../../services/auth.service';
import { NearestVessel, VesselService } from '../../services/vessel.service';
import { WatchAreaService } from '../../services/watch-area.service';

@Component({
  selector: 'app-tools-panel',
  standalone: true,
  imports: [
    FormsModule,
    MatExpansionModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
  ],
  templateUrl: './tools-panel.component.html',
  styleUrl: './tools-panel.component.scss',
})
export class ToolsPanelComponent implements OnInit, OnDestroy {
  protected readonly vesselService = inject(VesselService);
  protected readonly watchAreas = inject(WatchAreaService);
  protected readonly auth = inject(AuthService);

  protected readonly areaName = signal('');
  protected readonly nearest = signal<NearestVessel[]>([]);
  private statsTimer: ReturnType<typeof setInterval> | undefined;

  constructor() {
    // Reload watch areas whenever the login state changes.
    effect(() => {
      if (this.auth.isLoggedIn()) {
        this.watchAreas.refresh();
      } else {
        this.watchAreas.areas.set([]);
      }
    });
  }

  protected readonly topTypes = computed(() => {
    const stats = this.vesselService.stats();
    if (!stats) return [];
    return Object.entries(stats.byShipType)
      .sort((a, b) => b[1] - a[1])
      .slice(0, 5);
  });

  ngOnInit(): void {
    this.vesselService.refreshStats();
    this.statsTimer = setInterval(() => this.vesselService.refreshStats(), 15000);
    if (this.auth.isLoggedIn()) {
      this.vesselService.loadFollowed();
      this.watchAreas.refresh();
    }
  }

  ngOnDestroy(): void {
    clearInterval(this.statsTimer);
  }

  protected watchCurrentView(): void {
    const bounds = this.vesselService.currentBounds();
    const name = this.areaName().trim() || 'Watch area';
    if (bounds) {
      this.watchAreas.create(name, bounds);
      this.areaName.set('');
    }
  }

  protected exportView(): void {
    const bounds = this.vesselService.currentBounds();
    if (bounds) {
      this.vesselService.exportViewportCsv(bounds);
    }
  }

  protected findNearest(): void {
    const b = this.vesselService.currentBounds();
    if (!b) {
      return;
    }
    const lat = (b.latMin + b.latMax) / 2;
    const lon = (b.lonMin + b.lonMax) / 2;
    this.vesselService.getNearest(lat, lon, 8).subscribe((r) => this.nearest.set(r));
  }

  protected selectNearest(n: NearestVessel): void {
    this.vesselService.selectFromSearch({
      mmsi: n.mmsi,
      name: n.name,
      latitude: n.latitude,
      longitude: n.longitude,
      shipType: n.shipType,
      speedOverGround: null,
      courseOverGround: null,
      trueHeading: null,
      navigationalStatus: null,
      destination: null,
      callSign: null,
      imo: null,
      length: null,
      width: null,
      draught: null,
      eta: null,
      lastUpdate: new Date().toISOString(),
    });
  }

  protected km(meters: number): string {
    return meters >= 1000 ? `${(meters / 1000).toFixed(1)} km` : `${Math.round(meters)} m`;
  }

  protected select(mmsi: number): void {
    const vessel = this.vesselService.followed().find((v) => v.mmsi === mmsi);
    if (vessel) {
      this.vesselService.selectFromSearch(vessel);
    }
  }
}
