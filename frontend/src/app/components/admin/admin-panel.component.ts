import { Component, OnInit, inject, output } from '@angular/core';
import { AdminService } from '../../services/admin.service';
import { Tier } from '../../services/auth.service';

@Component({
  selector: 'app-admin-panel',
  standalone: true,
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
