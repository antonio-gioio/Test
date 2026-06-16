using AisStream.Api.Ingestion;

namespace AisStream.Api.Data;

/// <summary>
/// An admin-configured AIS data source, stored in the database so integrations can be added,
/// edited, enabled, and disabled at runtime without redeploying. The ingestor runs every
/// enabled integration concurrently.
/// </summary>
public class Integration
{
    public int Id { get; set; }

    public string Name { get; set; } = "New integration";
    public AisProviderType Provider { get; set; } = AisProviderType.Simulator;
    public bool Enabled { get; set; } = true;

    public string? ApiKey { get; set; }
    public string? Url { get; set; }

    /// <summary>Bounding boxes as JSON: [[[latMin,lonMin],[latMax,lonMax]], ...].</summary>
    public string? BoundingBoxesJson { get; set; }

    /// <summary>Optional MMSI allow-list as JSON: [123456789, ...].</summary>
    public string? MmsiFilterJson { get; set; }

    /// <summary>Polling interval for request/response providers (MarineTraffic, Datalastic).</summary>
    public int PollSeconds { get; set; } = 60;

    /// <summary>Search circle for area-query providers (Datalastic).</summary>
    public double CenterLat { get; set; } = 50.5;
    public double CenterLon { get; set; } = -1.0;
    public double RadiusKm { get; set; } = 100;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Bumped whenever the config changes, so the ingestion manager restarts the runner.</summary>
    public int Revision { get; set; }
}
