import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  let http: HttpClient;
  let ctrl: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideNoopAnimations(),
      ],
    });
    http = TestBed.inject(HttpClient);
    ctrl = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    ctrl.verify();
    localStorage.clear();
  });

  it('attaches the bearer token to API requests', () => {
    localStorage.setItem('ais.token', 'tok');
    http.get('/api/vessels/stats').subscribe();
    const req = ctrl.expectOne('/api/vessels/stats');
    expect(req.request.headers.get('Authorization')).toBe('Bearer tok');
    req.flush({});
  });

  it('refreshes the token on 401 and retries the original request', () => {
    localStorage.setItem('ais.token', 'old');
    localStorage.setItem('ais.refresh', 'r1');

    let result: unknown;
    http.get('/api/vessels').subscribe((r) => (result = r));

    const first = ctrl.expectOne('/api/vessels');
    expect(first.request.headers.get('Authorization')).toBe('Bearer old');
    first.flush(null, { status: 401, statusText: 'Unauthorized' });

    // Interceptor calls refresh, which also reloads the account.
    ctrl.expectOne('/api/auth/refresh').flush({
      token: 'new',
      refreshToken: 'r2',
      expiresAt: '',
      email: 'a@b.com',
      tier: 'Free',
    });
    ctrl.expectOne('/api/account/me').flush({
      email: 'a@b.com',
      tier: 'Free',
      isAdmin: false,
      limits: { maxViewportAreaSqDegrees: 4, maxTrackHistoryHours: 1, refreshCadence: 'Slow', maxFollowedVessels: 3 },
      followedMmsis: [],
    });

    // The original request is retried with the new token.
    const retried = ctrl.expectOne('/api/vessels');
    expect(retried.request.headers.get('Authorization')).toBe('Bearer new');
    retried.flush(['ok']);

    expect(result).toEqual(['ok']);
    expect(localStorage.getItem('ais.token')).toBe('new');
  });
});
