import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { navStatusLabel } from '../../models/vessel';
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
  protected readonly search = signal('');
  protected readonly navStatusLabel = navStatusLabel;

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

  protected select(mmsi: number): void {
    this.vesselService.selectedMmsi.set(mmsi);
  }
}
