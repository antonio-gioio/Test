import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminService } from './admin.service';

describe('AdminService', () => {
  let service: AdminService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [AdminService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AdminService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads users, stats, and audit on refresh', () => {
    service.refresh();
    http.expectOne('/api/admin/users').flush([]);
    http.expectOne('/api/admin/stats').flush({ totalUsers: 0, usersByTier: {} });
    http.expectOne('/api/admin/audit').flush([]);
    expect(service.stats()?.totalUsers).toBe(0);
  });

  it('maps tier name to the numeric value when changing tier', () => {
    service.setTier('abc', 'Enterprise');
    const post = http.expectOne('/api/admin/users/abc/tier');
    expect(post.request.body.tier).toBe(2);
    post.flush(null);
    // setTier triggers a refresh
    http.expectOne('/api/admin/users').flush([]);
    http.expectOne('/api/admin/stats').flush({ totalUsers: 1, usersByTier: {} });
    http.expectOne('/api/admin/audit').flush([]);
  });
});
