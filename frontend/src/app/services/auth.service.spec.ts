import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [AuthService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  function flushAccount(): void {
    http.expectOne('/api/account/me').flush({
      email: 'a@b.com',
      tier: 'Free',
      isAdmin: false,
      limits: { maxViewportAreaSqDegrees: 4, maxTrackHistoryHours: 1, refreshCadence: 'Slow', maxFollowedVessels: 3 },
      followedMmsis: [],
    });
  }

  it('stores access and refresh tokens on login', () => {
    service.login('a@b.com', 'pw').subscribe();

    const req = http.expectOne('/api/auth/login');
    expect(req.request.method).toBe('POST');
    req.flush({ token: 'access1', refreshToken: 'refresh1', expiresAt: '', email: 'a@b.com', tier: 'Free' });
    flushAccount();

    expect(service.token()).toBe('access1');
    expect(service.isLoggedIn()).toBe(true);
    expect(localStorage.getItem('ais.token')).toBe('access1');
    expect(localStorage.getItem('ais.refresh')).toBe('refresh1');
  });

  it('clears tokens and calls logout on the server', () => {
    localStorage.setItem('ais.token', 'access1');
    localStorage.setItem('ais.refresh', 'refresh1');
    // constructor of a fresh instance would refresh account; this instance was made with empty storage.
    service.logout();

    http.expectOne('/api/auth/logout').flush({});
    expect(service.token()).toBeNull();
    expect(service.isLoggedIn()).toBe(false);
    expect(localStorage.getItem('ais.token')).toBeNull();
    expect(localStorage.getItem('ais.refresh')).toBeNull();
  });
});
