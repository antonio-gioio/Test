# AIS Vessel Tracker

A live ship-tracking website built on the [aisstream.io](https://aisstream.io) AIS data feed.

- **Backend** — C# / ASP.NET Core 8 (`backend/AisStream.Api`). Connects to the aisstream.io
  WebSocket, keeps an in-memory store of the latest state per vessel, and pushes batched
  updates to browsers over SignalR. Also exposes a REST snapshot endpoint.
- **Frontend** — Angular 19 (`frontend/`). Leaflet map with live vessel markers (rotated to
  the ship's heading), a searchable vessel list, and a detail panel.

## Architecture

```
aisstream.io ──WebSocket──▶ AisStreamWorker ──▶ VesselStore (in-memory, keyed by MMSI)
                                   │
                                   ▼
                           VesselBroadcaster ──SignalR /hubs/vessels──▶ Angular + Leaflet
                                                REST  /api/vessels  ──▶ (initial snapshot)
```

## Running it

### 1. Backend

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
cd backend/AisStream.Api
dotnet run
```

The API listens on `http://localhost:5000`.

**Live data:** get a free API key from [aisstream.io](https://aisstream.io) and set it in
`appsettings.json` (`AisStream:ApiKey`) or via an environment variable:

```bash
AISSTREAM__APIKEY=your-key dotnet run
```

**No key?** The backend automatically runs in **simulation mode**, generating ~20 moving
vessels in the English Channel so the whole site works end-to-end without credentials.
The header badge in the UI shows which mode is active.

You can narrow the subscription to a region by editing `AisStream:BoundingBoxes` in
`appsettings.json` (each box is `[[latMin, lonMin], [latMax, lonMax]]`). Subscribing to
the whole world produces a very high message volume.

### 2. Frontend

Requires Node 20+.

```bash
cd frontend
npm install
npm start
```

Open <http://localhost:4200>. The dev server proxies `/api` and `/hubs` (including the
SignalR WebSocket) to the backend on port 5000 (`proxy.conf.json`).

## API

| Endpoint        | Description                                          |
| --------------- | ---------------------------------------------------- |
| `GET /api/vessels` | Snapshot of all currently tracked vessels         |
| `GET /api/status`  | `{ mode: "live" \| "simulation", vesselCount }`   |
| `/hubs/vessels` | SignalR hub; server pushes `VesselsUpdated` batches  |
