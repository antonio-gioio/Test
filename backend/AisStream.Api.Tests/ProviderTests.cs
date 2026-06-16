using AisStream.Api.Data;
using AisStream.Api.Ingestion;
using AisStream.Api.Ingestion.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AisStream.Api.Tests;

public class ProviderSettingsTests
{
    [Fact]
    public void From_parses_bounding_boxes_and_mmsi_filter()
    {
        var settings = ProviderSettings.From(new Integration
        {
            Name = "x",
            BoundingBoxesJson = "[[[49,-3],[51,-1]]]",
            MmsiFilterJson = "[111, 222]",
            ApiKey = "k",
        });

        Assert.Single(settings.BoundingBoxes);
        Assert.Equal(49, settings.BoundingBoxes[0][0][0]);
        Assert.Equal(new long[] { 111, 222 }, settings.MmsiFilter);
        Assert.Equal("k", settings.ApiKey);
    }

    [Fact]
    public void From_falls_back_to_world_and_defaults_on_null_or_bad_json()
    {
        var settings = ProviderSettings.From(new Integration
        {
            Name = "x",
            BoundingBoxesJson = "not json",
            MmsiFilterJson = null,
            PollSeconds = 0,
            RadiusKm = 0,
        });

        Assert.Single(settings.BoundingBoxes); // world box
        Assert.Equal(-90, settings.BoundingBoxes[0][0][0]);
        Assert.Empty(settings.MmsiFilter);
        Assert.Equal(60, settings.PollSeconds);
        Assert.Equal(100, settings.RadiusKm);
    }

    [Theory]
    [InlineData(new long[0], 999, true)]    // no filter -> allow all
    [InlineData(new long[] { 1, 2 }, 2, true)]
    [InlineData(new long[] { 1, 2 }, 3, false)]
    public void Allows_respects_the_mmsi_filter(long[] filter, long mmsi, bool expected)
    {
        var settings = new ProviderSettings("x", null, null, [[[0, 0], [0, 0]]], filter, 60, 0, 0, 100);
        Assert.Equal(expected, settings.Allows(mmsi));
    }
}

public class AisProviderFactoryTests
{
    private static AisProviderFactory Factory()
    {
        var http = new ServiceCollection().AddHttpClient().BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>();
        return new AisProviderFactory(http, NullLoggerFactory.Instance);
    }

    [Theory]
    [InlineData(AisProviderType.Simulator, typeof(SimulatorProvider))]
    [InlineData(AisProviderType.AisStream, typeof(AisStreamProvider))]
    [InlineData(AisProviderType.Digitraffic, typeof(DigitrafficProvider))]
    [InlineData(AisProviderType.MarineTraffic, typeof(MarineTrafficProvider))]
    [InlineData(AisProviderType.Datalastic, typeof(DatalasticProvider))]
    public void Create_builds_the_right_provider(AisProviderType type, Type expected)
    {
        var provider = Factory().Create(new Integration { Name = "x", Provider = type });
        Assert.IsType(expected, provider);
    }
}
