using AisStream.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AisStream.Api.Services;

/// <summary>
/// Bridges the in-memory hot cache to durable PostGIS storage. On startup it warms the
/// cache from the database (so vessel names and positions survive restarts without
/// re-incurring the AIS warm-up). Then it periodically flushes changed vessels back to
/// the database as upserts, appends track-history points, and prunes old history.
/// </summary>
public class VesselPersistenceService : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TrackPruneInterval = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly VesselStore _store;
    private readonly AisStreamOptions _options;
    private readonly ILogger<VesselPersistenceService> _logger;
    private DateTimeOffset _lastTrackPrune = DateTimeOffset.MinValue;

    public VesselPersistenceService(
        IServiceScopeFactory scopeFactory,
        VesselStore store,
        IOptions<AisStreamOptions> options,
        ILogger<VesselPersistenceService> logger)
    {
        _scopeFactory = scopeFactory;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WarmCacheAsync(stoppingToken);

        using var timer = new PeriodicTimer(FlushInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await FlushAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Vessel persistence flush failed");
            }
        }
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

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        var dirty = _store.DrainDirty();
        if (dirty.Count == 0)
        {
            await PruneTracksIfDueAsync(cancellationToken);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var mmsis = dirty.Select(v => v.Mmsi).ToList();
        var existing = await db.Vessels
            .Where(v => mmsis.Contains(v.Mmsi))
            .ToDictionaryAsync(v => v.Mmsi, cancellationToken);

        foreach (var vessel in dirty)
        {
            if (existing.TryGetValue(vessel.Mmsi, out var entity))
            {
                VesselMapping.Apply(entity, vessel);
            }
            else
            {
                entity = new VesselEntity();
                VesselMapping.Apply(entity, vessel);
                db.Vessels.Add(entity);
            }

            db.TrackPoints.Add(new VesselTrackPoint
            {
                Mmsi = vessel.Mmsi,
                Location = VesselMapping.ToPoint(vessel.Latitude, vessel.Longitude),
                SpeedOverGround = vessel.SpeedOverGround,
                CourseOverGround = vessel.CourseOverGround,
                Timestamp = vessel.LastUpdate,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await PruneTracksIfDueAsync(cancellationToken, db);
    }

    private async Task PruneTracksIfDueAsync(CancellationToken cancellationToken, AppDbContext? db = null)
    {
        if (DateTimeOffset.UtcNow - _lastTrackPrune < TrackPruneInterval)
        {
            return;
        }

        _lastTrackPrune = DateTimeOffset.UtcNow;

        // Track history is retained at most as long as the highest tier allows (30 days).
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(30);
        var owns = db is null;
        IServiceScope? scope = null;
        if (db is null)
        {
            scope = _scopeFactory.CreateScope();
            db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        }

        try
        {
            var removed = await db.TrackPoints
                .Where(t => t.Timestamp < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
            if (removed > 0)
            {
                _logger.LogInformation("Pruned {Count} expired track points", removed);
            }
        }
        finally
        {
            if (owns)
            {
                scope?.Dispose();
            }
        }
    }
}
