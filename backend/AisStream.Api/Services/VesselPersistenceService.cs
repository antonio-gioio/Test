using AisStream.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AisStream.Api.Services;

/// <summary>
/// Flushes the in-memory hot cache to durable PostGIS storage. Runs only on the ingestor.
/// Periodically writes changed vessels back to the database as upserts, appends track-history
/// points, and prunes old history. (Cache warm-up lives in <see cref="VesselBusConsumer"/>.)
/// </summary>
public class VesselPersistenceService : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TrackPruneInterval = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly VesselStore _store;
    private readonly ILogger<VesselPersistenceService> _logger;
    private DateTimeOffset _lastTrackPrune = DateTimeOffset.MinValue;

    public VesselPersistenceService(
        IServiceScopeFactory scopeFactory,
        VesselStore store,
        ILogger<VesselPersistenceService> logger)
    {
        _scopeFactory = scopeFactory;
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Cache warm-up is handled by VesselBusConsumer (which runs on every node).
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
