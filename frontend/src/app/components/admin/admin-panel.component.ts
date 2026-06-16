import { Component, OnInit, inject, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { AdminService } from '../../services/admin.service';
import { Tier } from '../../services/auth.service';
import { IntegrationsAdminComponent } from './integrations-admin.component';

@Component({
  selector: 'app-admin-panel',
  standalone: true,
  imports: [
    MatTabsModule,
    MatButtonModule,
    MatIconModule,
    MatSelectModule,
    IntegrationsAdminComponent,
  ],
  templateUrl: './admin-panel.component.html',
  styleUrl: './admin-panel.component.scss',
})
export class AdminPanelComponent implements OnInit {
  protected readonly admin = inject(AdminService);
  readonly closed = output<void>();
  protected readonly tiers: Tier[] = ['Free', 'Pro', 'Enterprise'];

  ngOnInit(): void {
    this.admin.refresh();
  }

  protected close(): void {
    this.closed.emit();
  }

  protected onTierChange(id: string, value: string): void {
    this.admin.setTier(id, value as Tier);
  }
}
