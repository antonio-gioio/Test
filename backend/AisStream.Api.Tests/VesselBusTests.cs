using AisStream.Api.Messaging;
using AisStream.Api.Models;

namespace AisStream.Api.Tests;

public class InProcessVesselBusTests
{
    [Fact]
    public async Task Published_vessels_reach_subscribers()
    {
        var bus = new InProcessVesselBus();
        var received = new List<long>();
        bus.Subscribe(v => received.Add(v.Mmsi));

        await bus.PublishAsync(new Vessel { Mmsi = 1 });
        await bus.PublishAsync(new Vessel { Mmsi = 2 });

        Assert.Equal(new long[] { 1, 2 }, received);
    }

    [Fact]
    public async Task All_subscribers_receive_each_message()
    {
        var bus = new InProcessVesselBus();
        var a = 0;
        var b = 0;
        bus.Subscribe(_ => a++);
        bus.Subscribe(_ => b++);

        await bus.PublishAsync(new Vessel { Mmsi = 1 });

        Assert.Equal(1, a);
        Assert.Equal(1, b);
    }
}

public class ClusterOptionsTests
{
    [Theory]
    [InlineData(NodeRole.All, true, true)]
    [InlineData(NodeRole.Ingestor, true, false)]
    [InlineData(NodeRole.Web, false, true)]
    public void Role_drives_responsibilities(NodeRole role, bool ingestion, bool realtime)
    {
        var options = new ClusterOptions { Role = role };
        Assert.Equal(ingestion, options.RunsIngestion);
        Assert.Equal(realtime, options.RunsRealtime);
    }
}
