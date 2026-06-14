using System.Text.Json;
using AisStream.Api.Models;
using StackExchange.Redis;

namespace AisStream.Api.Messaging;

/// <summary>
/// Redis pub/sub bus connecting the ingestor to the web tier. The ingestor publishes vessel
/// snapshots to a channel; every web node subscribes and relays to its own SignalR clients.
/// Because each node only forwards to its locally-connected clients, no SignalR backplane is
/// needed and there is no cross-node duplication — adding web nodes scales user capacity.
/// </summary>
public sealed class RedisVesselBus : IVesselBus, IDisposable
{
    private const string Channel = "vessels:updates";

    private readonly IConnectionMultiplexer _redis;
    private readonly ISubscriber _subscriber;
    private readonly ILogger<RedisVesselBus> _logger;

    public RedisVesselBus(IConnectionMultiplexer redis, ILogger<RedisVesselBus> logger)
    {
        _redis = redis;
        _subscriber = redis.GetSubscriber();
        _logger = logger;
    }

    public async ValueTask PublishAsync(Vessel vessel, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(vessel);
        await _subscriber.PublishAsync(RedisChannel.Literal(Channel), payload);
    }

    public void Subscribe(Action<Vessel> handler)
    {
        _subscriber.Subscribe(RedisChannel.Literal(Channel), (_, value) =>
        {
            if (value.IsNullOrEmpty)
            {
                return;
            }

            try
            {
                var vessel = JsonSerializer.Deserialize<Vessel>(value!);
                if (vessel is not null)
                {
                    handler(vessel);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to handle vessel message from Redis");
            }
        });
    }

    public void Dispose() => _redis.Dispose();
}
