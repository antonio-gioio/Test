using AisStream.Api.Data;
using AisStream.Api.Models;
using NetTopologySuite.Geometries;

namespace AisStream.Api.Services;

public static class VesselMapping
{
    // SRID 4326 (WGS84). Points are (X=longitude, Y=latitude).
    public static readonly GeometryFactory GeometryFactory =
        NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

    public static Point ToPoint(double latitude, double longitude) =>
        GeometryFactory.CreatePoint(new Coordinate(longitude, latitude));

    public static Vessel ToDto(VesselEntity e) => new()
    {
        Mmsi = e.Mmsi,
        Name = e.Name,
        Latitude = e.Location.Y,
        Longitude = e.Location.X,
        SpeedOverGround = e.SpeedOverGround,
        CourseOverGround = e.CourseOverGround,
        TrueHeading = e.TrueHeading,
        NavigationalStatus = e.NavigationalStatus,
        ShipType = e.ShipType,
        Destination = e.Destination,
        CallSign = e.CallSign,
        Imo = e.Imo,
        Length = e.Length,
        Width = e.Width,
        Draught = e.Draught,
        Eta = e.Eta,
        LastUpdate = e.LastUpdate,
    };

    public static void Apply(VesselEntity e, Vessel v)
    {
        e.Mmsi = v.Mmsi;
        e.Name = v.Name;
        e.Location = ToPoint(v.Latitude, v.Longitude);
        e.SpeedOverGround = v.SpeedOverGround;
        e.CourseOverGround = v.CourseOverGround;
        e.TrueHeading = v.TrueHeading;
        e.NavigationalStatus = v.NavigationalStatus;
        e.ShipType = v.ShipType;
        e.Destination = v.Destination;
        e.CallSign = v.CallSign;
        e.Imo = v.Imo;
        e.Length = v.Length;
        e.Width = v.Width;
        e.Draught = v.Draught;
        e.Eta = v.Eta;
        e.LastUpdate = v.LastUpdate;
    }
}
