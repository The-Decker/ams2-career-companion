using Companion.Core.Numerics;

namespace Companion.Core.Scoring;

public enum SessionKind
{
    Race,
    Sprint,
}

public enum FinishStatus
{
    /// <summary>Classified with a finishing position that can score points.</summary>
    Classified,

    Retired,
    Disqualified,
    NotClassified,
    DidNotStart,

    /// <summary>Excluded from the event; never scores.</summary>
    Excluded,
}

/// <summary>
/// One car/driver line of a session classification. Shared drives (two or more drivers in
/// the same car) appear as one entry per driver with the same <see cref="Position"/> and
/// <see cref="SharedDrive"/> set — the engine groups them by position to split or zero
/// the points per the season's <see cref="SharedDrivePolicy"/>.
/// </summary>
public sealed record ClassifiedEntry
{
    public required string DriverId { get; init; }

    /// <summary>Null for entries outside the constructors championship
    /// (e.g. Indianapolis 500 guest entrants, 1950–1960).</summary>
    public string? ConstructorId { get; init; }

    /// <summary>Finishing position, 1-based. Null when unclassified.</summary>
    public int? Position { get; init; }

    public FinishStatus Status { get; init; } = FinishStatus.Classified;

    /// <summary>True when this driver shared the car with at least one other driver.</summary>
    public bool SharedDrive { get; init; }

    /// <summary>False when the entry was classified but officially ineligible for
    /// championship points: F2 cars in the 1958–69 German GPs, non-registered second cars
    /// (1984 Monza, 1987 Adelaide), annulled results (Hill, France 1963). The classification
    /// stands — only points are withheld.</summary>
    public bool PointsEligible { get; init; } = true;

    /// <summary>The position used for the points-table lookup when official scoring diverged
    /// from raw classification — e.g. 1967 German GP, where F1 cars behind ineligible F2
    /// finishers received the points of their rank among eligible cars (Bonnier: classified
    /// 6th, paid as 5th). Null means the classification position scores.</summary>
    public int? PointsPosition { get; init; }
}

/// <summary>The classification of one session (race or sprint) of a round.</summary>
public sealed record SessionResult
{
    public required SessionKind Kind { get; init; }

    public required IReadOnlyList<ClassifiedEntry> Entries { get; init; }

    /// <summary>Holder(s) of the session's fastest lap. More than one id means an exact tie
    /// on lap time (1954 British GP had seven).</summary>
    public IReadOnlyList<string> FastestLapDriverIds { get; init; } = [];
}

/// <summary>Everything the engine consumes for one championship round.</summary>
public sealed record RoundResult
{
    /// <summary>Round number, 1-based, unique and ordered within a season.</summary>
    public required int Round { get; init; }

    /// <summary>False for rounds excluded from the constructors championship while still
    /// counting for drivers (Indianapolis 500, 1958–1960).</summary>
    public bool CountsForConstructors { get; init; } = true;

    /// <summary>Scales every point scored at this round: 1/2 for the six historical
    /// half-points races, 2 for the 2014 double-points finale, 1 otherwise.</summary>
    public Rational PointsFactor { get; init; } = Rational.One;

    /// <summary>Selects a named table from <see cref="PointsSystem.AlternateRaceTables"/>
    /// (2022+ shortened-race sliding scale). Null uses the season's standard race table.</summary>
    public string? AlternateRaceTableId { get; init; }

    public required IReadOnlyList<SessionResult> Sessions { get; init; }
}
