using System.Text.Json.Serialization;
using Companion.Core.Numerics;
using Companion.Core.Scoring;

namespace Companion.FixtureGen;

// The oracle-fixture shape defined by docs/dev/oracle-fixtures.md (v2). Serialized with
// Companion.Core.Json.CoreJson.Options: camelCase properties, enums as camelCase strings,
// Rational as "n/d" strings. Property declaration order is the on-disk order.

public sealed record Fixture
{
    public required int Year { get; init; }

    /// <summary>Races that season per the f1db race table (2026: includes not-yet-run rounds).</summary>
    public required int RoundCount { get; init; }

    public required IReadOnlyList<FixtureRound> Rounds { get; init; }

    public required IReadOnlyList<ExpectedCompetitor> ExpectedDrivers { get; init; }

    /// <summary>Omitted entirely for seasons without a constructors championship (before 1958).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ExpectedCompetitor>? ExpectedConstructors { get; init; }
}

public sealed record FixtureRound
{
    public required int Round { get; init; }

    /// <summary>f1db grand-prix id, for traceability and catalog roundOverrides matching.</summary>
    public required string GrandPrixId { get; init; }

    public required Rational PointsFactor { get; init; }

    public required bool CountsForConstructors { get; init; }

    public required string? AlternateRaceTableId { get; init; }

    public required IReadOnlyList<FixtureSession> Sessions { get; init; }
}

public sealed record FixtureSession
{
    public required SessionKind Kind { get; init; }

    /// <summary>All holders of the session's fastest time (1954 British GP: seven).
    /// Always empty for sprints — sprint fastest laps are never emitted.</summary>
    public required IReadOnlyList<string> FastestLapDriverIds { get; init; }

    public required IReadOnlyList<FixtureEntry> Entries { get; init; }
}

public sealed record FixtureEntry
{
    public required string DriverId { get; init; }

    /// <summary>Championship constructor identity: f1db constructorId + "+" + engineManufacturerId,
    /// matching how season_constructor_standing is keyed.</summary>
    public required string ConstructorId { get; init; }

    public required int? Position { get; init; }

    public required FinishStatus Status { get; init; }

    public required bool SharedDrive { get; init; }

    /// <summary>False when the row is classified inside the points places but f1db records no
    /// points for it (race_points NULL/0): F2 cars in the 1958/1967/1969 German GPs,
    /// unregistered second cars (1984 Monza, 1987 Adelaide), annulled results (Hill, France
    /// 1963). Derived from f1db per-row race_points, never authored.</summary>
    public required bool PointsEligible { get; init; }

    /// <summary>Position used for the points-table lookup when official scoring paid a
    /// different rank than the classification (1967/1969 German GPs: F1 cars paid by rank
    /// among eligible cars). Null: the classification position scores.</summary>
    public required int? PointsPosition { get; init; }
}

public sealed record ExpectedCompetitor
{
    public required string Id { get; init; }

    /// <summary>Null when f1db has no position number (1997 Michael Schumacher: EX/DSQ).</summary>
    public required int? Position { get; init; }

    public required double Points { get; init; }
}
