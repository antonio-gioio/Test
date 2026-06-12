namespace AisStream.Api.Models;

/// <summary>
/// Latest known state of a vessel, merged from AIS position reports and static data.
/// This is the DTO sent to clients over SignalR and the REST API.
/// </summary>
public class Vessel
{
    public long Mmsi { get; set; }
    public string? Name { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    /// <summary>Speed over ground in knots.</summary>
    public double? SpeedOverGround { get; set; }

    /// <summary>Course over ground in degrees.</summary>
    public double? CourseOverGround { get; set; }

    /// <summary>True heading in degrees (511 = not available in raw AIS, normalized to null).</summary>
    public double? TrueHeading { get; set; }

    public int? NavigationalStatus { get; set; }
    public string? ShipType { get; set; }
    public string? Destination { get; set; }
    public string? CallSign { get; set; }
    public DateTimeOffset LastUpdate { get; set; }

    public Vessel Clone() => (Vessel)MemberwiseClone();
}
