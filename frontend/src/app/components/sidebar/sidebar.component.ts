import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { navStatusLabel, Vessel } from '../../models/vessel';
import { AuthService } from '../../services/auth.service';
import { VesselService } from '../../services/vessel.service';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [FormsModule, DatePipe, DecimalPipe],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss',
})
export class SidebarComponent {
  protected readonly vesselService = inject(VesselService);
  protected readonly auth = inject(AuthService);
  protected readonly search = signal('');
  protected readonly globalResults = signal<Vessel[]>([]);
  protected readonly navStatusLabel = navStatusLabel;

  protected isFollowed(mmsi: number): boolean {
    return this.auth.account()?.followedMmsis.includes(mmsi) ?? false;
  }

  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly filteredVessels = computed(() => {
    const term = this.search().trim().toLowerCase();
    const vessels = this.vesselService.vessels();
    const matches = term
      ? vessels.filter(
          (v) =>
            (v.name ?? '').toLowerCase().includes(term) || String(v.mmsi).includes(term),
        )
      : vessels;
    return [...matches]
      .sort((a, b) => (a.name ?? `~${a.mmsi}`).localeCompare(b.name ?? `~${b.mmsi}`))
      .slice(0, 200);
  });

  /** Debounced global search against the server (covers vessels outside the viewport). */
  protected onSearchChange(value: string): void {
    this.search.set(value);
    clearTimeout(this.searchTimer);
    const term = value.trim();
    if (term.length < 2) {
      this.globalResults.set([]);
      return;
    }
    this.searchTimer = setTimeout(() => {
      this.vesselService.searchGlobal(term).subscribe({
        next: (results) => this.globalResults.set(results),
        error: () => this.globalResults.set([]),
      });
    }, 300);
  }

  protected select(mmsi: number): void {
    this.vesselService.selectedMmsi.set(mmsi);
  }

  protected selectGlobal(vessel: Vessel): void {
    this.vesselService.selectFromSearch(vessel);
  }
}
