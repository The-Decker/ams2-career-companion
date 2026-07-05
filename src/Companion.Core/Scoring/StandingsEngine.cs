using Companion.Core.Numerics;

namespace Companion.Core.Scoring;

/// <summary>
/// Pure, data-driven championship scoring. Replays round classifications through a
/// <see cref="PointsSystem"/> and produces per-round standings snapshots with gross vs
/// counted points and explicit dropped-results lists. No I/O, no state, no randomness —
/// identical inputs always produce identical output.
/// </summary>
public static class StandingsEngine
{
    public static SeasonStandingsResult ComputeSeason(
        SeasonScoringDefinition definition,
        IReadOnlyList<RoundResult> rounds)
    {
        Validate(definition, rounds);

        var driverScores = new Dictionary<string, List<RoundScore>>(StringComparer.Ordinal);
        var constructorScores = new Dictionary<string, List<RoundScore>>(StringComparer.Ordinal);
        var driverFinishes = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var constructorFinishes = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        var snapshots = new List<StandingsSnapshot>(rounds.Count);

        foreach (var round in rounds)
        {
            ScoreRound(definition.PointsSystem, round, driverScores, constructorScores);
            AccumulateFinishCounts(round, driverFinishes, constructorFinishes);

            snapshots.Add(new StandingsSnapshot
            {
                AfterRound = round.Round,
                Drivers = RankDrivers(definition, driverScores, driverFinishes),
                Constructors = definition.PointsSystem.Constructors is null
                    ? null
                    : RankConstructors(definition, constructorScores, constructorFinishes),
            });
        }

        return new SeasonStandingsResult { Snapshots = snapshots };
    }

    private static void Validate(SeasonScoringDefinition definition, IReadOnlyList<RoundResult> rounds)
    {
        if (rounds.Count == 0)
            throw new ArgumentException("At least one round result is required.", nameof(rounds));

        for (int i = 0; i < rounds.Count; i++)
        {
            var round = rounds[i];

            if (i > 0 && round.Round <= rounds[i - 1].Round)
                throw new ArgumentException(
                    $"Rounds must be strictly ordered; round {round.Round} follows {rounds[i - 1].Round}.",
                    nameof(rounds));

            if (round.Round < 1 || round.Round > definition.RoundCount)
                throw new ArgumentException(
                    $"Round {round.Round} is outside the season's 1..{definition.RoundCount} range.",
                    nameof(rounds));

            if (round.AlternateRaceTableId is { } tableId &&
                definition.PointsSystem.AlternateRaceTables?.ContainsKey(tableId) != true)
                throw new ArgumentException(
                    $"Round {round.Round} references unknown alternate points table '{tableId}'.",
                    nameof(rounds));

            foreach (var session in round.Sessions)
            {
                if (session.Kind == SessionKind.Sprint && definition.PointsSystem.SprintPoints is null)
                    throw new ArgumentException(
                        $"Round {round.Round} has a sprint session but the points system defines no sprint points.",
                        nameof(rounds));

                // An authored per-session points table (Increment 2c) must resolve to a real table.
                if (session.PointsTableId is { } sessionTableId && !SessionTableExists(definition.PointsSystem, sessionTableId))
                    throw new ArgumentException(
                        $"Round {round.Round} session references unknown points table '{sessionTableId}'.",
                        nameof(rounds));

                // A duplicated position that is not a flagged shared drive is a data error;
                // silently splitting (or zeroing) the winner's points would be far worse.
                foreach (var positionGroup in session.Entries
                             .Where(e => e.Status == FinishStatus.Classified && e.Position is >= 1)
                             .GroupBy(e => e.Position!.Value))
                {
                    if (positionGroup.Count() > 1 && !positionGroup.Any(e => e.SharedDrive))
                        throw new ArgumentException(
                            $"Round {round.Round}: position {positionGroup.Key} appears {positionGroup.Count()} times " +
                            "with no entry flagged SharedDrive.",
                            nameof(rounds));

                    if (positionGroup.Count() > 1 && positionGroup.Any(e => e.PointsPosition is not null))
                        throw new ArgumentException(
                            $"Round {round.Round}: PointsPosition is not supported on shared-drive entries " +
                            $"(position {positionGroup.Key}).",
                            nameof(rounds));
                }

                var sessionDrivers = session.Entries.Select(e => e.DriverId).ToHashSet(StringComparer.Ordinal);
                foreach (var holder in session.FastestLapDriverIds)
                {
                    if (!sessionDrivers.Contains(holder))
                        throw new ArgumentException(
                            $"Round {round.Round}: fastest-lap holder '{holder}' has no entry in the session.",
                            nameof(rounds));
                }
            }
        }

        foreach (var rule in new[] { definition.PointsSystem.DriversBestN, definition.PointsSystem.Constructors?.BestN })
        {
            if (rule is null)
                continue;

            var ordered = rule.Segments.OrderBy(s => s.FromRound).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                var segment = ordered[i];
                if (segment.FromRound < 1 || segment.ToRound > definition.RoundCount || segment.FromRound > segment.ToRound)
                    throw new ArgumentException(
                        $"Best-N segment {segment.FromRound}–{segment.ToRound} is invalid for a {definition.RoundCount}-round season.");
                if (segment.Count < 1)
                    throw new ArgumentException("Best-N segment count must be at least 1.");
                if (i > 0 && segment.FromRound <= ordered[i - 1].ToRound)
                    throw new ArgumentException(
                        $"Best-N segments {ordered[i - 1].FromRound}–{ordered[i - 1].ToRound} and " +
                        $"{segment.FromRound}–{segment.ToRound} overlap; a round's score would count twice.");
            }
        }
    }

    private static void ScoreRound(
        PointsSystem system,
        RoundResult round,
        Dictionary<string, List<RoundScore>> driverScores,
        Dictionary<string, List<RoundScore>> constructorScores)
    {
        var driverPoints = new Dictionary<string, Rational>(StringComparer.Ordinal);
        var constructorPoints = new Dictionary<string, Rational>(StringComparer.Ordinal);

        foreach (var session in round.Sessions)
        {
            IReadOnlyList<Rational> driversTable;
            IReadOnlyList<Rational> constructorsTable;

            if (session.PointsTableId is { } sessionTableId)
            {
                // Authored per-session table (Increment 2c): the named table drives both
                // championships for this race. Historical fixtures never set this, so the
                // 1961 constructors override below is untouched for every oracle season.
                driversTable = NamedTable(system, sessionTableId);
                constructorsTable = driversTable;
            }
            else
            {
                driversTable = session.Kind switch
                {
                    SessionKind.Sprint => system.SprintPoints!,
                    _ when round.AlternateRaceTableId is { } id => system.AlternateRaceTables![id],
                    _ => system.RacePoints,
                };

                // 1961: the constructors cup stayed on the old win-8 scale while drivers moved on.
                constructorsTable = session.Kind == SessionKind.Race && round.AlternateRaceTableId is null
                    ? system.Constructors?.RacePoints ?? driversTable
                    : driversTable;
            }

            // Half/double points scale the race classification only; sprint points were
            // always awarded in full.
            Rational sessionFactor = session.Kind == SessionKind.Race ? round.PointsFactor : Rational.One;

            // Points each car earned this session, keyed by (constructor, position) — one
            // finishing position is one car. Needed for best-car-only constructor scoring.
            var carPoints = new Dictionary<(string ConstructorId, int Position), Rational>();

            // Shared cars appear as one entry per driver at the same position; group by
            // position so the split (or zeroing) applies per car, not per driver.
            foreach (var positionGroup in session.Entries
                         .Where(e => e.Status == FinishStatus.Classified && e.Position is >= 1)
                         .GroupBy(e => e.Position!.Value))
            {
                var entries = positionGroup.ToList();
                bool shared = entries.Count > 1 || entries[0].SharedDrive;

                foreach (var entry in entries)
                {
                    if (!entry.PointsEligible)
                        continue;

                    int lookupPosition = !shared && entry.PointsPosition is { } redirected
                        ? redirected
                        : positionGroup.Key;

                    Rational driverBase = TableValue(driversTable, lookupPosition);
                    Rational constructorBase = TableValue(constructorsTable, lookupPosition);

                    (Rational driverShare, Rational constructorShare) = shared
                        ? system.SharedDrivePolicy == SharedDrivePolicy.Split
                            ? (driverBase / entries.Count, constructorBase / entries.Count)
                            : (Rational.Zero, Rational.Zero)
                        : (driverBase, constructorBase);

                    if (!driverShare.IsZero)
                        driverPoints[entry.DriverId] =
                            driverPoints.GetValueOrDefault(entry.DriverId, Rational.Zero) + driverShare * sessionFactor;

                    if (!constructorShare.IsZero && entry.ConstructorId is { } constructorId)
                    {
                        var carKey = (constructorId, positionGroup.Key);
                        carPoints[carKey] =
                            carPoints.GetValueOrDefault(carKey, Rational.Zero) + constructorShare * sessionFactor;
                    }
                }
            }

            if (system.Constructors is { } constructorsRule && round.CountsForConstructors)
            {
                foreach (var byConstructor in carPoints.GroupBy(kv => kv.Key.ConstructorId))
                {
                    Rational contribution = constructorsRule.BestCarOnly
                        ? byConstructor.Max(kv => kv.Value)
                        : byConstructor.Aggregate(Rational.Zero, (acc, kv) => acc + kv.Value);

                    constructorPoints[byConstructor.Key] =
                        constructorPoints.GetValueOrDefault(byConstructor.Key, Rational.Zero) + contribution;
                }
            }

            // Fastest lap. Ties on identical lap times may split the point fractionally
            // (1954 British GP: seven drivers, 1/7 each).
            if (system.FastestLap is { } fastestLap &&
                session.Kind == SessionKind.Race &&
                session.FastestLapDriverIds.Count > 0)
            {
                var eligible = session.FastestLapDriverIds
                    .Where(driverId => IsFastestLapEligible(fastestLap.Eligibility, session, driverId))
                    .ToList();

                if (eligible.Count > 0)
                {
                    Rational each = (fastestLap.SplitOnTie ? fastestLap.Points / eligible.Count : fastestLap.Points)
                                    * sessionFactor;

                    foreach (var driverId in eligible)
                    {
                        driverPoints[driverId] = driverPoints.GetValueOrDefault(driverId, Rational.Zero) + each;

                        if (fastestLap.CountsForConstructors && round.CountsForConstructors &&
                            system.Constructors is not null &&
                            FindConstructor(session, driverId) is { } constructorId)
                            constructorPoints[constructorId] =
                                constructorPoints.GetValueOrDefault(constructorId, Rational.Zero) + each;
                    }
                }
            }

            // Every participant enters the standings, points or not.
            foreach (var entry in session.Entries)
            {
                driverPoints.TryAdd(entry.DriverId, Rational.Zero);
                if (entry.ConstructorId is { } cid && system.Constructors is not null && round.CountsForConstructors)
                    constructorPoints.TryAdd(cid, Rational.Zero);
            }
        }

        foreach (var (driverId, points) in driverPoints)
            Scores(driverScores, driverId).Add(new RoundScore { Round = round.Round, Points = points });

        foreach (var (constructorId, points) in constructorPoints)
            Scores(constructorScores, constructorId).Add(new RoundScore { Round = round.Round, Points = points });
    }

    private static Rational TableValue(IReadOnlyList<Rational> table, int position) =>
        position >= 1 && position <= table.Count ? table[position - 1] : Rational.Zero;

    /// <summary>Whether a session's authored <see cref="SessionResult.PointsTableId"/> names a table
    /// the points system actually defines (validated before scoring).</summary>
    private static bool SessionTableExists(PointsSystem system, string tableId) =>
        tableId switch
        {
            "primary" => true,
            "sprint" => system.SprintPoints is not null,
            _ => system.AlternateRaceTables?.ContainsKey(tableId) == true,
        };

    /// <summary>Resolves a session's authored <see cref="SessionResult.PointsTableId"/> to a table:
    /// "primary" → the standard race table, "sprint" → the sprint table, any other key → a named
    /// alternate table. <see cref="Validate"/> guarantees the referenced table exists.</summary>
    private static IReadOnlyList<Rational> NamedTable(PointsSystem system, string tableId) =>
        tableId switch
        {
            "primary" => system.RacePoints,
            "sprint" => system.SprintPoints!,
            _ => system.AlternateRaceTables![tableId],
        };

    private static bool IsFastestLapEligible(FastestLapEligibility eligibility, SessionResult session, string driverId) =>
        eligibility switch
        {
            FastestLapEligibility.Any => session.Entries.Any(e =>
                e.DriverId == driverId && e.PointsEligible),
            FastestLapEligibility.ClassifiedTopTen => session.Entries.Any(e =>
                e.DriverId == driverId && e.PointsEligible &&
                e.Status == FinishStatus.Classified && e.Position is >= 1 and <= 10),
            _ => throw new ArgumentOutOfRangeException(nameof(eligibility)),
        };

    private static string? FindConstructor(SessionResult session, string driverId) =>
        session.Entries.FirstOrDefault(e => e.DriverId == driverId)?.ConstructorId;

    private static void AccumulateFinishCounts(
        RoundResult round,
        Dictionary<string, List<int>> driverFinishes,
        Dictionary<string, List<int>> constructorFinishes)
    {
        foreach (var session in round.Sessions)
        {
            if (session.Kind != SessionKind.Race)
                continue;

            var countedCars = new HashSet<(string ConstructorId, int Position)>();

            foreach (var entry in session.Entries)
            {
                if (entry.Status != FinishStatus.Classified || entry.Position is not (>= 1 and var position))
                    continue;

                Finishes(driverFinishes, entry.DriverId).Add(position);

                // Constructor countback mirrors constructor scoring: excluded rounds don't
                // count, and a shared car is one finish, not one per driver.
                if (round.CountsForConstructors &&
                    entry.ConstructorId is { } constructorId &&
                    countedCars.Add((constructorId, position)))
                    Finishes(constructorFinishes, constructorId).Add(position);
            }
        }
    }

    private static (Rational Gross, Rational Counted, List<DroppedResult> Dropped) ApplyBestN(
        IReadOnlyList<RoundScore> scores,
        BestNRule? rule)
    {
        Rational gross = scores.Aggregate(Rational.Zero, (acc, s) => acc + s.Points);
        if (rule is null)
            return (gross, gross, []);

        var counted = Rational.Zero;
        var dropped = new List<DroppedResult>();
        var coveredRounds = new HashSet<int>();

        foreach (var segment in rule.Segments)
        {
            var inSegment = scores
                .Where(s => s.Round >= segment.FromRound && s.Round <= segment.ToRound)
                .OrderByDescending(s => s.Points)
                .ThenBy(s => s.Round)
                .ToList();

            foreach (var score in inSegment)
                coveredRounds.Add(score.Round);

            foreach (var kept in inSegment.Take(segment.Count))
                counted += kept.Points;

            foreach (var droppedScore in inSegment.Skip(segment.Count))
            {
                if (!droppedScore.Points.IsZero)
                    dropped.Add(new DroppedResult { Round = droppedScore.Round, PointsDropped = droppedScore.Points });
            }
        }

        // Rounds no segment covers always count (a rules-data gap, not a reason to lose points).
        foreach (var score in scores)
        {
            if (!coveredRounds.Contains(score.Round))
                counted += score.Points;
        }

        dropped.Sort((a, b) => a.Round.CompareTo(b.Round));
        return (gross, counted, dropped);
    }

    private sealed record Aggregate(
        string Id,
        Rational Gross,
        Rational Counted,
        IReadOnlyList<RoundScore> Scores,
        IReadOnlyList<DroppedResult> Dropped,
        Rational Adjustment,
        int[] Countback,
        bool Excluded);

    private static IReadOnlyList<DriverStanding> RankDrivers(
        SeasonScoringDefinition definition,
        Dictionary<string, List<RoundScore>> roundScores,
        Dictionary<string, List<int>> finishes)
    {
        var aggregates = roundScores
            .Select(kv =>
            {
                var (gross, counted, dropped) = ApplyBestN(kv.Value, definition.PointsSystem.DriversBestN);
                var adjustment = definition.DriverPointsAdjustments.GetValueOrDefault(kv.Key, Rational.Zero);
                return new Aggregate(kv.Key, gross, counted + adjustment, kv.Value.ToList(), dropped,
                    adjustment,
                    BuildCountback(finishes.GetValueOrDefault(kv.Key)),
                    definition.ExcludedDrivers.Contains(kv.Key));
            })
            .ToList();

        var standings = AssignPositions(aggregates.Where(a => !a.Excluded))
            .Select(ranked => new DriverStanding
            {
                DriverId = ranked.Aggregate.Id,
                Position = ranked.Position,
                GrossPoints = ranked.Aggregate.Gross,
                CountedPoints = ranked.Aggregate.Counted,
                RoundScores = ranked.Aggregate.Scores,
                Dropped = ranked.Aggregate.Dropped,
                AdjustmentPoints = ranked.Aggregate.Adjustment,
            })
            .ToList();

        // Excluded drivers keep their points (1997: the results stood) but take no position.
        standings.AddRange(aggregates
            .Where(a => a.Excluded)
            .OrderBy(a => a.Id, StringComparer.Ordinal)
            .Select(a => new DriverStanding
            {
                DriverId = a.Id,
                Position = null,
                GrossPoints = a.Gross,
                CountedPoints = a.Counted,
                RoundScores = a.Scores,
                Dropped = a.Dropped,
                AdjustmentPoints = a.Adjustment,
                Excluded = true,
            }));

        return standings;
    }

    private static IReadOnlyList<ConstructorStanding> RankConstructors(
        SeasonScoringDefinition definition,
        Dictionary<string, List<RoundScore>> roundScores,
        Dictionary<string, List<int>> finishes)
    {
        var aggregates = roundScores
            .Select(kv =>
            {
                var (gross, counted, dropped) = ApplyBestN(kv.Value, definition.PointsSystem.Constructors?.BestN);
                var adjustment = definition.ConstructorPointsAdjustments.GetValueOrDefault(kv.Key, Rational.Zero);
                return new Aggregate(kv.Key, gross, counted + adjustment, kv.Value.ToList(), dropped,
                    adjustment,
                    BuildCountback(finishes.GetValueOrDefault(kv.Key)),
                    definition.ExcludedConstructors.Contains(kv.Key));
            })
            .ToList();

        var standings = AssignPositions(aggregates.Where(a => !a.Excluded))
            .Select(ranked => new ConstructorStanding
            {
                ConstructorId = ranked.Aggregate.Id,
                Position = ranked.Position,
                GrossPoints = ranked.Aggregate.Gross,
                CountedPoints = ranked.Aggregate.Counted,
                RoundScores = ranked.Aggregate.Scores,
                Dropped = ranked.Aggregate.Dropped,
                AdjustmentPoints = ranked.Aggregate.Adjustment,
            })
            .ToList();

        // Excluded constructors lose their points outright (2007 McLaren was stripped, not
        // merely unclassified) and everyone below moves up.
        standings.AddRange(aggregates
            .Where(a => a.Excluded)
            .OrderBy(a => a.Id, StringComparer.Ordinal)
            .Select(a => new ConstructorStanding
            {
                ConstructorId = a.Id,
                Position = null,
                GrossPoints = a.Gross,
                CountedPoints = Rational.Zero,
                RoundScores = a.Scores,
                Dropped = a.Dropped,
                AdjustmentPoints = a.Adjustment,
                Excluded = true,
            }));

        return standings;
    }

    /// <summary>Counts of finishes per position (index 0 = wins), used for countback tiebreaks.
    /// Compared lexicographically: more wins beats fewer, then seconds, and so on.</summary>
    private static int[] BuildCountback(List<int>? positions)
    {
        if (positions is null || positions.Count == 0)
            return [];
        var counts = new int[positions.Max()];
        foreach (var position in positions)
            counts[position - 1]++;
        return counts;
    }

    private static int CompareCountback(int[] a, int[] b)
    {
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int countA = i < a.Length ? a[i] : 0;
            int countB = i < b.Length ? b[i] : 0;
            if (countA != countB)
                return countB.CompareTo(countA);
        }
        return 0;
    }

    private static IEnumerable<(Aggregate Aggregate, int Position)> AssignPositions(IEnumerable<Aggregate> aggregates)
    {
        var ordered = aggregates
            .OrderByDescending(a => a.Counted)
            .ThenBy(a => a.Countback, Comparer<int[]>.Create(CompareCountback))
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .ToList();

        // Standard competition ranking: dead heats share a position (1, 2, 2, 4). The ordinal
        // id sort above is presentation order only and never affects the position number.
        int index = 0;
        int position = 0;
        Aggregate? previous = null;

        foreach (var aggregate in ordered)
        {
            index++;
            if (previous is null ||
                aggregate.Counted != previous.Counted ||
                CompareCountback(aggregate.Countback, previous.Countback) != 0)
            {
                position = index;
                previous = aggregate;
            }
            yield return (aggregate, position);
        }
    }

    private static List<RoundScore> Scores(Dictionary<string, List<RoundScore>> map, string key)
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = [];
        return list;
    }

    private static List<int> Finishes(Dictionary<string, List<int>> map, string key)
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = [];
        return list;
    }
}
