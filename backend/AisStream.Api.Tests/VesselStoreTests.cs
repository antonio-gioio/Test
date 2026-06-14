using AisStream.Api.Services;

namespace AisStream.Api.Tests;

public class VesselStoreTests
{
    private static VesselStore StoreWith(params (long Mmsi, double Lat, double Lon)[] vessels)
    {
        var store = new VesselStore();
        foreach (var (mmsi, lat, lon) in vessels)
        {
            store.Upsert(mmsi, v =>
            {
                v.Latitude = lat;
                v.Longitude = lon;
                v.LastUpdate = DateTimeOffset.UtcNow;
            });
        }

        return store;
    }

    [Fact]
    public void Upsert_creates_then_updates_in_place()
    {
        var store = new VesselStore();
        store.Upsert(1, v => v.Name = "FIRST");
        store.Upsert(1, v => v.Name = "SECOND");

        Assert.Equal(1, store.Count);
        Assert.True(store.TryGet(1, out var vessel));
        Assert.Equal("SECOND", vessel.Name);
    }

    [Fact]
    public void SnapshotInBounds_filters_by_position()
    {
        var store = StoreWith((1, 50.5, -1.0), (2, 10.0, 10.0));

        var inside = store.SnapshotInBounds(new Bounds(50, -2, 51, 0));

        Assert.Single(inside);
        Assert.Equal(1, inside[0].Mmsi);
    }

    [Fact]
    public void DrainDirty_returns_changes_once()
    {
        var store = StoreWith((1, 50.5, -1.0), (2, 50.6, -1.1));

        Assert.Equal(2, store.DrainDirty().Count);
        Assert.Empty(store.DrainDirty()); // drained; nothing new

        store.Upsert(1, v => v.Name = "MOVED");
        Assert.Single(store.DrainDirty());
    }

    [Fact]
    public void Seed_does_not_mark_entries_dirty()
    {
        var store = new VesselStore();
        store.Seed(new[]
        {
            new AisStream.Api.Models.Vessel { Mmsi = 7, Latitude = 1, Longitude = 1, LastUpdate = DateTimeOffset.UtcNow },
        });

        Assert.Equal(1, store.Count);
        Assert.Empty(store.DrainDirty());
    }

    [Fact]
    public void PruneOlderThan_removes_stale_vessels()
    {
        var store = new VesselStore();
        store.Upsert(1, v => v.LastUpdate = DateTimeOffset.UtcNow - TimeSpan.FromHours(2));
        store.Upsert(2, v => v.LastUpdate = DateTimeOffset.UtcNow);

        var removed = store.PruneOlderThan(TimeSpan.FromHours(1));

        Assert.Equal(1, removed);
        Assert.Equal(1, store.Count);
        Assert.True(store.TryGet(2, out _));
    }
}

public class ShipTypesTests
{
    [Theory]
    [InlineData(70, "Cargo")]
    [InlineData(80, "Tanker")]
    [InlineData(60, "Passenger")]
    [InlineData(30, "Fishing")]
    [InlineData(0, null)]
    [InlineData(null, null)]
    public void Describe_maps_ais_codes(int? code, string? expected)
    {
        Assert.Equal(expected, ShipTypes.Describe(code));
    }
}
