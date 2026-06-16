using AisStream.Api.Data;
using AisStream.Api.Ingestion.Providers;

namespace AisStream.Api.Ingestion;

/// <summary>Builds an <see cref="IAisProvider"/> from an integration's stored configuration.</summary>
public interface IAisProviderFactory
{
    IAisProvider Create(Integration integration);
}

public class AisProviderFactory : IAisProviderFactory
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILoggerFactory _loggerFactory;

    public AisProviderFactory(IHttpClientFactory httpFactory, ILoggerFactory loggerFactory)
    {
        _httpFactory = httpFactory;
        _loggerFactory = loggerFactory;
    }

    public IAisProvider Create(Integration integration)
    {
        var settings = ProviderSettings.From(integration);
        return integration.Provider switch
        {
            AisProviderType.AisStream =>
                new AisStreamProvider(settings, _loggerFactory.CreateLogger<AisStreamProvider>()),
            AisProviderType.Digitraffic =>
                new DigitrafficProvider(settings, _loggerFactory.CreateLogger<DigitrafficProvider>()),
            AisProviderType.MarineTraffic =>
                new MarineTrafficProvider(settings, _httpFactory, _loggerFactory.CreateLogger<MarineTrafficProvider>()),
            AisProviderType.Datalastic =>
                new DatalasticProvider(settings, _httpFactory, _loggerFactory.CreateLogger<DatalasticProvider>()),
            _ => new SimulatorProvider(settings),
        };
    }
}
