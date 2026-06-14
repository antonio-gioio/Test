using System.Collections.Concurrent;
using AisStream.Api.Models;

namespace AisStream.Api.Services;

/// <summary>
/// In-memory hot cache of the latest known state per vessel (keyed by MMSI). This is the
/// fan-out path: AIS ingestion writes here and the broadcaster reads from here, so live
/// updates never wait on the database. Durability is handled separately by
/// <see cref="VesselPersistenceService"/>, which flushes dirty entries to PostGIS and
/// warms this cache from the database on startup.
/// </summary>
public class VesselStore
{
    private readonly ConcurrentDictionary<long, Vessel> _vessels = new();

    // MMSIs changed since the last persistence flush.
    private readonly ConcurrentDictionary<long, byte> _dirty = new();

    public Vessel Upsert(long mmsi, Action<Vessel> update)
    {
        var vessel = _vessels.AddOrUpdate(
            mmsi,
            _ =>
            {
                var created = new Vessel { Mmsi = mmsi };
                update(created);
                return created;
            },
            (_, existing) =>
            {
                update(existing);
                return existing;
            });

        _dirty[mmsi] = 1;
        return vessel.Clone();
    }

    /// <summary>
    /// Seeds the cache from durable storage without marking entries dirty. Existing entries
    /// are left untouched, so a live update that arrived before warm-up completes is never
    /// clobbered by older database state.
    /// </summary>
    public void Seed(IEnumerable<Vessel> vessels)
    {
        foreach (var vessel in vessels)
        {
            _vessels.TryAdd(vessel.Mmsi, vessel);
        }
    }

    /// <summary>
    /// Applies a full vessel snapshot received from the bus. Does not mark the entry dirty:
    /// persistence is driven by the ingestor's own <see cref="Upsert"/> calls, not by every
    /// node that merely caches the broadcast.
    /// </summary>
    public void Apply(Vessel vessel) => _vessels[vessel.Mmsi] = vessel;

    public bool TryGet(long mmsi, out Vessel vessel)
    {
        if (_vessels.TryGetValue(mmsi, out var stored))
        {
            vessel = stored.Clone();
            return true;
        }

        vessel = default!;
        return false;
    }

    public int Count => _vessels.Count;

    public IReadOnlyList<Vessel> Snapshot(TimeSpan? maxAge = null)
    {
        var cutoff = maxAge is null ? (DateTimeOffset?)null : DateTimeOffset.UtcNow - maxAge.Value;
        return _vessels.Values
            .Where(v => cutoff is null || v.LastUpdate >= cutoff)
            .Select(v => v.Clone())
            .ToList();
    }

    /// <summary>Latest vessels whose position falls inside the bounds (warm viewport snapshot).</summary>
    public IReadOnlyList<Vessel> SnapshotInBounds(Bounds bounds, TimeSpan? maxAge = null)
    {
        var cutoff = maxAge is null ? (DateTimeOffset?)null : DateTimeOffset.UtcNow - maxAge.Value;
        return _vessels.Values
            .Where(v => bounds.Contains(v.Latitude, v.Longitude))
            .Where(v => cutoff is null || v.LastUpdate >= cutoff)
            .Select(v => v.Clone())
            .ToList();
    }

    /// <summary>Removes and returns the vessels changed since the last call, for batched persistence.</summary>
    public IReadOnlyList<Vessel> DrainDirty()
    {
        var drained = new List<Vessel>(_dirty.Count);
        foreach (var mmsi in _dirty.Keys)
        {
            if (_dirty.TryRemove(mmsi, out _) && _vessels.TryGetValue(mmsi, out var vessel))
            {
                drained.Add(vessel.Clone());
            }
        }

        return drained;
    }

    public int PruneOlderThan(TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        var removed = 0;
        foreach (var (mmsi, vessel) in _vessels)
        {
            if (vessel.LastUpdate < cutoff && _vessels.TryRemove(mmsi, out _))
            {
                removed++;
            }
        }

        return removed;
    }
}
