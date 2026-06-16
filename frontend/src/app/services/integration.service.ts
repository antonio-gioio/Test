import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable } from 'rxjs';

export type ProviderType =
  | 'Simulator'
  | 'AisStream'
  | 'Digitraffic'
  | 'MarineTraffic'
  | 'Datalastic';

export interface Integration {
  id: number;
  name: string;
  provider: ProviderType;
  enabled: boolean;
  apiKey: string | null;
  url: string | null;
  boundingBoxesJson: string | null;
  mmsiFilterJson: string | null;
  pollSeconds: number;
  centerLat: number;
  centerLon: number;
  radiusKm: number;
}

export interface ProviderTypeInfo {
  value: ProviderType;
  usesApiKey: boolean;
  usesUrl: boolean;
  note: string;
}

export type IntegrationInput = Omit<Integration, 'id'>;

@Injectable({ providedIn: 'root' })
export class IntegrationService {
  private readonly http = inject(HttpClient);

  readonly integrations = signal<Integration[]>([]);
  readonly providerTypes = signal<ProviderTypeInfo[]>([]);

  refresh(): void {
    this.http.get<Integration[]>('/api/admin/integrations').subscribe((x) => this.integrations.set(x));
    this.http
      .get<ProviderTypeInfo[]>('/api/admin/provider-types')
      .subscribe((x) => this.providerTypes.set(x));
  }

  create(input: IntegrationInput): Observable<Integration> {
    return this.http.post<Integration>('/api/admin/integrations', input);
  }

  update(id: number, input: IntegrationInput): Observable<Integration> {
    return this.http.put<Integration>(`/api/admin/integrations/${id}`, input);
  }

  setEnabled(id: number, value: boolean): Observable<void> {
    return this.http.post<void>(`/api/admin/integrations/${id}/enabled?value=${value}`, {});
  }

  remove(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/integrations/${id}`);
  }
}
