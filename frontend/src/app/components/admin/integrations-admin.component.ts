import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import {
  Integration,
  IntegrationInput,
  IntegrationService,
  ProviderType,
} from '../../services/integration.service';

@Component({
  selector: 'app-integrations-admin',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatTooltipModule,
  ],
  templateUrl: './integrations-admin.component.html',
  styleUrl: './integrations-admin.component.scss',
})
export class IntegrationsAdminComponent implements OnInit {
  protected readonly svc = inject(IntegrationService);
  protected readonly showForm = signal(false);
  protected readonly error = signal<string | null>(null);
  protected editingId: number | null = null;
  protected form: IntegrationInput = blank();

  protected readonly currentInfo = computed(() =>
    this.svc.providerTypes().find((p) => p.value === this.form.provider),
  );

  ngOnInit(): void {
    this.svc.refresh();
  }

  protected add(): void {
    this.editingId = null;
    this.form = blank();
    this.error.set(null);
    this.showForm.set(true);
  }

  protected edit(i: Integration): void {
    this.editingId = i.id;
    this.form = { ...i };
    this.error.set(null);
    this.showForm.set(true);
  }

  protected cancel(): void {
    this.showForm.set(false);
  }

  protected providerNeeds(field: 'apiKey' | 'url'): boolean {
    const info = this.currentInfo();
    return field === 'apiKey' ? !!info?.usesApiKey : !!info?.usesUrl;
  }

  protected get usesArea(): boolean {
    return this.form.provider === 'Datalastic';
  }

  protected get usesPoll(): boolean {
    return this.form.provider === 'MarineTraffic' || this.form.provider === 'Datalastic';
  }

  protected get usesBoxes(): boolean {
    return this.form.provider === 'AisStream' || this.form.provider === 'MarineTraffic';
  }

  protected save(): void {
    const op =
      this.editingId == null
        ? this.svc.create(this.form)
        : this.svc.update(this.editingId, this.form);
    op.subscribe({
      next: () => {
        this.showForm.set(false);
        this.svc.refresh();
      },
      error: (e) => this.error.set(e?.error?.error ?? 'Save failed'),
    });
  }

  protected toggle(i: Integration): void {
    this.svc.setEnabled(i.id, !i.enabled).subscribe(() => this.svc.refresh());
  }

  protected remove(i: Integration): void {
    this.svc.remove(i.id).subscribe(() => this.svc.refresh());
  }
}

function blank(): IntegrationInput {
  return {
    name: '',
    provider: 'Simulator' as ProviderType,
    enabled: true,
    apiKey: null,
    url: null,
    boundingBoxesJson: null,
    mmsiFilterJson: null,
    pollSeconds: 60,
    centerLat: 50,
    centerLon: -1,
    radiusKm: 100,
  };
}
