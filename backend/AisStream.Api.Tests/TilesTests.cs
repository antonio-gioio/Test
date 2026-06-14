using AisStream.Api.Services;
using AisStream.Api.Subscriptions;

namespace AisStream.Api.Tests;

public class TilesTests
{
    [Theory]
    [InlineData(50.5, -1.5, 50, -2)]
    [InlineData(0.1, 0.1, 0, 0)]
    [InlineData(-0.1, -0.1, -1, -1)]
    public void TileOf_floors_to_the_containing_cell(double lat, double lon, int expectedLat, int expectedLon)
    {
        var (tileLat, tileLon) = Tiles.TileOf(lat, lon);
        Assert.Equal(expectedLat, tileLat);
        Assert.Equal(expectedLon, tileLon);
    }

    [Fact]
    public void GroupName_distinguishes_cadence_and_tile()
    {
        Assert.Equal("t:f:50:-2", Tiles.GroupName(RefreshCadence.Fast, 50, -2));
        Assert.Equal("t:s:50:-2", Tiles.GroupName(RefreshCadence.Slow, 50, -2));
        Assert.NotEqual(
            Tiles.GroupName(RefreshCadence.Fast, 50, -2),
            Tiles.GroupName(RefreshCadence.Slow, 50, -2));
    }

    [Fact]
    public void TilesCovering_enumerates_every_touched_cell()
    {
        var bounds = new Bounds(50, -2, 51.5, 0.5);
        var tiles = Tiles.TilesCovering(bounds).ToList();

        // lat tiles 50,51 ; lon tiles -2,-1,0 => 2 x 3 = 6
        Assert.Equal(6, tiles.Count);
        Assert.Contains((50, -2), tiles);
        Assert.Contains((51, 0), tiles);
    }

    [Fact]
    public void TilesCovering_is_capped_to_avoid_runaway_joins()
    {
        var world = new Bounds(-90, -180, 90, 180);
        var tiles = Tiles.TilesCovering(world, maxTiles: 100).ToList();
        Assert.Equal(100, tiles.Count);
    }
}

public class BoundsTests
{
    [Fact]
    public void Area_is_width_times_height()
    {
        Assert.Equal(6, new Bounds(50, -2, 53, 0).AreaSqDegrees, 6);
    }

    [Fact]
    public void Contains_checks_inclusion()
    {
        var bounds = new Bounds(50, -2, 51, 0);
        Assert.True(bounds.Contains(50.5, -1));
        Assert.False(bounds.Contains(52, -1));
        Assert.False(bounds.Contains(50.5, 1));
    }
}
