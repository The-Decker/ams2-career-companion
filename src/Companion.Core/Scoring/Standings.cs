using Companion.Core.Numerics;

namespace Companion.Core.Scoring;

/// <summary>Scoring rules plus the season shape the engine needs to compute standings.</summary>
public sealed record SeasonScoringDefinition
{
    public required PointsSystem PointsSystem { get; init; }

    /// <summary>Total championship rounds in the season, needed so best-N segment bounds are
    /// validated even before all rounds have results.</summary>
    public required int RoundCount { get; init; }

    /// <summary>Drivers excluded from the championship classification whose race results
    /// nevertheless stand (1997 Michael Schumacher). They accumulate points in the output
    /// but receive no championship position, and do not displace anyone.</summary>
    public IReadOnlySet<string> ExcludedDrivers { get; init; } = new HashSet<string>();

    /// <summary>Constructors excluded from the championship (2007 McLaren spygate; the
    /// pre-split 2018 Force India entity). Unlike excluded drivers, their counted points are
    /// zeroed — that is how the official records treated both cases — and they receive no
    /// position, so everyone below moves up.</summary>
    public IReadOnlySet<string> ExcludedConstructors { get; init; } = new HashSet<string>();

    /// <summary>Flat end-of-season points corrections per driver id, applied after best-N
    /// drops: stewards' shared-drive reallocations (1957 Collins/Trintignant ±1.5) and
    /// official-record quirks the data cannot express (1953 Fangio +0.5 Reims fastest lap,
    /// 1956 Fangio −1.5 Monaco second shared car).
    /// NOTE: adjustments and exclusions apply from the FIRST snapshot, not from the round the
    /// penalty landed — final standings are exact, mid-season snapshots for such seasons are
    /// approximations (revisit if per-round snapshots become user-facing for oracle seasons).</summary>
    public IReadOnlyDictionary<string, Rational> DriverPointsAdjustments { get; init; } =
        new Dictionary<string, Rational>();

    /// <summary>Flat end-of-season points corrections per constructor id: constructors-only
    /// penalties (1995 Benetton −10 / Williams −6, 2000 McLaren −10, 2020 Racing Point −15).</summary>
    public IReadOnlyDictionary<string, Rational> ConstructorPointsAdjustments { get; init; } =
        new Dictionary<string, Rational>();
}

/// <summary>A round's contribution to one competitor's season total.</summary>
public sealed record RoundScore
{
    public required int Round { get; init; }
    public required Rational Points { get; init; }
}

public sealed record DroppedResult
{
    public required int Round { get; init; }
    public required Rational PointsDropped { get; init; }
}

public sealed record DriverStanding
{
    public required string DriverId { get; init; }

    /// <summary>Championship position (standard competition ranking; ties share a position).
    /// Null for excluded drivers.</summary>
    public required int? Position { get; init; }

    /// <summary>All points earned across the season, before dropped scores.</summary>
    public required Rational GrossPoints { get; init; }

    /// <summary>Points counting toward the championship: gross minus dropped scores, plus
    /// any flat adjustment.</summary>
    public required Rational CountedPoints { get; init; }

    public required IReadOnlyList<RoundScore> RoundScores { get; init; }

    public required IReadOnlyList<DroppedResult> Dropped { get; init; }

    /// <summary>Non-zero when a season-level points adjustment applied (penalty or
    /// reallocation); already included in <see cref="CountedPoints"/>.</summary>
    public Rational AdjustmentPoints { get; init; }

    public bool Excluded { get; init; }
}

public sealed record ConstructorStanding
{
    public required string ConstructorId { get; init; }
    public required int? Position { get; init; }
    public required Rational GrossPoints { get; init; }
    public required Rational CountedPoints { get; init; }
    public required IReadOnlyList<RoundScore> RoundScores { get; init; }
    public required IReadOnlyList<DroppedResult> Dropped { get; init; }

    /// <summary>Non-zero when a season-level points adjustment applied; already included in
    /// <see cref="CountedPoints"/>.</summary>
    public Rational AdjustmentPoints { get; init; }

    /// <summary>Excluded from the championship: no position, counted points zeroed
    /// (gross retained for transparency).</summary>
    public bool Excluded { get; init; }
}

/// <summary>Standings as they stood after a given round.</summary>
public sealed record StandingsSnapshot
{
    public required int AfterRound { get; init; }
    public required IReadOnlyList<DriverStanding> Drivers { get; init; }

    /// <summary>Null when the season has no constructors championship.</summary>
    public IReadOnlyList<ConstructorStanding>? Constructors { get; init; }
}

/// <summary>The engine's full output: one snapshot per completed round.</summary>
public sealed record SeasonStandingsResult
{
    public required IReadOnlyList<StandingsSnapshot> Snapshots { get; init; }

    public StandingsSnapshot Final => Snapshots[^1];
}
