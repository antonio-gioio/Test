import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { WatchAreaService } from './watch-area.service';

describe('WatchAreaService', () => {
  let service: WatchAreaService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [WatchAreaService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(WatchAreaService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads areas on refresh', () => {
    service.refresh();
    http.expectOne('/api/watch-areas').flush([{ id: 1, name: 'A', latMin: 0, lonMin: 0, latMax: 1, lonMax: 1 }]);
    expect(service.areas().length).toBe(1);
  });

  it('posts a new area then reloads', () => {
    service.create('Channel', { latMin: 49, lonMin: -3, latMax: 51, lonMax: -1 });
    const post = http.expectOne('/api/watch-areas');
    expect(post.request.method).toBe('POST');
    expect(post.request.body.name).toBe('Channel');
    post.flush({ id: 5, name: 'Channel', latMin: 49, lonMin: -3, latMax: 51, lonMax: -1 });
    http.expectOne('/api/watch-areas').flush([]); // refresh after create
  });

  it('deletes an area then reloads', () => {
    service.remove(7);
    const del = http.expectOne('/api/watch-areas/7');
    expect(del.request.method).toBe('DELETE');
    del.flush(null);
    http.expectOne('/api/watch-areas').flush([]);
  });
});
