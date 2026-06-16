using System.Runtime.CompilerServices;

namespace AisStream.Api.Ingestion.Providers;

/// <summary>
/// Built-in fake-fleet provider — generates plausible moving vessels in the English Channel,
/// so the app works end-to-end with no external service. Just another <see cref="IAisProvider"/>.
/// </summary>
public class SimulatorProvider : IAisProvider
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

    private readonly Random _random = new(42);
    private readonly List<SimulatedVessel> _fleet = [];

    public SimulatorProvider(ProviderSettings settings)
    {
        Name = $"Simulator ({settings.Name})";
    }

    public string Name { get; }

    public async IAsyncEnumerable<VesselUpdate> StreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        SpawnFleet();
        using var timer = new PeriodicTimer(Tick);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            foreach (var sim in _fleet)
            {
                Advance(sim);
                yield return new VesselUpdate
                {
                    Mmsi = sim.Mmsi,
                    Name = sim.Name,
                    Latitude = sim.Latitude,
                    Longitude = sim.Longitude,
                    SpeedOverGround = Math.Round(sim.SpeedKnots, 1),
                    CourseOverGround = Math.Round(sim.CourseDegrees, 1),
                    TrueHeading = Math.Round(sim.CourseDegrees),
                    NavigationalStatus = 0,
                    ShipType = sim.Type,
                    Destination = sim.Destination,
                    Imo = sim.Imo,
                    Length = sim.Length,
                    Width = sim.Width,
                    Draught = sim.Draught,
                    Eta = sim.Eta,
                };
            }
        }
    }

    private void SpawnFleet()
    {
        for (var i = 0; i < Names.Length; i++)
        {
            var length = 80 + _random.Next(0, 280);
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
                Imo = 9_000_000 + i,
                Length = length,
                Width = Math.Round(length / 7.0),
                Draught = Math.Round(4 + _random.NextDouble() * 12, 1),
                Eta = $"{_random.Next(1, 13):00}-{_random.Next(1, 28):00} {_random.Next(0, 24):00}:00 UTC",
            });
        }
    }

    private void Advance(SimulatedVessel sim)
    {
        sim.CourseDegrees = (sim.CourseDegrees + (_random.NextDouble() - 0.5) * 6 + 360) % 360;
        sim.SpeedKnots = Math.Clamp(sim.SpeedKnots + (_random.NextDouble() - 0.5), 2, 22);

        var hours = Tick.TotalHours;
        var distanceNm = sim.SpeedKnots * hours;
        var radians = sim.CourseDegrees * Math.PI / 180;
        sim.Latitude += distanceNm / 60.0 * Math.Cos(radians);
        sim.Longitude += distanceNm / 60.0 * Math.Sin(radians) /
                         Math.Max(0.2, Math.Cos(sim.Latitude * Math.PI / 180));

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
        public long Imo { get; init; }
        public double Length { get; init; }
        public double Width { get; init; }
        public double Draught { get; init; }
        public required string Eta { get; init; }
    }
}
