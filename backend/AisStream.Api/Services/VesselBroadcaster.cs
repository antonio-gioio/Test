using System.Collections.Concurrent;
using AisStream.Api.Hubs;
using AisStream.Api.Models;
using Microsoft.AspNetCore.SignalR;

namespace AisStream.Api.Services;

/// <summary>
/// Batches vessel updates and pushes them to SignalR clients once per interval,
/// so a busy AIS stream does not turn into thousands of tiny hub messages.
/// </summary>
public class VesselBroadcaster : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<long, Vessel> _pending = new();
    private readonly IHubContext<VesselHub> _hubContext;
    private readonly ILogger<VesselBroadcaster> _logger;

    public VesselBroadcaster(IHubContext<VesselHub> hubContext, ILogger<VesselBroadcaster> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public void Enqueue(Vessel vessel) => _pending[vessel.Mmsi] = vessel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(FlushInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (_pending.IsEmpty)
            {
                continue;
            }

            var batch = new List<Vessel>(_pending.Count);
            foreach (var mmsi in _pending.Keys)
            {
                if (_pending.TryRemove(mmsi, out var vessel))
                {
                    batch.Add(vessel);
                }
            }

            try
            {
                await _hubContext.Clients.All.SendAsync("VesselsUpdated", batch, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast vessel batch of {Count}", batch.Count);
            }
        }
    }
}
