using System.Runtime.CompilerServices;
using AisStream.Api.Ingestion;
using AisStream.Api.Messaging;
using AisStream.Api.Models;
using AisStream.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AisStream.Api.Tests;

public class VesselUpdateTests
{
    [Fact]
    public void ApplyTo_merges_set_fields_and_preserves_unset_ones()
    {
        var vessel = new Vessel
        {
            Mmsi = 1,
            Name = "OLD NAME",
            ShipType = "Cargo",
            Latitude = 10,
            Longitude = 10,
        };

        // A position-only update should not wipe the existing name/type.
        new VesselUpdate { Mmsi = 1, Latitude = 20, Longitude = 21, SpeedOverGround = 12 }.ApplyTo(vessel);

        Assert.Equal(20, vessel.Latitude);
        Assert.Equal(21, vessel.Longitude);
        Assert.Equal(12, vessel.SpeedOverGround);
        Assert.Equal("OLD NAME", vessel.Name);
        Assert.Equal("Cargo", vessel.ShipType);
    }

    [Theory]
    [InlineData(10.0, 20.0, true)]
    [InlineData(200.0, 20.0, false)]
    [InlineData(null, 20.0, false)]
    public void HasPosition_validates_coordinates(double? lat, double? lon, bool expected)
    {
        Assert.Equal(expected, new VesselUpdate { Mmsi = 1, Latitude = lat, Longitude = lon }.HasPosition);
    }

    [Fact]
    public void ApplyTo_merges_static_data_fields()
    {
        var vessel = new Vessel { Mmsi = 1, Imo = 123, Length = 100 };

        // A later update with only ETA should keep IMO/length and add ETA.
        new VesselUpdate { Mmsi = 1, Eta = "06-15 12:00 UTC", Draught = 8.5 }.ApplyTo(vessel);

        Assert.Equal(123, vessel.Imo);
        Assert.Equal(100, vessel.Length);
        Assert.Equal(8.5, vessel.Draught);
        Assert.Equal("06-15 12:00 UTC", vessel.Eta);
    }
}

public class ProviderResolutionTests
{
    [Fact]
    public void Auto_picks_AisStream_when_key_present_else_Simulator()
    {
        var options = new IngestionOptions { Provider = AisProviderType.Auto };
        Assert.Equal(AisProviderType.AisStream, options.Resolve(aisStreamKeyPresent: true));
        Assert.Equal(AisProviderType.Simulator, options.Resolve(aisStreamKeyPresent: false));
    }

    [Fact]
    public void Explicit_provider_is_respected_regardless_of_key()
    {
        var options = new IngestionOptions { Provider = AisProviderType.Datalastic };
        Assert.Equal(AisProviderType.Datalastic, options.Resolve(aisStreamKeyPresent: false));
    }
}

public class IngestionPipelineTests
{
    private sealed class FakeProvider : IAisProvider
    {
        private readonly VesselUpdate[] _updates;
        public FakeProvider(params VesselUpdate[] updates) => _updates = updates;
        public string Name => "Fake";

        public async IAsyncEnumerable<VesselUpdate> StreamAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var u in _updates)
            {
                await Task.Yield();
                yield return u;
            }
        }
    }

    private static ProviderSettings Settings(params long[] mmsiFilter) =>
        new("test", null, null, [[[-90, -180], [90, 180]]], mmsiFilter, 60, 0, 0, 100);

    [Fact]
    public async Task Runner_merges_provider_updates_into_store_and_bus()
    {
        var store = new VesselStore();
        var bus = new InProcessVesselBus();
        var published = new List<long>();
        bus.Subscribe(v => published.Add(v.Mmsi));

        var provider = new FakeProvider(
            new VesselUpdate { Mmsi = 100, Latitude = 50, Longitude = -1 },
            new VesselUpdate { Mmsi = 200, Latitude = 51, Longitude = 0 });

        var runner = new IntegrationRunner(
            provider, Settings(), store, bus, NullLogger<IntegrationRunner>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await runner.RunAsync(cts.Token);

        Assert.Equal(2, store.Count);
        Assert.True(store.TryGet(100, out _));
        Assert.Contains(100L, published);
        Assert.Contains(200L, published);
    }

    [Fact]
    public async Task Runner_honours_the_mmsi_filter()
    {
        var store = new VesselStore();
        var bus = new InProcessVesselBus();
        var provider = new FakeProvider(
            new VesselUpdate { Mmsi = 100, Latitude = 50, Longitude = -1 },
            new VesselUpdate { Mmsi = 200, Latitude = 51, Longitude = 0 });

        var runner = new IntegrationRunner(
            provider, Settings(100), store, bus, NullLogger<IntegrationRunner>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await runner.RunAsync(cts.Token);

        Assert.Equal(1, store.Count);
        Assert.True(store.TryGet(100, out _));
        Assert.False(store.TryGet(200, out _));
    }
}
