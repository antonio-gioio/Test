using System.Collections.Concurrent;
using AisStream.Api.Data;
using AisStream.Api.Hubs;
using AisStream.Api.Messaging;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AisStream.Api.Services;

/// <summary>
/// Runs on web nodes. Watches the vessel bus and, when a vessel crosses into one of a user's
/// geofences, pushes an "AreaAlert" to that user's SignalR group. Enter transitions are
/// de-duplicated so a vessel sitting inside an area alerts once, not on every update.
/// </summary>
public class WatchAreaAlertService : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private readonly IVesselBus _bus;
    private readonly IHubContext<VesselHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WatchAreaAlertService> _logger;

    private volatile List<WatchArea> _areas = new();
    private readonly ConcurrentDictionary<(int AreaId, long Mmsi), byte> _inside = new();

    public WatchAreaAlertService(
        IVesselBus bus,
        IHubContext<VesselHub> hub,
        IServiceScopeFactory scopeFactory,
        ILogger<WatchAreaAlertService> logger)
    {
        _bus = bus;
        _hub = hub;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshAreasAsync(stoppingToken);
        _bus.Subscribe(OnVessel);

        using var timer = new PeriodicTimer(RefreshInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAreasAsync(stoppingToken);
        }
    }

    private void OnVessel(Models.Vessel vessel)
    {
        var areas = _areas;
        if (areas.Count == 0)
        {
            return;
        }

        foreach (var area in areas)
        {
            var key = (area.Id, vessel.Mmsi);
            if (area.Contains(vessel.Latitude, vessel.Longitude))
            {
                if (_inside.TryAdd(key, 1))
                {
                    _ = SendAlertAsync(area, vessel);
                }
            }
            else
            {
                _inside.TryRemove(key, out _);
            }
        }
    }

    private async Task SendAlertAsync(WatchArea area, Models.Vessel vessel)
    {
        try
        {
            await _hub.Clients.Group($"user:{area.UserId}").SendAsync("AreaAlert", new
            {
                areaId = area.Id,
                areaName = area.Name,
                mmsi = vessel.Mmsi,
                name = vessel.Name,
                shipType = vessel.ShipType,
                latitude = vessel.Latitude,
                longitude = vessel.Longitude,
                at = DateTimeOffset.UtcNow,
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to deliver area alert");
        }
    }

    private async Task RefreshAreasAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var areas = await db.WatchAreas.AsNoTracking().ToListAsync(cancellationToken);
            _areas = areas;

            // Drop transition state for areas that no longer exist.
            var ids = areas.Select(a => a.Id).ToHashSet();
            foreach (var key in _inside.Keys)
            {
                if (!ids.Contains(key.AreaId))
                {
                    _inside.TryRemove(key, out _);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh watch areas");
        }
    }
}
