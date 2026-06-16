using AisStream.Api.Messaging;
using AisStream.Api.Services;

namespace AisStream.Api.Ingestion;

/// <summary>
/// Runs a single integration's provider: streams updates, applies the per-integration MMSI
/// filter, merges into the store, and publishes to the bus. Reconnects with backoff on fault.
/// The ingestion manager owns one runner per enabled integration.
/// </summary>
public class IntegrationRunner
{
    private readonly IAisProvider _provider;
    private readonly ProviderSettings _settings;
    private readonly VesselStore _store;
    private readonly IVesselBus _bus;
    private readonly ILogger _logger;

    public IntegrationRunner(
        IAisProvider provider,
        ProviderSettings settings,
        VesselStore store,
        IVesselBus bus,
        ILogger logger)
    {
        _provider = provider;
        _settings = settings;
        _store = store;
        _bus = bus;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var backoff = TimeSpan.FromSeconds(2);
        var maxBackoff = TimeSpan.FromSeconds(60);
        _logger.LogInformation("Integration '{Name}' starting ({Provider})", _settings.Name, _provider.Name);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var update in _provider.StreamAsync(stoppingToken))
                {
                    if (!_settings.Allows(update.Mmsi))
                    {
                        continue;
                    }

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
                _logger.LogWarning(ex, "Integration '{Name}' faulted; reconnecting in {Backoff}",
                    _settings.Name, backoff);
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
