using AisStream.Api.Data;

namespace AisStream.Api.Tests;

public class WatchAreaTests
{
    private static WatchArea Area() => new()
    {
        LatMin = 50,
        LonMin = -2,
        LatMax = 51,
        LonMax = 0,
    };

    [Theory]
    [InlineData(50.5, -1.0, true)]
    [InlineData(50.0, -2.0, true)] // on the edge
    [InlineData(52.0, -1.0, false)]
    [InlineData(50.5, 1.0, false)]
    public void Contains_checks_the_box(double lat, double lon, bool expected)
    {
        Assert.Equal(expected, Area().Contains(lat, lon));
    }
}
