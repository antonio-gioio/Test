using AisStream.Api.Data;
using AisStream.Api.Hubs;
using AisStream.Api.Messaging;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AisStream.Api.Services;

/// <summary>
/// Runs on web nodes. Pushes a "FollowedUpdated" event to a user whenever one of their
/// followed vessels reports — so followed ships stay live even when outside the current
/// viewport. The MMSI→followers map is refreshed periodically from the database.
/// </summary>
public class FollowedBroadcastService : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private readonly IVesselBus _bus;
    private readonly IHubContext<VesselHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FollowedBroadcastService> _logger;

    private volatile Dictionary<long, HashSet<string>> _followers = new();

    public FollowedBroadcastService(
        IVesselBus bus,
        IHubContext<VesselHub> hub,
        IServiceScopeFactory scopeFactory,
        ILogger<FollowedBroadcastService> logger)
    {
        _bus = bus;
        _hub = hub;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshAsync(stoppingToken);
        _bus.Subscribe(OnVessel);

        using var timer = new PeriodicTimer(RefreshInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }

    private void OnVessel(Models.Vessel vessel)
    {
        if (!_followers.TryGetValue(vessel.Mmsi, out var users))
        {
            return;
        }

        foreach (var userId in users)
        {
            _ = _hub.Clients.Group($"user:{userId}").SendAsync("FollowedUpdated", vessel);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.FollowedVessels.AsNoTracking()
                .Select(f => new { f.Mmsi, f.UserId })
                .ToListAsync(cancellationToken);

            var map = new Dictionary<long, HashSet<string>>();
            foreach (var row in rows)
            {
                if (!map.TryGetValue(row.Mmsi, out var set))
                {
                    map[row.Mmsi] = set = new HashSet<string>();
                }

                set.Add(row.UserId);
            }

            _followers = map;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh followed-vessel map");
        }
    }
}
