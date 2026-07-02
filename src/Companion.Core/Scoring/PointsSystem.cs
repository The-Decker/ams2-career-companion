using Companion.Core.Numerics;

namespace Companion.Core.Scoring;

/// <summary>How points are shared when two or more drivers drove the same car in a race.</summary>
public enum SharedDrivePolicy
{
    /// <summary>Points for the car's finishing position split equally among its drivers (F1 1950–1957).</summary>
    Split,

    /// <summary>Shared drives score no points at all (F1 1958 onward).</summary>
    Zero,
}

/// <summary>Who may receive the fastest-lap point.</summary>
public enum FastestLapEligibility
{
    /// <summary>Whoever set the fastest lap, classified or not (F1 1950–1959).</summary>
    Any,

    /// <summary>Only drivers classified in the top 10 (F1 2019–2024).</summary>
    ClassifiedTopTen,
}

/// <summary>The fastest-lap bonus rule, when a season awards one.</summary>
public sealed record FastestLapRule
{
    public required Rational Points { get; init; }

    /// <summary>When several drivers tie on identical lap times, split the point fractionally
    /// (1954 British GP: seven drivers received 1/7 each). When false, each tied driver
    /// receives the full point.</summary>
    public bool SplitOnTie { get; init; } = true;

    public FastestLapEligibility Eligibility { get; init; } = FastestLapEligibility.Any;

    /// <summary>Whether the fastest-lap point also counts toward the constructors championship
    /// (true 2019–2024; the 1950s had no constructors championship at all).</summary>
    public bool CountsForConstructors { get; init; }
}

/// <summary>
/// A best-N dropped-scores rule expressed as season segments. A whole-season rule is a single
/// segment spanning every round; the 1967–1980 split seasons use two segments
/// (e.g. 1967: best 5 of rounds 1–6 plus best 4 of rounds 7–11).
/// </summary>
public sealed record BestNRule
{
    public required IReadOnlyList<BestNSegment> Segments { get; init; }
}

public sealed record BestNSegment
{
    /// <summary>First round of the segment, 1-based, inclusive.</summary>
    public required int FromRound { get; init; }

    /// <summary>Last round of the segment, 1-based, inclusive.</summary>
    public required int ToRound { get; init; }

    /// <summary>How many round scores within the segment count toward the championship.</summary>
    public required int Count { get; init; }
}

/// <summary>Constructors-championship rules. Absent entirely for seasons before 1958.</summary>
public sealed record ConstructorsRule
{
    /// <summary>Only the best-placed car of each constructor scores (1958–1978);
    /// from 1979 every car scores.</summary>
    public bool BestCarOnly { get; init; }

    /// <summary>Dropped-scores rule for constructors, when the era had one.</summary>
    public BestNRule? BestN { get; init; }

    /// <summary>Race points table for the constructors championship when it differed from
    /// the drivers table (1961: constructors stayed on the 8-6-4-3-2-1 win-8 scale while
    /// drivers moved to 9-6-4-3-2-1). Null means the drivers race table applies.</summary>
    public IReadOnlyList<Rational>? RacePoints { get; init; }
}

/// <summary>
/// A complete, data-driven points system for one season. Everything the standings engine
/// needs to score results lives here; nothing is hard-coded per era.
/// </summary>
public sealed record PointsSystem
{
    /// <summary>Race points by finishing position (index 0 = winner).</summary>
    public required IReadOnlyList<Rational> RacePoints { get; init; }

    /// <summary>Sprint points by finishing position (index 0 = winner), for seasons with sprints.</summary>
    public IReadOnlyList<Rational>? SprintPoints { get; init; }

    /// <summary>Named alternate race-points tables selectable per round, e.g. the 2022+
    /// shortened-race sliding scale bands.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Rational>>? AlternateRaceTables { get; init; }

    public FastestLapRule? FastestLap { get; init; }

    public SharedDrivePolicy SharedDrivePolicy { get; init; } = SharedDrivePolicy.Zero;

    public BestNRule? DriversBestN { get; init; }

    /// <summary>Null when the season has no constructors championship (F1 before 1958).</summary>
    public ConstructorsRule? Constructors { get; init; }
}
