using AisStream.Api.Data;
using AisStream.Api.Ingestion;
using AisStream.Api.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AisStream.Api.Services;

/// <summary>
/// Runs on every node. Warms the in-memory cache from PostGIS at startup (so a freshly
/// started web node serves instant viewport snapshots), then subscribes to the vessel bus:
/// each update refreshes the local cache and, on web nodes, is queued for SignalR fan-out.
/// </summary>
public class VesselBusConsumer : BackgroundService
{
    private readonly IVesselBus _bus;
    private readonly VesselStore _store;
    private readonly VesselBroadcaster _broadcaster;
    private readonly ClusterOptions _cluster;
    private readonly IngestionOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VesselBusConsumer> _logger;

    public VesselBusConsumer(
        IVesselBus bus,
        VesselStore store,
        VesselBroadcaster broadcaster,
        IOptions<ClusterOptions> cluster,
        IOptions<IngestionOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<VesselBusConsumer> logger)
    {
        _bus = bus;
        _store = store;
        _broadcaster = broadcaster;
        _cluster = cluster.Value;
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Subscribe before warming so updates arriving mid-warm are applied (and not
        // clobbered by older DB state — Seed leaves live entries untouched).
        var realtime = _cluster.RunsRealtime;
        _bus.Subscribe(vessel =>
        {
            _store.Apply(vessel);
            AppMetrics.BusUpdates.Inc();
            AppMetrics.CachedVessels.Set(_store.Count);
            if (realtime)
            {
                _broadcaster.Enqueue(vessel);
            }
        });
        _logger.LogInformation("Vessel bus consumer started (realtime fan-out: {Realtime})", realtime);

        await WarmCacheAsync(stoppingToken);
    }

    private async Task WarmCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(_options.VesselTtlMinutes);
            var recent = await db.Vessels
                .AsNoTracking()
                .Where(v => v.LastUpdate >= cutoff)
                .ToListAsync(cancellationToken);

            _store.Seed(recent.Select(VesselMapping.ToDto));
            _logger.LogInformation("Warmed cache with {Count} vessels from PostGIS", recent.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not warm vessel cache from database");
        }
    }
}
