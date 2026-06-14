namespace AisStream.Api.Ingestion;

/// <summary>Selectable AIS data sources. See each provider class for details.</summary>
public enum AisProviderType
{
    /// <summary>Pick AisStream if its API key is set, otherwise the built-in Simulator.</summary>
    Auto = 0,

    /// <summary>Built-in fake fleet — no external service or credentials required.</summary>
    Simulator,

    /// <summary>aisstream.io — free, terrestrial, WebSocket. https://aisstream.io</summary>
    AisStream,

    /// <summary>Digitraffic (Finnish Transport Infrastructure Agency) — free, MQTT. Baltic coverage.</summary>
    Digitraffic,

    /// <summary>MarineTraffic — paid, global, REST polling. Free trial credits available.</summary>
    MarineTraffic,

    /// <summary>Datalastic — paid, global, REST polling. Free trial available.</summary>
    Datalastic,
}

/// <summary>Shared ingestion settings, independent of which provider is selected.</summary>
public class IngestionOptions
{
    public const string SectionName = "Ais";

    public AisProviderType Provider { get; set; } = AisProviderType.Auto;

    /// <summary>Vessels not heard from for this long are pruned from snapshots.</summary>
    public int VesselTtlMinutes { get; set; } = 30;

    /// <summary>
    /// Areas of interest as [[latMin, lonMin], [latMax, lonMax]]. Used by providers that
    /// support spatial filtering (AisStream, MarineTraffic). Defaults to the whole world.
    /// </summary>
    public double[][][] BoundingBoxes { get; set; } = [[[-90, -180], [90, 180]]];

    /// <summary>Resolves <see cref="AisProviderType.Auto"/> using whether an AisStream key is set.</summary>
    public AisProviderType Resolve(bool aisStreamKeyPresent) =>
        Provider == AisProviderType.Auto
            ? (aisStreamKeyPresent ? AisProviderType.AisStream : AisProviderType.Simulator)
            : Provider;
}

/// <summary>Singleton describing which provider is actually active (for status reporting).</summary>
public record ActiveProvider(AisProviderType Type)
{
    public bool IsSimulated => Type == AisProviderType.Simulator;
}
