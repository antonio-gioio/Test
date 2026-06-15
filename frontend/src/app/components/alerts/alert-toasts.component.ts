import { DatePipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { VesselService } from '../../services/vessel.service';

@Component({
  selector: 'app-alert-toasts',
  standalone: true,
  imports: [DatePipe],
  template: `
    <div class="toasts">
      @for (alert of vesselService.alerts(); track $index) {
        <div class="toast" (click)="select(alert.mmsi)">
          <button class="close" (click)="dismiss($event, $index)">✕</button>
          <div class="title">⚠ Vessel entered “{{ alert.areaName }}”</div>
          <div class="body">
            {{ alert.name || 'MMSI ' + alert.mmsi }}
            @if (alert.shipType) {
              · {{ alert.shipType }}
            }
            <span class="time">{{ alert.at | date: 'HH:mm:ss' }}</span>
          </div>
        </div>
      }
    </div>
  `,
  styles: [
    `
      .toasts {
        position: absolute;
        top: 0.75rem;
        right: 0.75rem;
        z-index: 1300;
        display: flex;
        flex-direction: column;
        gap: 0.5rem;
        max-width: 320px;
      }
      .toast {
        position: relative;
        padding: 0.6rem 0.75rem;
        background: #7f1d1d;
        color: #fff;
        border-radius: 8px;
        box-shadow: 0 2px 10px rgba(0, 0, 0, 0.3);
        cursor: pointer;
      }
      .title {
        font-size: 0.82rem;
        font-weight: 600;
      }
      .body {
        font-size: 0.78rem;
        opacity: 0.9;
      }
      .time {
        margin-left: 0.3rem;
        opacity: 0.7;
      }
      .close {
        position: absolute;
        top: 0.3rem;
        right: 0.4rem;
        border: none;
        background: none;
        color: #fff;
        cursor: pointer;
        opacity: 0.8;
      }
    `,
  ],
})
export class AlertToastsComponent {
  protected readonly vesselService = inject(VesselService);

  protected select(mmsi: number): void {
    this.vesselService.selectedMmsi.set(mmsi);
  }

  protected dismiss(event: Event, index: number): void {
    event.stopPropagation();
    this.vesselService.dismissAlert(index);
  }
}
