namespace AisStream.Api.Services;

public class AisStreamOptions
{
    public const string SectionName = "AisStream";

    /// <summary>API key from https://aisstream.io. Leave empty to run in simulation mode.</summary>
    public string ApiKey { get; set; } = "";

    public string Url { get; set; } = "wss://stream.aisstream.io/v0/stream";

    /// <summary>
    /// Bounding boxes to subscribe to, each as [[latMin, lonMin], [latMax, lonMax]].
    /// Defaults to the whole world.
    /// </summary>
    public double[][][] BoundingBoxes { get; set; } =
    [
        [[-90, -180], [90, 180]]
    ];

    /// <summary>AIS message types to subscribe to.</summary>
    public string[] MessageTypes { get; set; } = ["PositionReport", "ShipStaticData"];

    /// <summary>Vessels not heard from for this long are pruned from the snapshot.</summary>
    public int VesselTtlMinutes { get; set; } = 30;
}
