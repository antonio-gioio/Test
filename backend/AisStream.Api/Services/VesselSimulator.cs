using AisStream.Api.Models;
using Microsoft.Extensions.Options;

namespace AisStream.Api.Services;

/// <summary>
/// Generates plausible moving vessels when no aisstream.io API key is configured,
/// so the site can be demoed end-to-end without external credentials.
/// </summary>
public class VesselSimulator : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(2);

    private static readonly string[] Names =
    [
        "EVER GIVEN", "MAERSK ALTAIR", "NORDIC ORION", "PACIFIC DAWN", "STELLA MARIS",
        "ATLANTIC HERON", "BALTIC BREEZE", "CORAL EMPRESS", "GOLDEN GATE", "HARBOR STAR",
        "IRON DUKE", "JADE HORIZON", "KESTREL BAY", "LUNA ROSSA", "MISTRAL WIND",
        "NEPTUNE GLORY", "OCEAN PIONEER", "POLAR QUEST", "QUEEN OF FUNDY", "ROYAL ALBATROSS",
    ];

    private static readonly string[] Types =
        ["Cargo", "Tanker", "Passenger", "Fishing", "Tug", "Sailing", "High speed craft"];

    private static readonly string[] Destinations =
        ["ROTTERDAM", "SINGAPORE", "SHANGHAI", "HAMBURG", "NEW YORK", "ANTWERP", "FELIXSTOWE", "GENOA"];

    private readonly AisStreamOptions _options;
    private readonly VesselStore _store;
    private readonly VesselBroadcaster _broadcaster;
    private readonly ILogger<VesselSimulator> _logger;
    private readonly Random _random = new(42);
    private readonly List<SimulatedVessel> _fleet = [];

    public VesselSimulator(
        IOptions<AisStreamOptions> options,
        VesselStore store,
        VesselBroadcaster broadcaster,
        ILogger<VesselSimulator> logger)
    {
        _options = options.Value;
        _store = store;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return; // Live feed configured; nothing to simulate.
        }

        _logger.LogInformation("Simulation mode: generating {Count} fake vessels in the English Channel area", Names.Length);
        SpawnFleet();

        using var timer = new PeriodicTimer(Tick);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var sim in _fleet)
            {
                Advance(sim);
                var snapshot = _store.Upsert(sim.Mmsi, v =>
                {
                    v.Name = sim.Name;
                    v.Latitude = sim.Latitude;
                    v.Longitude = sim.Longitude;
                    v.SpeedOverGround = Math.Round(sim.SpeedKnots, 1);
                    v.CourseOverGround = Math.Round(sim.CourseDegrees, 1);
                    v.TrueHeading = Math.Round(sim.CourseDegrees);
                    v.NavigationalStatus = 0;
                    v.ShipType = sim.Type;
                    v.Destination = sim.Destination;
                    v.LastUpdate = DateTimeOffset.UtcNow;
                });
                _broadcaster.Enqueue(snapshot);
            }
        }
    }

    private void SpawnFleet()
    {
        // Roughly the English Channel / North Sea approaches.
        for (var i = 0; i < Names.Length; i++)
        {
            _fleet.Add(new SimulatedVessel
            {
                Mmsi = 200_000_000 + i,
                Name = Names[i],
                Type = Types[i % Types.Length],
                Destination = Destinations[i % Destinations.Length],
                Latitude = 49.3 + _random.NextDouble() * 2.2,
                Longitude = -4.5 + _random.NextDouble() * 7.0,
                SpeedKnots = 5 + _random.NextDouble() * 15,
                CourseDegrees = _random.NextDouble() * 360,
            });
        }
    }

    private void Advance(SimulatedVessel sim)
    {
        // Small random walk on course and speed, then dead-reckon the position.
        sim.CourseDegrees = (sim.CourseDegrees + (_random.NextDouble() - 0.5) * 6 + 360) % 360;
        sim.SpeedKnots = Math.Clamp(sim.SpeedKnots + (_random.NextDouble() - 0.5), 2, 22);

        var hours = Tick.TotalHours;
        var distanceNm = sim.SpeedKnots * hours;
        var radians = sim.CourseDegrees * Math.PI / 180;
        sim.Latitude += distanceNm / 60.0 * Math.Cos(radians);
        sim.Longitude += distanceNm / 60.0 * Math.Sin(radians) /
                         Math.Max(0.2, Math.Cos(sim.Latitude * Math.PI / 180));

        // Keep the fleet inside the demo box by bouncing off the edges.
        if (sim.Latitude < 49.0 || sim.Latitude > 51.8 || sim.Longitude < -5.0 || sim.Longitude > 3.0)
        {
            sim.Latitude = Math.Clamp(sim.Latitude, 49.0, 51.8);
            sim.Longitude = Math.Clamp(sim.Longitude, -5.0, 3.0);
            sim.CourseDegrees = (sim.CourseDegrees + 180) % 360;
        }
    }

    private class SimulatedVessel
    {
        public long Mmsi { get; init; }
        public required string Name { get; init; }
        public required string Type { get; init; }
        public required string Destination { get; init; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double SpeedKnots { get; set; }
        public double CourseDegrees { get; set; }
    }
}
