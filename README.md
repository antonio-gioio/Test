# AIS Vessel Tracker

A live ship-tracking website built on the [aisstream.io](https://aisstream.io) AIS data feed,
with a persistent spatial backend and tiered user subscriptions.

- **Backend** — C# / ASP.NET Core 8 (`backend/AisStream.Api`). Consumes the aisstream.io
  WebSocket once, server-side, into a warm in-memory cache backed by **PostGIS**. Streams
  viewport-scoped updates to browsers over SignalR. Includes accounts (ASP.NET Core Identity
  + JWT) and subscription tiers.
- **Frontend** — Angular 19 (`frontend/`). Leaflet map that subscribes to its current
  viewport, a searchable vessel list, vessel detail + track trails, and login/plan management.

## Why it's built this way

aisstream.io is a **push-only stream, not a query API**, and AIS itself is slow: positions
arrive every 2s–3min and static data (name, type, destination) only every ~6 minutes. You
also get essentially **one connection per API key**. So the server, not each user, owns the
upstream feed:

```
                          ┌──────────────────────────────────────────────┐
aisstream.io ──WebSocket──▶ AisStreamWorker → VesselStore (warm cache)    │
  (one connection)         │        │                    │               │
                           │        ▼                    ▼               │
                           │  VesselBroadcaster    VesselPersistence      │
                           │  (per-tile/cadence)   (write-behind)         │
                           └────────│────────────────────│───────────────┘
                                    │                     ▼
                       SignalR  /hubs/vessels        PostGIS (GIST index)
                       (viewport-scoped groups)      vessels + track history
                                    │                     ▲
                                    ▼                     │ REST /api/vessels?bounds (spatial)
                              Angular + Leaflet ──────────┘      /api/vessels/{mmsi}/track
```

The cache is always warm and persisted, so a new user gets an **instant snapshot** of their
area instead of waiting minutes for the stream to fill in, and vessel names survive restarts.
Clients subscribe to *the server* by map viewport; the server filters spatially (PostGIS GIST
index) and fans out only the relevant tiles, at the refresh rate the user's plan allows.

## Subscription tiers

Enforced server-side over both REST and SignalR:

| Tier       | Max viewport | Track history | Refresh | Followed vessels |
| ---------- | ------------ | ------------- | ------- | ---------------- |
| Free       | 4 sq°        | 1 hour        | 10 s    | 3                |
| Pro        | 100 sq°      | 24 hours      | 2 s     | 50               |
| Enterprise | Unlimited    | 30 days       | 2 s     | Unlimited        |

Anonymous visitors are treated as Free. Tier is carried in the JWT; the
`POST /api/account/tier` endpoint changes it and reissues the token (in a real product this
would be driven by a billing webhook, not the client).

## Running it

### Quick start (Docker)

```bash
JWT_KEY="a-strong-secret-at-least-32-characters-long" docker compose up --build
```

Brings up the whole scaled-out stack — PostGIS, Redis, a single **ingestor**, a **web**
tier, and the nginx-served Angular frontend (migrations run automatically):

- App: <http://localhost:8080>
- API (web tier): <http://localhost:5000> (health at `/health`, metrics at `/metrics`)

The web tier is stateless and horizontally scalable — `docker compose up --scale web=3`
behind a session-affinity load balancer serves more concurrent clients without touching the
ingestor or the database.

`JWT_KEY` is optional for the demo (a placeholder default is provided) but **must** be set
to your own secret for any real deployment — the API refuses to start in Production with the
built-in development key.

### Backend (without Docker)

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and a
PostgreSQL with the PostGIS extension. Point the connection string at it via
`ConnectionStrings:Postgres` in `appsettings.json` (default:
`Host=localhost;Port=5432;Database=aisstream;Username=postgres;Password=ais`).

```bash
cd backend/AisStream.Api
dotnet run        # applies EF migrations (incl. the PostGIS extension) on startup
```

**Live data:** get a free key from [aisstream.io](https://aisstream.io) and set
`AisStream:ApiKey` (or the `AISSTREAM__APIKEY` env var). **No key?** The backend runs in
**simulation mode**, generating ~20 moving vessels so the whole stack works without
credentials. The header badge shows which mode is active.

### Frontend

Requires Node 20+.

```bash
cd frontend
npm install
npm start         # http://localhost:4200, proxies /api and /hubs to the backend
```

## API

| Endpoint | Description |
| --- | --- |
| `POST /api/auth/register` · `login` | Create account / sign in → JWT |
| `GET /api/account/me` | Current account, tier limits, followed vessels |
| `POST /api/account/tier` | Change tier, reissue token |
| `GET /api/vessels?latMin&lonMin&latMax&lonMax` | Vessels in bounds (PostGIS, tier-gated) |
| `GET /api/vessels/clusters?...&zoom=` | Grid-aggregated clusters for low-zoom/wide views (cached) |
| `GET /api/vessels/search?q=` | Global search across all vessels by name or MMSI |
| `GET /api/vessels/{mmsi}/track?hours=` | Track history (clamped to tier window) |
| `GET /api/status` | Feed mode, vessel count, caller tier |
| `GET /health` | Liveness + database health check |
| `GET /metrics` | Prometheus metrics |
| `GET /swagger` | Interactive OpenAPI documentation |
| `/hubs/vessels` | SignalR. `SubscribeViewport(bounds)` → warm snapshot + per-tile deltas; `FollowVessel` / `UnfollowVessel` (auth) |

## Clustering

At low zoom (wide area) the frontend switches from individual ship markers to
**server-side clusters**: PostGIS snaps vessels to a grid (cell size derived from the map
zoom) and returns one centroid + count per cell, so a world view stays fast instead of
rendering thousands of overlapping markers. Zooming in past the threshold returns to live,
per-vessel streaming.

## Scaling to many concurrent users

The system separates the **ingestor** from the **web** tier so it scales horizontally
(`Cluster:Role` = `All` | `Ingestor` | `Web`):

```
                    ┌──────────── Ingestor (exactly one) ─────────────┐
 aisstream.io ──ws──▶ AisStreamWorker → VesselStore → PostGIS         │
   (1 connection)   │        │                                        │
                    │        └── publishes updates ──▶ Redis pub/sub  │
                    └───────────────────────────────────│────────────┘
                                                         │ (every update)
              ┌──────────────────────┬──────────────────┴───────────────┐
              ▼                      ▼                                    ▼
        Web node 1             Web node 2            ...            Web node N   (stateless,
        consume → SignalR      consume → SignalR                   consume       scale freely)
              │                      │                                    │
        its own clients        its own clients                     its own clients
```

- **One ingestor** holds the single aisstream.io connection (the feed allows one per key),
  writes to PostGIS, and publishes every vessel update to Redis.
- **Many stateless web nodes** each consume the Redis stream and relay updates to *their own*
  connected SignalR clients. Because each node only fans out to its local connections, **no
  SignalR backplane is needed** and there's no cross-node duplication — add web nodes to add
  user capacity. New nodes warm their cache from PostGIS on startup for instant snapshots.
- **Redis** also backs a short-TTL cache for the expensive cluster aggregations, so many users
  panning the same area share one PostGIS query.
- Single-node `All` mode (the default, no Redis) keeps dev simple: the same flow runs in-process.

Verified on a live two-node setup: a client on a web node receives updates produced only by
the ingestor's feed, proving the cross-node Redis fan-out.

> Multiple web replicas need a **session-affinity** (sticky) load balancer so a client's
> SignalR negotiate and WebSocket connect land on the same node.

## Production notes

- **Health checks** at `/health` (includes a database probe) for liveness/readiness.
- **Rate limiting** on the auth endpoints (fixed window per IP) to blunt brute-force.
- **RFC 7807 ProblemDetails** for unhandled errors — no stack traces leak to clients.
- **Secrets guard**: the API refuses to start in Production with the default JWT key; set
  `Jwt__Key` (and a real `ConnectionStrings__Postgres`) via environment/secret store.
- **CORS** origins are configurable via `Cors:AllowedOrigins`.
- **Observability**: Prometheus metrics at `/metrics` (request duration/count/in-flight) plus
  structured logging.
- **CI** (`.github/workflows/ci.yml`) builds both projects and runs the backend test suite
  against a PostGIS service container.

## Tests

```bash
cd backend
dotnet test          # 31 tests: tier logic, tile math, vessel store, and HTTP integration
```

Integration tests run against PostGIS. Point them at a database with the `TEST_POSTGRES`
environment variable (defaults to the local development database).
