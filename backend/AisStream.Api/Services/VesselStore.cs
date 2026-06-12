using System.Collections.Concurrent;
using AisStream.Api.Models;

namespace AisStream.Api.Services;

/// <summary>
/// In-memory store of the latest known state per vessel (keyed by MMSI).
/// </summary>
public class VesselStore
{
    private readonly ConcurrentDictionary<long, Vessel> _vessels = new();

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

        return vessel.Clone();
    }

    public IReadOnlyList<Vessel> Snapshot(TimeSpan? maxAge = null)
    {
        var cutoff = maxAge is null ? (DateTimeOffset?)null : DateTimeOffset.UtcNow - maxAge.Value;
        return _vessels.Values
            .Where(v => cutoff is null || v.LastUpdate >= cutoff)
            .Select(v => v.Clone())
            .ToList();
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
