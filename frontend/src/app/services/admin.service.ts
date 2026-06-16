import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Tier } from './auth.service';

export interface AdminUser {
  id: string;
  email: string;
  tier: string;
  isAdmin: boolean;
  followedCount: number;
  watchAreas: number;
}

export interface AdminStats {
  totalUsers: number;
  usersByTier: Record<string, number>;
  watchAreas: number;
  followedVessels: number;
  vesselsInDatabase: number;
  trackPoints: number;
  vesselsInCache: number;
}

export interface AuditEntry {
  timestamp: string;
  actor: string;
  action: string;
  detail: string | null;
}

@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);

  readonly users = signal<AdminUser[]>([]);
  readonly stats = signal<AdminStats | null>(null);
  readonly audit = signal<AuditEntry[]>([]);

  refresh(): void {
    this.http.get<AdminUser[]>('/api/admin/users').subscribe((u) => this.users.set(u));
    this.http.get<AdminStats>('/api/admin/stats').subscribe((s) => this.stats.set(s));
    this.http.get<AuditEntry[]>('/api/admin/audit').subscribe((a) => this.audit.set(a));
  }

  setTier(id: string, tier: Tier): void {
    const tierValue = { Free: 0, Pro: 1, Enterprise: 2 }[tier];
    this.http.post(`/api/admin/users/${id}/tier`, { tier: tierValue }).subscribe(() => this.refresh());
  }

  deleteUser(id: string): void {
    this.http.delete(`/api/admin/users/${id}`).subscribe(() => this.refresh());
  }
}
