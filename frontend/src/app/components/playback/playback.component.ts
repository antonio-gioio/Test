import { DatePipe } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSliderModule } from '@angular/material/slider';
import { MatTooltipModule } from '@angular/material/tooltip';
import { VesselService } from '../../services/vessel.service';

@Component({
  selector: 'app-playback',
  standalone: true,
  imports: [DatePipe, MatButtonModule, MatIconModule, MatSliderModule, MatTooltipModule],
  template: `
    @if (vesselService.playbackActive()) {
      <div class="bar">
        <button mat-icon-button (click)="vesselService.togglePlayback()">
          <mat-icon>{{ vesselService.playbackPlaying() ? 'pause' : 'play_arrow' }}</mat-icon>
        </button>
        <mat-slider class="slider" [min]="min()" [max]="max" [step]="30000" discrete>
          <input matSliderThumb [value]="vesselService.playbackAt()" (valueChange)="seek($event)" />
        </mat-slider>
        <span class="time">{{ vesselService.playbackAt() | date: 'MMM d, HH:mm:ss' }}</span>
        <button mat-icon-button (click)="vesselService.stopPlayback()" aria-label="Exit playback">
          <mat-icon>close</mat-icon>
        </button>
      </div>
    } @else {
      <button mat-mini-fab class="open" (click)="vesselService.startPlayback()" matTooltip="Playback">
        <mat-icon>history</mat-icon>
      </button>
    }
  `,
  styles: [
    `
      :host {
        position: absolute;
        bottom: 1rem;
        left: 50%;
        transform: translateX(-50%);
        z-index: 1200;
      }
      .bar {
        display: flex;
        align-items: center;
        gap: 0.5rem;
        padding: 0.25rem 0.6rem;
        border-radius: 14px;
        background: rgba(255, 255, 255, 0.82);
        backdrop-filter: blur(12px) saturate(140%);
        border: 1px solid rgba(255, 255, 255, 0.7);
        box-shadow: 0 8px 30px rgba(8, 35, 58, 0.28);
      }
      .slider {
        width: min(46vw, 460px);
      }
      .time {
        font-variant-numeric: tabular-nums;
        font-size: 0.82rem;
        min-width: 140px;
        color: #0f2333;
      }
      .open {
        box-shadow: 0 6px 18px rgba(8, 35, 58, 0.35);
      }
    `,
  ],
})
export class PlaybackComponent {
  protected readonly vesselService = inject(VesselService);
  protected readonly max = Date.now();
  protected readonly min = computed(() => this.max - this.vesselService.playbackWindowMs);

  protected seek(value: number): void {
    this.vesselService.seek(value);
  }
}
