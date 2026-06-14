using AisStream.Api.Messaging;
using AisStream.Api.Services;

namespace AisStream.Api.Ingestion;

/// <summary>
/// Hosts the selected <see cref="IAisProvider"/> on the ingestor node. Merges each update
/// into the vessel store and publishes the merged snapshot to the bus. Reconnects with
/// exponential backoff whenever the provider's stream ends or faults.
/// </summary>
public class AisIngestionWorker : BackgroundService
{
    private readonly IAisProvider _provider;
    private readonly VesselStore _store;
    private readonly IVesselBus _bus;
    private readonly ILogger<AisIngestionWorker> _logger;

    public AisIngestionWorker(
        IAisProvider provider,
        VesselStore store,
        IVesselBus bus,
        ILogger<AisIngestionWorker> logger)
    {
        _provider = provider;
        _store = store;
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backoff = TimeSpan.FromSeconds(2);
        var maxBackoff = TimeSpan.FromSeconds(60);

        _logger.LogInformation("AIS ingestion starting with provider '{Provider}'", _provider.Name);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var update in _provider.StreamAsync(stoppingToken))
                {
                    var merged = _store.Upsert(update.Mmsi, update.ApplyTo);
                    await _bus.PublishAsync(merged, stoppingToken);
                    backoff = TimeSpan.FromSeconds(2);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Provider '{Provider}' stream failed; reconnecting in {Backoff}",
                    _provider.Name, backoff);
            }

            try
            {
                await Task.Delay(backoff, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, maxBackoff.Ticks));
        }
    }
}
