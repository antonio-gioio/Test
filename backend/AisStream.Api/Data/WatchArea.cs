namespace AisStream.Api.Data;

/// <summary>
/// A user-defined geofence. When a vessel enters the box, the owner gets a live SignalR
/// alert. Stored per user; coordinates are plain WGS84 degrees.
/// </summary>
public class WatchArea
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!;
    public ApplicationUser User { get; set; } = default!;

    public string Name { get; set; } = "Watch area";
    public double LatMin { get; set; }
    public double LonMin { get; set; }
    public double LatMax { get; set; }
    public double LonMax { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool Contains(double lat, double lon) =>
        lat >= LatMin && lat <= LatMax && lon >= LonMin && lon <= LonMax;
}
