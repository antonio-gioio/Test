import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { WatchArea } from '../models/vessel';
import { Bounds } from './vessel.service';

@Injectable({ providedIn: 'root' })
export class WatchAreaService {
  private readonly http = inject(HttpClient);

  readonly areas = signal<WatchArea[]>([]);

  refresh(): void {
    this.http.get<WatchArea[]>('/api/watch-areas').subscribe({
      next: (a) => this.areas.set(a),
      error: () => this.areas.set([]),
    });
  }

  create(name: string, bounds: Bounds): void {
    this.http
      .post<WatchArea>('/api/watch-areas', {
        name,
        latMin: bounds.latMin,
        lonMin: bounds.lonMin,
        latMax: bounds.latMax,
        lonMax: bounds.lonMax,
      })
      .subscribe(() => this.refresh());
  }

  remove(id: number): void {
    this.http.delete(`/api/watch-areas/${id}`).subscribe(() => this.refresh());
  }
}
