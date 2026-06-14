using AisStream.Api.Models;

namespace AisStream.Api.Messaging;

/// <summary>
/// Transport for vessel updates between the ingestor and the web tier. The ingestor
/// publishes merged vessel snapshots; every node subscribes to update its local cache and
/// (on web nodes) fan out to SignalR. Implemented in-process for single-node and over Redis
/// pub/sub for multi-node deployments.
/// </summary>
public interface IVesselBus
{
    ValueTask PublishAsync(Vessel vessel, CancellationToken cancellationToken = default);

    /// <summary>Registers a handler invoked for every vessel update received on the bus.</summary>
    void Subscribe(Action<Vessel> handler);
}
