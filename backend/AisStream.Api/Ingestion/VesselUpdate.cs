using AisStream.Api.Models;

namespace AisStream.Api.Ingestion;

/// <summary>
/// A provider-agnostic AIS update. Each provider normalizes its wire format into this shape;
/// the ingestion worker merges the non-null fields into the vessel store. Unset fields are
/// left untouched, so a position-only update keeps the vessel's last known name/type, etc.
/// </summary>
public sealed class VesselUpdate
{
    public required long Mmsi { get; init; }

    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? SpeedOverGround { get; init; }
    public double? CourseOverGround { get; init; }
    public double? TrueHeading { get; init; }
    public int? NavigationalStatus { get; init; }
    public string? Name { get; init; }
    public string? ShipType { get; init; }
    public string? Destination { get; init; }
    public string? CallSign { get; init; }

    public bool HasPosition => Latitude is { } lat && Longitude is { } lon
        && lat is >= -90 and <= 90 && lon is >= -180 and <= 180;

    /// <summary>Merges this update's set fields into an existing vessel and stamps the time.</summary>
    public void ApplyTo(Vessel v)
    {
        if (Latitude.HasValue) v.Latitude = Latitude.Value;
        if (Longitude.HasValue) v.Longitude = Longitude.Value;
        if (SpeedOverGround.HasValue) v.SpeedOverGround = SpeedOverGround;
        if (CourseOverGround.HasValue) v.CourseOverGround = CourseOverGround;
        if (TrueHeading.HasValue) v.TrueHeading = TrueHeading;
        if (NavigationalStatus.HasValue) v.NavigationalStatus = NavigationalStatus;
        if (!string.IsNullOrWhiteSpace(Name)) v.Name = Name.Trim();
        if (!string.IsNullOrWhiteSpace(ShipType)) v.ShipType = ShipType;
        if (!string.IsNullOrWhiteSpace(Destination)) v.Destination = Destination.Trim();
        if (!string.IsNullOrWhiteSpace(CallSign)) v.CallSign = CallSign.Trim();
        v.LastUpdate = DateTimeOffset.UtcNow;
    }
}

/// <summary>A source of AIS vessel updates. Implementations stream until the connection drops;
/// the ingestion worker handles reconnect/backoff.</summary>
public interface IAisProvider
{
    string Name { get; }

    IAsyncEnumerable<VesselUpdate> StreamAsync(CancellationToken cancellationToken);
}
