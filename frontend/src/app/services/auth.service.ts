import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

export interface AuthResponse {
  token: string;
  expiresAt: string;
  email: string;
  tier: string;
}

export interface TierLimits {
  maxViewportAreaSqDegrees: number | null;
  maxTrackHistoryHours: number;
  refreshCadence: string;
  maxFollowedVessels: number | null;
}

export interface Account {
  email: string;
  tier: string;
  limits: TierLimits;
  followedMmsis: number[];
}

export type Tier = 'Free' | 'Pro' | 'Enterprise';

const TOKEN_KEY = 'ais.token';

/** Holds the JWT and current account, and talks to the auth/account endpoints. */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  readonly token = signal<string | null>(localStorage.getItem(TOKEN_KEY));
  readonly account = signal<Account | null>(null);
  readonly isLoggedIn = computed(() => this.token() !== null);
  readonly tier = computed<Tier>(() => (this.account()?.tier as Tier) ?? 'Free');

  /** Fires whenever the token changes (login/logout/tier change) so the hub can reconnect. */
  readonly tokenVersion = signal(0);

  constructor() {
    if (this.token()) {
      this.refreshAccount();
    }
  }

  register(email: string, password: string): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>('/api/auth/register', { email, password })
      .pipe(tap((res) => this.setToken(res.token)));
  }

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>('/api/auth/login', { email, password })
      .pipe(tap((res) => this.setToken(res.token)));
  }

  changeTier(tier: Tier): Observable<{ token: string }> {
    const tierValue = { Free: 0, Pro: 1, Enterprise: 2 }[tier];
    return this.http
      .post<{ token: string }>('/api/account/tier', { tier: tierValue })
      .pipe(tap((res) => this.setToken(res.token)));
  }

  logout(): void {
    localStorage.removeItem(TOKEN_KEY);
    this.token.set(null);
    this.account.set(null);
    this.tokenVersion.update((v) => v + 1);
  }

  refreshAccount(): void {
    this.http.get<Account>('/api/account/me').subscribe({
      next: (acc) => this.account.set(acc),
      error: () => this.logout(),
    });
  }

  private setToken(token: string): void {
    localStorage.setItem(TOKEN_KEY, token);
    this.token.set(token);
    this.tokenVersion.update((v) => v + 1);
    this.refreshAccount();
  }
}
