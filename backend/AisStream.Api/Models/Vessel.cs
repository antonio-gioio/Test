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

    /// <summary>IMO number (permanent vessel identifier), from static data.</summary>
    public long? Imo { get; set; }

    /// <summary>Overall length in metres (bow-to-stern dimension sum).</summary>
    public double? Length { get; set; }

    /// <summary>Beam/width in metres (port-to-starboard dimension sum).</summary>
    public double? Width { get; set; }

    /// <summary>Maximum static draught in metres.</summary>
    public double? Draught { get; set; }

    /// <summary>Estimated time of arrival as reported (raw AIS ETA string).</summary>
    public string? Eta { get; set; }

    public DateTimeOffset LastUpdate { get; set; }

    public Vessel Clone() => (Vessel)MemberwiseClone();
}
