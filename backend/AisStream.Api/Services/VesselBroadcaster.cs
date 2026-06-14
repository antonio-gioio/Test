using System.Collections.Concurrent;
using AisStream.Api.Hubs;
using AisStream.Api.Models;
using AisStream.Api.Subscriptions;
using Microsoft.AspNetCore.SignalR;

namespace AisStream.Api.Services;

/// <summary>
/// Batches vessel updates and pushes them to SignalR clients grouped by map tile and
/// refresh cadence. A connection only receives the tiles it subscribed to, at the rate
/// its subscription tier allows (Fast = 2s for Pro/Enterprise, Slow = 10s for Free).
/// </summary>
public class VesselBroadcaster : BackgroundService
{
    private static readonly TimeSpan BaseTick = TimeSpan.FromSeconds(2);
    private const int SlowEveryNTicks = 5; // 2s * 5 = 10s

    private readonly ConcurrentDictionary<long, Vessel> _fastPending = new();
    private readonly ConcurrentDictionary<long, Vessel> _slowPending = new();
    private readonly IHubContext<VesselHub> _hubContext;
    private readonly ILogger<VesselBroadcaster> _logger;

    public VesselBroadcaster(IHubContext<VesselHub> hubContext, ILogger<VesselBroadcaster> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public void Enqueue(Vessel vessel)
    {
        _fastPending[vessel.Mmsi] = vessel;
        _slowPending[vessel.Mmsi] = vessel;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(BaseTick);
        var tick = 0;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            tick++;
            await FlushAsync(RefreshCadence.Fast, _fastPending, stoppingToken);
            if (tick % SlowEveryNTicks == 0)
            {
                await FlushAsync(RefreshCadence.Slow, _slowPending, stoppingToken);
            }
        }
    }

    private async Task FlushAsync(
        RefreshCadence cadence,
        ConcurrentDictionary<long, Vessel> pending,
        CancellationToken cancellationToken)
    {
        if (pending.IsEmpty)
        {
            return;
        }

        // Drain into per-tile batches.
        var byTile = new Dictionary<(int, int), List<Vessel>>();
        foreach (var mmsi in pending.Keys)
        {
            if (!pending.TryRemove(mmsi, out var vessel))
            {
                continue;
            }

            var tile = Tiles.TileOf(vessel.Latitude, vessel.Longitude);
            if (!byTile.TryGetValue(tile, out var list))
            {
                byTile[tile] = list = new List<Vessel>();
            }

            list.Add(vessel);
        }

        foreach (var ((tileLat, tileLon), batch) in byTile)
        {
            var group = Tiles.GroupName(cadence, tileLat, tileLon);
            try
            {
                await _hubContext.Clients.Group(group).SendAsync("VesselsUpdated", batch, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast {Count} vessels to {Group}", batch.Count, group);
            }
        }
    }
}
