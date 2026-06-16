import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Integration, IntegrationInput, IntegrationService } from './integration.service';

describe('IntegrationService', () => {
  let service: IntegrationService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [IntegrationService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(IntegrationService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads integrations and provider types on refresh', () => {
    service.refresh();
    http.expectOne('/api/admin/integrations').flush([]);
    http.expectOne('/api/admin/provider-types').flush([]);
    expect(service.integrations()).toEqual([]);
  });

  it('posts a new integration on create', () => {
    const input: IntegrationInput = {
      name: 'test',
      provider: 'Simulator',
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
    const created = { id: 1, ...input } as Integration;

    service.create(input).subscribe((res) => expect(res.id).toBe(1));

    const req = http.expectOne('/api/admin/integrations');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.name).toBe('test');
    req.flush(created);
  });
});
