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

    /// <summary>Additive per-track nudges keyed by track id, -0.05..+0.05.</summary>
    public IReadOnlyDictionary<string, double> TrackForm { get; init; } =
        new Dictionary<string, double>();
}

/// <summary>Driver ratings in the AMS2 custom-AI vocabulary verbatim, each 0.0–1.0.</summary>
public sealed record PackDriverRatings
{
    public required double RaceSkill { get; init; }
    public required double QualifyingSkill { get; init; }
    public required double Aggression { get; init; }
    public required double Defending { get; init; }
    public required double Stamina { get; init; }
    public required double Consistency { get; init; }
    public required double StartReactions { get; init; }
    public required double WetSkill { get; init; }
    public required double TyreManagement { get; init; }
    public required double AvoidanceOfMistakes { get; init; }

    /// <summary>Every rating with its camelCase JSON name, for range validation and mapping.</summary>
    public IEnumerable<(string Name, double Value)> Enumerate()
    {
        yield return ("raceSkill", RaceSkill);
        yield return ("qualifyingSkill", QualifyingSkill);
        yield return ("aggression", Aggression);
        yield return ("defending", Defending);
        yield return ("stamina", Stamina);
        yield return ("consistency", Consistency);
        yield return ("startReactions", StartReactions);
        yield return ("wetSkill", WetSkill);
        yield return ("tyreManagement", TyreManagement);
        yield return ("avoidanceOfMistakes", AvoidanceOfMistakes);
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
    }
}
