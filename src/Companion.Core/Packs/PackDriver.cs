namespace Companion.Core.Packs;

/// <summary>drivers.json root.</summary>
public sealed record PackDriversFile
{
    public required IReadOnlyList<PackDriver> Drivers { get; init; }
}

public sealed record PackDriver
{
    /// <summary>Lineage id, stable across era packs (e.g. "driver.j_brabham").</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>IOC-style country code (e.g. "AUS").</summary>
    public string? Country { get; init; }

    /// <summary>Birth year, the aging-curve anchor.</summary>
    public int? Born { get; init; }

    public required PackDriverRatings Ratings { get; init; }

    /// <summary>Optional per-driver CAR tuning (v1.3, additive — the juppo schema): physics
    /// scalars + reliability for THIS driver's car, overriding the team-level values in the
    /// STAGED custom-AI file only. The sim's seat-strength model keeps reading the team values,
    /// so this is sim-inert staging data (community sets author per-driver "evolving car
    /// balancing" the team-level model cannot express).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PackDriverCar? Car { get; init; }

    /// <summary>Additive per-track nudges keyed by track id, -0.05..+0.05.</summary>
    public IReadOnlyDictionary<string, double> TrackForm { get; init; } =
        new Dictionary<string, double>();
}

/// <summary>Per-driver car tuning in the AMS2 custom-AI vocabulary (all optional; ~1.0 =
/// neutral scalars). Staged-file only — never the sim.</summary>
public sealed record PackDriverCar
{
    public double? WeightScalar { get; init; }
    public double? PowerScalar { get; init; }
    public double? DragScalar { get; init; }
    public double? VehicleReliability { get; init; }

    /// <summary>Only the fields actually set, with their camelCase JSON names (validation).</summary>
    public IEnumerable<(string Name, double Value)> Enumerate()
    {
        if (WeightScalar is { } weightScalar) yield return ("weightScalar", weightScalar);
        if (PowerScalar is { } powerScalar) yield return ("powerScalar", powerScalar);
        if (DragScalar is { } dragScalar) yield return ("dragScalar", dragScalar);
        if (VehicleReliability is { } vehicleReliability) yield return ("vehicleReliability", vehicleReliability);
    }

    public bool IsEmpty => WeightScalar is null && PowerScalar is null &&
                           DragScalar is null && VehicleReliability is null;
}

/// <summary>Driver ratings in the AMS2 custom-AI vocabulary verbatim, each 0.0–1.0.
/// Only raceSkill and qualifyingSkill are required — they are the pace stats the career sim
/// and the difficulty anchor read, and every real community AI file provides them. Every
/// other stat is optional, mirroring the game format exactly: an omitted stat keeps the stock
/// driver's default in-game (jusk's 1986 set, for instance, ships no start_reactions). Keeping
/// them optional preserves NAMeS-first fidelity — a generated file omits precisely what the
/// installed file omits, so diff-aware staging stays a no-op.</summary>
public sealed record PackDriverRatings
{
    public required double RaceSkill { get; init; }
    public required double QualifyingSkill { get; init; }
    public double? Aggression { get; init; }
    public double? Defending { get; init; }
    public double? Stamina { get; init; }
    public double? Consistency { get; init; }
    public double? StartReactions { get; init; }
    public double? WetSkill { get; init; }
    public double? TyreManagement { get; init; }
    public double? AvoidanceOfMistakes { get; init; }
    public double? BlueFlagConceding { get; init; }
    public double? WeatherTyreChanges { get; init; }
    public double? AvoidanceOfForcedMistakes { get; init; }
    public double? FuelManagement { get; init; }

    /// <summary>Setup preference (v1.3, juppo schema): how much downforce the AI's setup runs
    /// (0..1) and how much it randomizes race to race. Staged-file only — the sim never reads
    /// either.</summary>
    public double? SetupDownforce { get; init; }
    public double? SetupDownforceRandomness { get; init; }

    /// <summary>Every authored rating with its camelCase JSON name, for range validation and
    /// mapping (optional ratings appear only when set).</summary>
    public IEnumerable<(string Name, double Value)> Enumerate()
    {
        yield return ("raceSkill", RaceSkill);
        yield return ("qualifyingSkill", QualifyingSkill);
        if (Aggression is { } aggression) yield return ("aggression", aggression);
        if (Defending is { } defending) yield return ("defending", defending);
        if (Stamina is { } stamina) yield return ("stamina", stamina);
        if (Consistency is { } consistency) yield return ("consistency", consistency);
        if (StartReactions is { } startReactions) yield return ("startReactions", startReactions);
        if (WetSkill is { } wetSkill) yield return ("wetSkill", wetSkill);
        if (TyreManagement is { } tyreManagement) yield return ("tyreManagement", tyreManagement);
        if (AvoidanceOfMistakes is { } avoidanceOfMistakes) yield return ("avoidanceOfMistakes", avoidanceOfMistakes);
        if (BlueFlagConceding is { } blueFlagConceding) yield return ("blueFlagConceding", blueFlagConceding);
        if (WeatherTyreChanges is { } weatherTyreChanges) yield return ("weatherTyreChanges", weatherTyreChanges);
        if (AvoidanceOfForcedMistakes is { } avoidanceOfForcedMistakes) yield return ("avoidanceOfForcedMistakes", avoidanceOfForcedMistakes);
        if (FuelManagement is { } fuelManagement) yield return ("fuelManagement", fuelManagement);
        if (SetupDownforce is { } setupDownforce) yield return ("setupDownforce", setupDownforce);
        if (SetupDownforceRandomness is { } setupDownforceRandomness) yield return ("setupDownforceRandomness", setupDownforceRandomness);
    }
}

/// <summary>A partial ratings patch (per-round aiOverrides): unset fields keep the base rating.</summary>
public sealed record PackRatingsPatch
{
    public double? RaceSkill { get; init; }
    public double? QualifyingSkill { get; init; }
    public double? Aggression { get; init; }
    public double? Defending { get; init; }
    public double? Stamina { get; init; }
    public double? Consistency { get; init; }
    public double? StartReactions { get; init; }
    public double? WetSkill { get; init; }
    public double? TyreManagement { get; init; }
    public double? AvoidanceOfMistakes { get; init; }
    public double? BlueFlagConceding { get; init; }
    public double? WeatherTyreChanges { get; init; }
    public double? AvoidanceOfForcedMistakes { get; init; }
    public double? FuelManagement { get; init; }
    public double? SetupDownforce { get; init; }
    public double? SetupDownforceRandomness { get; init; }

    /// <summary>Per-round CAR overrides (v1.3, juppo's per-track "evolving car balancing"):
    /// scalar/reliability values for THIS driver THIS round, beating both the team performance
    /// block and the driver's own car block — in the STAGED file only (never the sim).</summary>
    public double? WeightScalar { get; init; }
    public double? PowerScalar { get; init; }
    public double? DragScalar { get; init; }
    public double? VehicleReliability { get; init; }

    /// <summary>Only the ratings the patch actually sets, with their camelCase JSON names.</summary>
    public IEnumerable<(string Name, double Value)> Enumerate()
    {
        if (RaceSkill is { } raceSkill) yield return ("raceSkill", raceSkill);
        if (QualifyingSkill is { } qualifyingSkill) yield return ("qualifyingSkill", qualifyingSkill);
        if (Aggression is { } aggression) yield return ("aggression", aggression);
        if (Defending is { } defending) yield return ("defending", defending);
        if (Stamina is { } stamina) yield return ("stamina", stamina);
        if (Consistency is { } consistency) yield return ("consistency", consistency);
        if (StartReactions is { } startReactions) yield return ("startReactions", startReactions);
        if (WetSkill is { } wetSkill) yield return ("wetSkill", wetSkill);
        if (TyreManagement is { } tyreManagement) yield return ("tyreManagement", tyreManagement);
        if (AvoidanceOfMistakes is { } avoidanceOfMistakes) yield return ("avoidanceOfMistakes", avoidanceOfMistakes);
        if (BlueFlagConceding is { } blueFlagConceding) yield return ("blueFlagConceding", blueFlagConceding);
        if (WeatherTyreChanges is { } weatherTyreChanges) yield return ("weatherTyreChanges", weatherTyreChanges);
        if (AvoidanceOfForcedMistakes is { } avoidanceOfForcedMistakes) yield return ("avoidanceOfForcedMistakes", avoidanceOfForcedMistakes);
        if (FuelManagement is { } fuelManagement) yield return ("fuelManagement", fuelManagement);
        if (SetupDownforce is { } setupDownforce) yield return ("setupDownforce", setupDownforce);
        if (SetupDownforceRandomness is { } setupDownforceRandomness) yield return ("setupDownforceRandomness", setupDownforceRandomness);
    }

    /// <summary>Only the CAR fields the patch actually sets (validated on their own, wider,
    /// ranges — scalars hover around 1.0, reliability can legitimately exceed it).</summary>
    public IEnumerable<(string Name, double Value)> EnumerateCar()
    {
        if (WeightScalar is { } weightScalar) yield return ("weightScalar", weightScalar);
        if (PowerScalar is { } powerScalar) yield return ("powerScalar", powerScalar);
        if (DragScalar is { } dragScalar) yield return ("dragScalar", dragScalar);
        if (VehicleReliability is { } vehicleReliability) yield return ("vehicleReliability", vehicleReliability);
    }
}
