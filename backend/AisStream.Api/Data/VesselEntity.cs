using NetTopologySuite.Geometries;

namespace AisStream.Api.Data;

/// <summary>
/// Durable latest-known state of a vessel. The position is stored as a PostGIS
/// geometry point (SRID 4326) with a GIST index so viewport queries are index-backed.
/// </summary>
public class VesselEntity
{
    public long Mmsi { get; set; }
    public string? Name { get; set; }

    /// <summary>Point(longitude, latitude) in WGS84 (SRID 4326).</summary>
    public Point Location { get; set; } = default!;

    public double? SpeedOverGround { get; set; }
    public double? CourseOverGround { get; set; }
    public double? TrueHeading { get; set; }
    public int? NavigationalStatus { get; set; }
    public string? ShipType { get; set; }
    public string? Destination { get; set; }
    public string? CallSign { get; set; }
    public DateTimeOffset LastUpdate { get; set; }
}

/// <summary>A single historical position fix, used to render vessel tracks/trails.</summary>
public class VesselTrackPoint
{
    public long Id { get; set; }
    public long Mmsi { get; set; }

    /// <summary>Point(longitude, latitude) in WGS84 (SRID 4326).</summary>
    public Point Location { get; set; } = default!;

    public double? SpeedOverGround { get; set; }
    public double? CourseOverGround { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
