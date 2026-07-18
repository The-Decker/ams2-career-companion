namespace Companion.Ams2.CustomAi;

/// <summary>
/// One <c>&lt;driver&gt;</c> entry of an AMS2 custom-AI file. All skill values are 0.0–1.0
/// (0.5 ≈ realistic average); null values are omitted from the XML so the stock driver's
/// default applies. The physics scalars (~0.900–1.100, v1.6.9.8+) also affect the player
/// when driving that livery, that is how period-correct car spread is made physical.
/// </summary>
public sealed record CustomAiDriver
{
    /// <summary>Must exactly match the livery display name (case-sensitive) shown in the
    /// vehicle-select screen, the #1 failure mode this app preflights.</summary>
    public required string LiveryName { get; init; }

    public string? Name { get; init; }

    /// <summary>Three-letter country code (e.g. AUS, BRA).</summary>
    public string? Country { get; init; }

    /// <summary>Internal track ids (e.g. Monza_1971). Non-empty makes this a per-track
    /// override entry: present fields override the base entry, missing ones inherit.</summary>
    public IReadOnlyList<string> Tracks { get; init; } = [];

    public double? RaceSkill { get; init; }
    public double? QualifyingSkill { get; init; }
    public double? Aggression { get; init; }
    public double? Defending { get; init; }
    public double? Stamina { get; init; }
    public double? Consistency { get; init; }
    public double? StartReactions { get; init; }
    public double? WetSkill { get; init; }
    public double? TyreManagement { get; init; }
    public double? FuelManagement { get; init; }
    public double? BlueFlagConceding { get; init; }
    public double? WeatherTyreChanges { get; init; }
    public double? AvoidanceOfMistakes { get; init; }
    public double? AvoidanceOfForcedMistakes { get; init; }
    public double? VehicleReliability { get; init; }

    public double? WeightScalar { get; init; }
    public double? PowerScalar { get; init; }
    public double? DragScalar { get; init; }
    public double? SetupDownforce { get; init; }
    public double? SetupDownforceRandomness { get; init; }
}

/// <summary>A complete custom-AI file for one vehicle class.</summary>
public sealed record CustomAiFile
{
    /// <summary>The internal vehicle-class name, case-sensitive, it is the XML filename base
    /// (e.g. F-Vintage_Gen1 → F-Vintage_Gen1.xml) and must match the class's
    /// "Vehicle Class" value from the game's .crd data exactly.</summary>
    public required string VehicleClass { get; init; }

    public required IReadOnlyList<CustomAiDriver> Drivers { get; init; }

    /// <summary>Free-text provenance comment written at the top of the file
    /// (pack name, season, generation timestamp).</summary>
    public string? HeaderComment { get; init; }
}
