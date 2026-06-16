import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

export interface AuthResponse {
  token: string;
  expiresAt: string;
  refreshToken: string;
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
  isAdmin: boolean;
  limits: TierLimits;
  followedMmsis: number[];
}

export type Tier = 'Free' | 'Pro' | 'Enterprise';

const TOKEN_KEY = 'ais.token';
const REFRESH_KEY = 'ais.refresh';

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
      .pipe(tap((res) => this.setSession(res)));
  }

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>('/api/auth/login', { email, password })
      .pipe(tap((res) => this.setSession(res)));
  }

  changeTier(tier: Tier): Observable<{ token: string }> {
    const tierValue = { Free: 0, Pro: 1, Enterprise: 2 }[tier];
    return this.http
      .post<{ token: string }>('/api/account/tier', { tier: tierValue })
      .pipe(tap((res) => this.setAccessToken(res.token)));
  }

  /** Exchanges the stored refresh token for a fresh access token. Used by the interceptor on 401. */
  refresh(): Observable<AuthResponse> {
    const refreshToken = localStorage.getItem(REFRESH_KEY);
    return this.http
      .post<AuthResponse>('/api/auth/refresh', { refreshToken })
      .pipe(tap((res) => this.setSession(res)));
  }

  logout(): void {
    const refreshToken = localStorage.getItem(REFRESH_KEY);
    if (refreshToken) {
      this.http.post('/api/auth/logout', { refreshToken }).subscribe({ error: () => undefined });
    }
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(REFRESH_KEY);
    this.token.set(null);
    this.account.set(null);
    this.tokenVersion.update((v) => v + 1);
  }

  refreshAccount(): void {
    this.http.get<Account>('/api/account/me').subscribe({
      next: (acc) => this.account.set(acc),
      error: () => undefined, // 401s are handled by the refresh interceptor
    });
  }

  private setSession(res: AuthResponse): void {
    localStorage.setItem(REFRESH_KEY, res.refreshToken);
    this.setAccessToken(res.token);
  }

  private setAccessToken(token: string): void {
    localStorage.setItem(TOKEN_KEY, token);
    this.token.set(token);
    this.tokenVersion.update((v) => v + 1);
    this.refreshAccount();
  }
}
