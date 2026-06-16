using AisStream.Api.Data;
using AisStream.Api.Messaging;
using AisStream.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AisStream.Api.Ingestion;

/// <summary>
/// Ingestor-only hosted service that runs every enabled integration concurrently. Polls the
/// database so that when an admin adds, edits, enables, or disables an integration, the
/// corresponding runner is started, restarted (on a config change), or stopped — no redeploy.
/// </summary>
public class IngestionManager : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAisProviderFactory _providerFactory;
    private readonly VesselStore _store;
    private readonly IVesselBus _bus;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<IngestionManager> _logger;

    private readonly Dictionary<int, RunnerHandle> _runners = new();

    public IngestionManager(
        IServiceScopeFactory scopeFactory,
        IAisProviderFactory providerFactory,
        VesselStore store,
        IVesselBus bus,
        ILoggerFactory loggerFactory,
        ILogger<IngestionManager> logger)
    {
        _scopeFactory = scopeFactory;
        _providerFactory = providerFactory;
        _store = store;
        _bus = bus;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                await ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Integration reconcile failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));

        foreach (var handle in _runners.Values)
        {
            handle.Cts.Cancel();
        }
    }

    private async Task ReconcileAsync(CancellationToken stoppingToken)
    {
        List<Integration> enabled;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            enabled = await db.Integrations.AsNoTracking().Where(i => i.Enabled).ToListAsync(stoppingToken);
        }

        var enabledIds = enabled.Select(i => i.Id).ToHashSet();

        // Stop runners that were disabled or deleted.
        foreach (var (id, handle) in _runners.Where(kv => !enabledIds.Contains(kv.Key)).ToList())
        {
            _logger.LogInformation("Stopping integration runner {Id}", id);
            handle.Cts.Cancel();
            _runners.Remove(id);
        }

        // Start new runners and restart changed ones.
        foreach (var integration in enabled)
        {
            if (_runners.TryGetValue(integration.Id, out var existing))
            {
                if (existing.Revision == integration.Revision)
                {
                    continue;
                }

                _logger.LogInformation("Restarting integration runner {Id} (config changed)", integration.Id);
                existing.Cts.Cancel();
                _runners.Remove(integration.Id);
            }

            Start(integration, stoppingToken);
        }
    }

    private void Start(Integration integration, CancellationToken stoppingToken)
    {
        var settings = ProviderSettings.From(integration);
        var provider = _providerFactory.Create(integration);
        var runner = new IntegrationRunner(
            provider, settings, _store, _bus, _loggerFactory.CreateLogger<IntegrationRunner>());

        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var task = Task.Run(() => runner.RunAsync(cts.Token), cts.Token);
        _runners[integration.Id] = new RunnerHandle(integration.Revision, cts, task);
    }

    private sealed record RunnerHandle(int Revision, CancellationTokenSource Cts, Task Task);
}
