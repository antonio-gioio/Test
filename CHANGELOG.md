# Changelog

All notable changes to the AIS Vessel Tracker. Newest first.

## Unreleased

### Data ingestion
- **Admin-managed integrations** — data sources configured at runtime in the database (provider,
  API key, URL, bounding boxes, MMSI filter, poll/area), multiple at once, enabled/disabled live;
  the ingestion manager reconciles every ~10s. Adding a provider is a single factory case.
- **Pluggable providers** behind `IAisProvider`: Simulator, AisStream (free), Digitraffic (free,
  MQTT), MarineTraffic (paid trial), Datalastic (paid trial).

### Realtime & data
- Viewport-scoped SignalR streaming with per-tile/per-cadence groups; warm PostGIS-backed cache.
- Server-side **clustering** at low zoom (cached); **historical playback** (time-scrubber);
  **nearest-vessels** (PostGIS KNN); global search; fleet stats; CSV/GeoJSON export.
- **Followed vessels** update live even outside the viewport; **geofence watch areas** with live
  entry alerts. Richer vessel data: IMO, dimensions, draught, ETA.

### Accounts, tiers & security
- JWT **access + refresh tokens** (rotated, revocable); auto-refresh on 401 in the client.
- Free/Pro/Enterprise tiers enforced server-side; **self-service tier change disabled in
  Production**; secret-protected **billing webhook** (Stripe-ready); **per-tier API rate limit**.
- **Admin dashboard**: users, integrations, **audit log**, stats. Admin seeded on startup.

### Scale & operations
- Ingestor / web role split connected by a **Redis** bus (no SignalR backplane needed);
  resilient Redis connection. Distributed cache. Stateless, horizontally scalable web tier.
- Health probes (`/health`, `/health/live`, `/health/ready`); Prometheus metrics incl. domain
  gauges (`ais_vessels_cached`, `ais_signalr_connections`, `ais_bus_updates_total`).
- RFC 7807 ProblemDetails; CORS config; secrets guard; OpenAPI/Swagger.

### UI
- Angular Material with a skeuomorphic, fluid glass/ocean theme; mobile-responsive drawer.

### Quality
- 76 backend tests (xUnit) + 14 frontend tests (Jest/jsdom, no browser needed), run in CI
  (backend against a PostGIS service container).
