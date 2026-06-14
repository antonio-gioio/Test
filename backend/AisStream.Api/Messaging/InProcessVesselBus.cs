using AisStream.Api.Models;

namespace AisStream.Api.Messaging;

/// <summary>
/// Single-process bus: publishing invokes the registered handlers directly. Used when no
/// Redis is configured (the Role must be All, since there is nothing to connect separate
/// processes). Handlers are cheap, in-memory operations so synchronous dispatch is fine.
/// </summary>
public class InProcessVesselBus : IVesselBus
{
    private readonly List<Action<Vessel>> _handlers = new();

    public ValueTask PublishAsync(Vessel vessel, CancellationToken cancellationToken = default)
    {
        // Copy under lock-free read: handlers are only added at startup.
        for (var i = 0; i < _handlers.Count; i++)
        {
            _handlers[i](vessel);
        }

        return ValueTask.CompletedTask;
    }

    public void Subscribe(Action<Vessel> handler) => _handlers.Add(handler);
}
