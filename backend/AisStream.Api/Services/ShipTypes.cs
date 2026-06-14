namespace AisStream.Api.Services;

/// <summary>Maps the numeric AIS ship-type code to a coarse human-readable category.</summary>
public static class ShipTypes
{
    public static string? Describe(int? code) => code switch
    {
        null or 0 => null,
        >= 20 and <= 29 => "Wing in ground",
        30 => "Fishing",
        31 or 32 => "Towing",
        33 => "Dredging",
        34 => "Diving ops",
        35 => "Military ops",
        36 => "Sailing",
        37 => "Pleasure craft",
        >= 40 and <= 49 => "High speed craft",
        50 => "Pilot vessel",
        51 => "Search and rescue",
        52 => "Tug",
        53 => "Port tender",
        54 => "Anti-pollution",
        55 => "Law enforcement",
        58 => "Medical transport",
        >= 60 and <= 69 => "Passenger",
        >= 70 and <= 79 => "Cargo",
        >= 80 and <= 89 => "Tanker",
        _ => "Other",
    };
}
