namespace AisStream.Api.Ingestion;

/// <summary>aisstream.io connection settings (free, terrestrial WebSocket feed).</summary>
public class AisStreamOptions
{
    public const string SectionName = "Ais:AisStream";

    /// <summary>API key from https://aisstream.io (free).</summary>
    public string ApiKey { get; set; } = "";

    public string Url { get; set; } = "wss://stream.aisstream.io/v0/stream";

    public string[] MessageTypes { get; set; } = ["PositionReport", "ShipStaticData"];
}

/// <summary>Digitraffic (Finnish Transport Infrastructure Agency) — free, open MQTT AIS feed.</summary>
public class DigitrafficOptions
{
    public const string SectionName = "Ais:Digitraffic";

    /// <summary>MQTT-over-WebSocket endpoint. No credentials required (open data).</summary>
    public string Url { get; set; } = "wss://meri.digitraffic.fi:443/mqtt";
}

/// <summary>MarineTraffic API — paid, global. Free trial via account credits. https://www.marinetraffic.com/en/online-services/.</summary>
public class MarineTrafficOptions
{
    public const string SectionName = "Ais:MarineTraffic";

    /// <summary>API key for the PS07 "exportvessels" service.</summary>
    public string ApiKey { get; set; } = "";

    public string BaseUrl { get; set; } = "https://services.marinetraffic.com/api/exportvessels/v:8";

    /// <summary>Polling interval; MarineTraffic is a request/response API, not a stream.</summary>
    public int PollSeconds { get; set; } = 60;
}

/// <summary>Datalastic API — paid, global. Free trial available. https://datalastic.com.</summary>
public class DatalasticOptions
{
    public const string SectionName = "Ais:Datalastic";

    public string ApiKey { get; set; } = "";

    public string BaseUrl { get; set; } = "https://api.datalastic.com/api/v0";

    /// <summary>Search circle centre and radius (km); Datalastic queries vessels in an area.</summary>
    public double Latitude { get; set; } = 50.5;
    public double Longitude { get; set; } = -1.0;
    public double RadiusKm { get; set; } = 100;

    public int PollSeconds { get; set; } = 60;
}
