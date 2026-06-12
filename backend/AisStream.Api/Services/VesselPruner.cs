using Microsoft.Extensions.Options;

namespace AisStream.Api.Services;

/// <summary>Periodically removes vessels that have not reported within the configured TTL.</summary>
public class VesselPruner : BackgroundService
{
    private readonly VesselStore _store;
    private readonly AisStreamOptions _options;
    private readonly ILogger<VesselPruner> _logger;

    public VesselPruner(VesselStore store, IOptions<AisStreamOptions> options, ILogger<VesselPruner> logger)
    {
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var removed = _store.PruneOlderThan(TimeSpan.FromMinutes(_options.VesselTtlMinutes));
            if (removed > 0)
            {
                _logger.LogInformation("Pruned {Count} stale vessels", removed);
            }
        }
    }
}
