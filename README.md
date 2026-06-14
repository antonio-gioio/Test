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
docker compose up --build
```

Brings up PostGIS and the API (migrations run automatically). API on `http://localhost:5000`.
Then start the frontend (below).

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
| `GET /api/vessels/{mmsi}/track?hours=` | Track history (clamped to tier window) |
| `GET /api/status` | Feed mode, vessel count, caller tier |
| `/hubs/vessels` | SignalR. `SubscribeViewport(bounds)` → warm snapshot + per-tile deltas; `FollowVessel` / `UnfollowVessel` (auth) |
