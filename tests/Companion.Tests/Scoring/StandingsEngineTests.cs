using Companion.Core.Numerics;
using Companion.Core.Scoring;

namespace Companion.Tests.Scoring;

public class StandingsEngineTests
{
    // ---------- helpers ----------

    private static IReadOnlyList<Rational> Table(params long[] points) =>
        points.Select(p => new Rational(p)).ToArray();

    private static ClassifiedEntry Entry(
        string driverId,
        int? position,
        string? constructorId = null,
        FinishStatus status = FinishStatus.Classified,
        bool shared = false,
        bool pointsEligible = true,
        int? pointsPosition = null) => new()
    {
        DriverId = driverId,
        ConstructorId = constructorId,
        Position = position,
        Status = status,
        SharedDrive = shared,
        PointsEligible = pointsEligible,
        PointsPosition = pointsPosition,
    };

    private static RoundResult Race(
        int round,
        ClassifiedEntry[] entries,
        string[]? fastestLap = null,
        Rational? pointsFactor = null,
        string? alternateTableId = null) => new()
    {
        Round = round,
        PointsFactor = pointsFactor ?? Rational.One,
        AlternateRaceTableId = alternateTableId,
        Sessions =
        [
            new SessionResult
            {
                Kind = SessionKind.Race,
                Entries = entries,
                FastestLapDriverIds = fastestLap ?? [],
            },
        ],
    };

    private static SeasonStandingsResult Compute(PointsSystem system, int roundCount, params RoundResult[] rounds) =>
        StandingsEngine.ComputeSeason(
            new SeasonScoringDefinition { PointsSystem = system, RoundCount = roundCount },
            rounds);

    private static DriverStanding Driver(StandingsSnapshot snapshot, string driverId) =>
        snapshot.Drivers.Single(d => d.DriverId == driverId);

    private static ConstructorStanding Constructor(StandingsSnapshot snapshot, string constructorId) =>
        snapshot.Constructors!.Single(c => c.ConstructorId == constructorId);

    private static PointsSystem ModernSystem() => new()
    {
        RacePoints = Table(25, 18, 15, 12, 10, 8, 6, 4, 2, 1),
        FastestLap = new FastestLapRule
        {
            Points = Rational.One,
            SplitOnTie = false,
            Eligibility = FastestLapEligibility.ClassifiedTopTen,
            CountsForConstructors = true,
        },
        Constructors = new ConstructorsRule { BestCarOnly = false },
    };

    // ---------- fastest lap: 1950s seven-way tie ----------

    [Fact]
    public void FastestLap_SevenWayTie_SplitsThePointIntoExactSevenths()
    {
        // The 1954 British GP case: seven drivers share the fastest lap, 1/7 point each.
        var system = new PointsSystem
        {
            RacePoints = Table(8, 6, 4, 3, 2),
            FastestLap = new FastestLapRule
            {
                Points = Rational.One,
                SplitOnTie = true,
                Eligibility = FastestLapEligibility.Any,
            },
            SharedDrivePolicy = SharedDrivePolicy.Split,
        };

        string[] holders = ["gonzalez", "hawthorn", "marimon", "fangio", "moss", "behra", "ascari"];
        var entries = holders.Select((driver, index) => Entry(driver, index + 1)).ToArray();

        var final = Compute(system, 1, Race(1, entries, fastestLap: holders)).Final;

        Assert.Equal(new Rational(57, 7), Driver(final, "gonzalez").CountedPoints); // 8 + 1/7
        Assert.Equal(new Rational(43, 7), Driver(final, "hawthorn").CountedPoints); // 6 + 1/7
        Assert.Equal(new Rational(15, 7), Driver(final, "moss").CountedPoints);     // 2 + 1/7
        Assert.Equal(new Rational(1, 7), Driver(final, "behra").CountedPoints);     // P6: share only
        Assert.Equal(new Rational(1, 7), Driver(final, "ascari").CountedPoints);    // P7: share only

        // Exactly one fastest-lap point was distributed on top of the 8-6-4-3-2 race points.
        var total = final.Drivers.Aggregate(Rational.Zero, (acc, d) => acc + d.CountedPoints);
        Assert.Equal(new Rational(24), total);
    }

    // ---------- fastest lap: classifiedTopTen eligibility ----------

    [Fact]
    public void FastestLap_TopTenRule_HolderFinishingEleventh_AwardsNoPointToAnyone()
    {
        var entries = Enumerable.Range(1, 11).Select(i => Entry($"d{i}", i, $"team{i}")).ToArray();

        var final = Compute(ModernSystem(), 1, Race(1, entries, fastestLap: ["d11"])).Final;

        Assert.Equal(new Rational(25), Driver(final, "d1").CountedPoints);
        Assert.Equal(Rational.Zero, Driver(final, "d11").CountedPoints);
        Assert.Equal(Rational.Zero, Constructor(final, "team11").CountedPoints);

        // 25+18+15+12+10+8+6+4+2+1 = 101: the fastest-lap point went unawarded.
        var total = final.Drivers.Aggregate(Rational.Zero, (acc, d) => acc + d.CountedPoints);
        Assert.Equal(new Rational(101), total);
    }

    [Fact]
    public void FastestLap_TopTenRule_RetiredHolder_AwardsNoPointToAnyone()
    {
        var entries = new[]
        {
            Entry("d1", 1, "team1"),
            Entry("d2", 2, "team2"),
            Entry("ret", null, "team-ret", FinishStatus.Retired),
        };

        var final = Compute(ModernSystem(), 1, Race(1, entries, fastestLap: ["ret"])).Final;

        Assert.Equal(new Rational(25), Driver(final, "d1").CountedPoints);
        Assert.Equal(Rational.Zero, Driver(final, "ret").CountedPoints);
        Assert.Equal(Rational.Zero, Constructor(final, "team-ret").CountedPoints);

        var total = final.Drivers.Aggregate(Rational.Zero, (acc, d) => acc + d.CountedPoints);
        Assert.Equal(new Rational(43), total); // 25 + 18, nothing else
    }

    [Fact]
    public void FastestLap_TopTenRule_EligibleHolder_ScoresForDriverAndConstructor()
    {
        var entries = Enumerable.Range(1, 10).Select(i => Entry($"d{i}", i, $"team{i}")).ToArray();

        var final = Compute(ModernSystem(), 1, Race(1, entries, fastestLap: ["d5"])).Final;

        Assert.Equal(new Rational(11), Driver(final, "d5").CountedPoints);       // 10 + 1
        Assert.Equal(new Rational(11), Constructor(final, "team5").CountedPoints);
    }

    // ---------- shared drives ----------

    [Fact]
    public void SharedDrive_SplitPolicy_SplitsTheCarScoreEqually()
    {
        var system = new PointsSystem
        {
            RacePoints = Table(8, 6, 4, 3, 2),
            SharedDrivePolicy = SharedDrivePolicy.Split,
        };

        var final = Compute(system, 1, Race(1,
        [
            Entry("winner", 1),
            Entry("share-a", 2, shared: true),
            Entry("share-b", 2, shared: true),
            Entry("third", 3),
        ])).Final;

        Assert.Equal(new Rational(8), Driver(final, "winner").CountedPoints);
        Assert.Equal(new Rational(3), Driver(final, "share-a").CountedPoints); // 6 / 2
        Assert.Equal(new Rational(3), Driver(final, "share-b").CountedPoints);
        Assert.Equal(new Rational(4), Driver(final, "third").CountedPoints);   // unaffected
    }

    [Fact]
    public void SharedDrive_ZeroPolicy_SharedDriversScoreNothing()
    {
        var system = new PointsSystem
        {
            RacePoints = Table(8, 6, 4, 3, 2),
            SharedDrivePolicy = SharedDrivePolicy.Zero,
        };

        var final = Compute(system, 1, Race(1,
        [
            Entry("winner", 1),
            Entry("share-a", 2, shared: true),
            Entry("share-b", 2, shared: true),
            Entry("third", 3),
        ])).Final;

        Assert.Equal(Rational.Zero, Driver(final, "share-a").CountedPoints);
        Assert.Equal(Rational.Zero, Driver(final, "share-b").CountedPoints);
        Assert.Equal(new Rational(4), Driver(final, "third").CountedPoints); // P3 still scores 4
    }

    // ---------- constructors ----------

    [Fact]
    public void Constructors_BestCarOnly_ScoresOnlyTheBestPlacedCarPerRound()
    {
        var system = new PointsSystem
        {
            RacePoints = Table(8, 6, 4, 3, 2),
            Constructors = new ConstructorsRule { BestCarOnly = true },
        };

        var final = Compute(system, 1, Race(1,
        [
            Entry("a1", 1, "alpha"),
            Entry("a2", 2, "alpha"),
            Entry("b1", 3, "beta"),
        ])).Final;

        Assert.Equal(new Rational(8), Constructor(final, "alpha").CountedPoints); // not 8 + 6
        Assert.Equal(new Rational(4), Constructor(final, "beta").CountedPoints);
        Assert.Equal(1, Constructor(final, "alpha").Position);
        Assert.Equal(2, Constructor(final, "beta").Position);
    }

    [Fact]
    public void Constructors_SumMode_ScoresEveryCar()
    {
        var system = new PointsSystem
        {
            RacePoints = Table(8, 6, 4, 3, 2),
            Constructors = new ConstructorsRule { BestCarOnly = false },
        };

        var final = Compute(system, 1, Race(1,
        [
            Entry("a1", 1, "alpha"),
            Entry("a2", 2, "alpha"),
            Entry("b1", 3, "beta"),
        ])).Final;

        Assert.Equal(new Rational(14), Constructor(final, "alpha").CountedPoints); // 8 + 6
        Assert.Equal(new Rational(4), Constructor(final, "beta").CountedPoints);
    }

    [Fact]
    public void Constructors_SharedCarUnderSplitPolicy_ReceivesTheFullCarScore()
    {
        var system = new PointsSystem
        {
            RacePoints = Table(8, 6, 4, 3, 2),
            SharedDrivePolicy = SharedDrivePolicy.Split,
            Constructors = new ConstructorsRule { BestCarOnly = true },
        };

        var final = Compute(system, 1, Race(1,
        [
            Entry("d1", 1, "alpha", shared: true),
            Entry("d2", 1, "alpha", shared: true),
            Entry("b1", 2, "beta"),
        ])).Final;

        Assert.Equal(new Rational(4), Driver(final, "d1").CountedPoints); // 8 split two ways
        Assert.Equal(new Rational(4), Driver(final, "d2").CountedPoints);
        Assert.Equal(new Rational(8), Constructor(final, "alpha").CountedPoints); // full car score
        Assert.Equal(new Rational(6), Constructor(final, "beta").CountedPoints);
    }

    // ---------- best-N dropped scores ----------

    [Fact]
    public void BestN_WholeSeason_KeepsBestFourOfSix_AndListsTheTwoDroppedRounds()
    {
        var system = new PointsSystem
        {
            RacePoints = Table(9, 6, 4, 3, 2, 1),
            DriversBestN = new BestNRule
            {
                Segments = [new BestNSegment { FromRound = 1, ToRound = 6, Count = 4 }],
            },
        };

        // Finishes P1..P6 across rounds 1..6: round scores 9, 6, 4, 3, 2, 1.
        var rounds = Enumerable.Range(1, 6).Select(r => Race(r, [Entry("solo", r)])).ToArray();

        var result = Compute(system, 6, rounds);

        Assert.Equal(6, result.Snapshots.Count);
        Assert.Equal(new Rational(9), Driver(result.Snapshots[0], "solo").CountedPoints);

        var standing = Driver(result.Final, "solo");
        Assert.Equal(new Rational(25), standing.GrossPoints);
        Assert.Equal(new Rational(22), standing.CountedPoints); // 9 + 6 + 4 + 3

        // Exactly the two lowest-scoring rounds are dropped, in round order.
        Assert.Equal(2, standing.Dropped.Count);
        Assert.Equal(5, standing.Dropped[0].Round);
        Assert.Equal(new Rational(2), standing.Dropped[0].PointsDropped);
        Assert.Equal(6, standing.Dropped[1].Round);
        Assert.Equal(new Rational(1), standing.Dropped[1].PointsDropped);
    }

    [Fact]
    public void BestN_ZeroPointRounds_AreNeverListedAsDropped()
    {
        var system = new PointsSystem
        {
            RacePoints = Table(9, 6, 4, 3, 2, 1),
            DriversBestN = new BestNRule
            {
                Segments = [new BestNSegment { FromRound = 1, ToRound = 6, Count = 4 }],
            },
        };

        var rounds = new[]
        {
            Race(1, [Entry("solo", 1)]),                                    // 9
            Race(2, [Entry("solo", 2)]),                                    // 6
            Race(3, [Entry("solo", 3)]),                                    // 4
            Race(4, [Entry("solo", 4)]),                                    // 3
            Race(5, [Entry("solo", null, status: FinishStatus.Retired)]),   // 0
            Race(6, [Entry("solo", 6)]),                                    // 1
        };

        var standing = Driver(Compute(system, 6, rounds).Final, "solo");

        Assert.Equal(new Rational(23), standing.GrossPoints);
        Assert.Equal(new Rational(22), standing.CountedPoints);
        Assert.Equal(6, standing.RoundScores.Count);
        Assert.Equal(Rational.Zero, standing.RoundScores.Single(s => s.Round == 5).Points);

        // Round 5 (zero points) is dropped silently; only the 1-point round 6 is listed.
        var dropped = Assert.Single(standing.Dropped);
        Assert.Equal(6, dropped.Round);
        Assert.Equal(new Rational(1), dropped.PointsDropped);
    }

    [Fact]
    public void BestN_SplitSeason_1967Shape_AppliesSegmentsIndependently()
    {
        // 1967: best 5 of rounds 1-6 plus best 4 of rounds 7-11.
        var system = new PointsSystem
        {
            RacePoints = Table(9, 6, 4, 3, 2, 1),
            DriversBestN = new BestNRule
            {
                Segments =
                [
                    new BestNSegment { FromRound = 1, ToRound = 6, Count = 5 },
                    new BestNSegment { FromRound = 7, ToRound = 11, Count = 4 },
                ],
            },
        };

        // Rounds 1-6 score 9,6,4,3,2,1 (25); rounds 7-11 score 9,6,4,3,2 (24).
        var rounds = Enumerable.Range(1, 11)
            .Select(r => Race(r, [Entry("solo", r <= 6 ? r : r - 6)]))
            .ToArray();

        var standing = Driver(Compute(system, 11, rounds).Final, "solo");

        Assert.Equal(new Rational(49), standing.GrossPoints);
        Assert.Equal(new Rational(46), standing.CountedPoints); // (25-1) + (24-2)

        Assert.Equal(2, standing.Dropped.Count);
        Assert.Equal(6, standing.Dropped[0].Round);                          // worst of segment 1
        Assert.Equal(new Rational(1), standing.Dropped[0].PointsDropped);
        Assert.Equal(11, standing.Dropped[1].Round);                         // worst of segment 2
        Assert.Equal(new Rational(2), standing.Dropped[1].PointsDropped);
    }

    // ---------- points factor ----------

    [Fact]
    public void PointsFactor_Half_ScalesRaceAndFastestLapPoints()
    {
        var system = new PointsSystem
        {
            RacePoints = Table(10, 6, 4, 3, 2, 1),
            FastestLap = new FastestLapRule
            {
                Points = Rational.One,
                SplitOnTie = true,
                Eligibility = FastestLapEligibility.Any,
            },
        };

        var final = Compute(system, 1,
            Race(1, [Entry("a", 1), Entry("b", 2)], fastestLap: ["a"], pointsFactor: Rational.Half)).Final;

        Assert.Equal(new Rational(11, 2), Driver(final, "a").CountedPoints); // (10 + 1) / 2
        Assert.Equal(new Rational(3), Driver(final, "b").CountedPoints);     // 6 / 2
    }

    [Fact]
    public void PointsFactor_Double_ScalesRaceAndFastestLapPoints()
    {
        var system = new PointsSystem
        {
            RacePoints = Table(25, 18, 15, 12, 10, 8, 6, 4, 2, 1),
            FastestLap = new FastestLapRule
            {
                Points = Rational.One,
                SplitOnTie = true,
                Eligibility = FastestLapEligibility.Any,
            },
        };

        var final = Compute(system, 1,
            Race(1, [Entry("a", 1), Entry("b", 2)], fastestLap: ["b"], pointsFactor: new Rational(2))).Final;

        Assert.Equal(new Rational(50), Driver(final, "a").CountedPoints); // 25 * 2
        Assert.Equal(new Rational(38), Driver(final, "b").CountedPoints); // (18 + 1) * 2
    }

    [Fact]
    public void PointsFactor_Half_ScalesTheRaceSessionOnly_SprintPointsStayFull()
    {
        // Half points shorten the race classification; sprint points were always awarded
        // in full. Factor 1/2 must scale race position points and the race FL point, and
        // must not touch the sprint.
        var system = new PointsSystem
        {
            RacePoints = Table(25, 18, 15, 12, 10, 8, 6, 4, 2, 1),
            SprintPoints = Table(8, 7, 6, 5, 4, 3, 2, 1),
            FastestLap = new FastestLapRule
            {
                Points = Rational.One,
                SplitOnTie = false,
                Eligibility = FastestLapEligibility.Any,
            },
        };

        var round = new RoundResult
        {
            Round = 1,
            PointsFactor = Rational.Half,
            Sessions =
            [
                new SessionResult
                {
                    Kind = SessionKind.Sprint,
                    Entries = [Entry("max", 1), Entry("lewis", 2)],
                },
                new SessionResult
                {
                    Kind = SessionKind.Race,
                    Entries = [Entry("max", 1), Entry("lewis", 2)],
                    FastestLapDriverIds = ["max"],
                },
            ],
        };

        var final = Compute(system, 1, round).Final;

        // (25 + 1) / 2 = 13 for the race, plus the full sprint 8, 21, not (26 + 8) / 2 = 17.
        Assert.Equal(new Rational(21), Driver(final, "max").CountedPoints);
        Assert.Equal(new Rational(16), Driver(final, "lewis").CountedPoints); // 18/2 + 7
    }

    // ---------- sprints ----------

    [Fact]
    public void Sprint_ScoresFromTheSprintTable_AndRaceFromTheRaceTable()
    {
        var system = new PointsSystem
        {
            RacePoints = Table(25, 18, 15, 12, 10, 8, 6, 4, 2, 1),
            SprintPoints = Table(8, 7, 6, 5, 4, 3, 2, 1),
            FastestLap = new FastestLapRule
            {
                Points = Rational.One,
                SplitOnTie = false,
                Eligibility = FastestLapEligibility.Any,
                CountsForConstructors = true,
            },
            Constructors = new ConstructorsRule { BestCarOnly = false },
        };

        var round = new RoundResult
        {
            Round = 1,
            Sessions =
            [
                new SessionResult
                {
                    Kind = SessionKind.Sprint,
                    Entries = [Entry("max", 1, "red-bull"), Entry("lewis", 3, "mercedes")],
                    FastestLapDriverIds = ["lewis"], // sprint fastest laps never score
                },
                new SessionResult
                {
                    Kind = SessionKind.Race,
                    Entries = [Entry("max", 1, "red-bull"), Entry("lewis", 2, "mercedes")],
                },
            ],
        };

        var final = Compute(system, 1, round).Final;

        Assert.Equal(new Rational(33), Driver(final, "max").CountedPoints);   // 25 race + 8 sprint
        Assert.Equal(new Rational(24), Driver(final, "lewis").CountedPoints); // 18 race + 6 sprint, no FL
        Assert.Equal(new Rational(33), Constructor(final, "red-bull").CountedPoints);
        Assert.Equal(new Rational(24), Constructor(final, "mercedes").CountedPoints);
    }

    [Fact]
    public void Sprint_WithoutASprintTable_ThrowsOnValidation()
    {
        var system = new PointsSystem { RacePoints = Table(25, 18, 15) }; // SprintPoints null

        var round = new RoundResult
        {
            Round = 1,
            Sessions =
            [
                new SessionResult { Kind = SessionKind.Sprint, Entries = [Entry("max", 1)] },
                new SessionResult { Kind = SessionKind.Race, Entries = [Entry("max", 1)] },
            ],
        };

        var exception = Assert.Throws<ArgumentException>(() => Compute(system, 1, round));
        Assert.Contains("sprint", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- alternate race tables ----------

    [Fact]
    public void AlternateRaceTable_SelectsTheNamedTableForTheRound()
    {
        var system = new PointsSystem
        {
            RacePoints = Table(25, 18, 15, 12, 10, 8, 6, 4, 2, 1),
            AlternateRaceTables = new Dictionary<string, IReadOnlyList<Rational>>
            {
                ["reduced-2laps-25pct"] = Table(6, 4, 3, 2, 1),
            },
        };

        var final = Compute(system, 1,
            Race(1, [Entry("a", 1), Entry("b", 2)], alternateTableId: "reduced-2laps-25pct")).Final;

        Assert.Equal(new Rational(6), Driver(final, "a").CountedPoints);
        Assert.Equal(new Rational(4), Driver(final, "b").CountedPoints);
    }

    // ---------- per-session points tables (Increment 2c) ----------

    [Fact]
    public void PerSessionPointsTable_ScoresEachRaceOnItsOwnTable()
    {
        // An authored two-race weekend: the feature on the primary table, the second race on a
        // named alternate table. Each race scores on its OWN table (summed into the round, until
        // the per-session RoundScore split lands).
        var system = new PointsSystem
        {
            RacePoints = Table(25, 18, 15, 12, 10, 8, 6, 4, 2, 1),
            AlternateRaceTables = new Dictionary<string, IReadOnlyList<Rational>>
            {
                ["sprint-8"] = Table(8, 7, 6, 5, 4, 3, 2, 1),
            },
            Constructors = new ConstructorsRule { BestCarOnly = false },
        };

        var round = new RoundResult
        {
            Round = 1,
            Sessions =
            [
                new SessionResult
                {
                    Kind = SessionKind.Race,
                    PointsTableId = "primary",
                    Entries = [Entry("max", 1, "red-bull"), Entry("lewis", 2, "mercedes")],
                },
                new SessionResult
                {
                    Kind = SessionKind.Race,
                    PointsTableId = "sprint-8",
                    Entries = [Entry("lewis", 1, "mercedes"), Entry("max", 2, "red-bull")],
                },
            ],
        };

        var final = Compute(system, 1, round).Final;

        Assert.Equal(new Rational(32), Driver(final, "max").CountedPoints);   // 25 (P1 primary) + 7 (P2 sprint-8)
        Assert.Equal(new Rational(26), Driver(final, "lewis").CountedPoints); // 18 (P2 primary) + 8 (P1 sprint-8)
        Assert.Equal(new Rational(32), Constructor(final, "red-bull").CountedPoints);
        Assert.Equal(new Rational(26), Constructor(final, "mercedes").CountedPoints);
    }

    [Fact]
    public void PerSessionPointsTable_PrimaryKeyword_MatchesTheStandardRaceTable()
    {
        // "primary" on the only race must score identically to leaving the table unset, the
        // byte-identical guarantee for a single-race weekend that names its table explicitly.
        var system = ModernSystem();
        var entries = new[] { Entry("a", 1, "ta"), Entry("b", 2, "tb") };

        var withKeyword = Compute(system, 1, new RoundResult
        {
            Round = 1,
            Sessions = [new SessionResult { Kind = SessionKind.Race, PointsTableId = "primary", Entries = entries }],
        }).Final;
        var unset = Compute(system, 1, Race(1, entries)).Final;

        Assert.Equal(Driver(unset, "a").CountedPoints, Driver(withKeyword, "a").CountedPoints);
        Assert.Equal(new Rational(25), Driver(withKeyword, "a").CountedPoints);
    }

    [Fact]
    public void PerSessionPointsTable_UnknownId_Throws()
    {
        var system = new PointsSystem { RacePoints = Table(25, 18, 15) };

        var round = new RoundResult
        {
            Round = 1,
            Sessions =
            [
                new SessionResult { Kind = SessionKind.Race, PointsTableId = "no-such-session-table", Entries = [Entry("a", 1)] },
            ],
        };

        var exception = Assert.Throws<ArgumentException>(() => Compute(system, 1, round));
        Assert.Contains("no-such-session-table", exception.Message);
    }

    // ---------- per-session RoundScore split (Increment 2c.2) ----------

    [Fact]
    public void PerSessionScoring_EmitsOneRoundScorePerRace_SubKeyedBySessionIndex()
    {
        var system = new PointsSystem
        {
            RacePoints = Table(25, 18, 15, 12, 10, 8, 6, 4, 2, 1),
            AlternateRaceTables = new Dictionary<string, IReadOnlyList<Rational>>
            {
                ["sprint-8"] = Table(8, 7, 6, 5, 4, 3, 2, 1),
            },
        };

        var round = new RoundResult
        {
            Round = 1,
            PerSessionScoring = true,
            Sessions =
            [
                new SessionResult { Kind = SessionKind.Race, PointsTableId = "primary", Entries = [Entry("max", 1), Entry("lewis", 2)] },
                new SessionResult { Kind = SessionKind.Race, PointsTableId = "sprint-8", Entries = [Entry("lewis", 1), Entry("max", 2)] },
            ],
        };

        var max = Driver(Compute(system, 1, round).Final, "max");
        Assert.Equal(2, max.RoundScores.Count); // one score per race, not a merged round score
        Assert.Equal(new Rational(25), max.RoundScores.Single(s => s.SessionIndex == 0).Points);
        Assert.Equal(new Rational(7), max.RoundScores.Single(s => s.SessionIndex == 1).Points);
        Assert.Equal(new Rational(32), max.CountedPoints); // no best-N → both races count (25 + 7)
    }

    [Fact]
    public void PerSessionScoring_BestN_KeepsAndDropsTheTwoRacesIndependently()
    {
        // Best 1 of the season: scored per session, a two-race weekend presents best-N with two
        // separate round-1 scores; it keeps the stronger race and drops the weaker one.
        var system = new PointsSystem
        {
            RacePoints = Table(25, 18, 15),
            AlternateRaceTables = new Dictionary<string, IReadOnlyList<Rational>> { ["half"] = Table(12, 9, 7) },
            DriversBestN = new BestNRule { Segments = [new BestNSegment { FromRound = 1, ToRound = 1, Count = 1 }] },
        };

        var round = new RoundResult
        {
            Round = 1,
            PerSessionScoring = true,
            Sessions =
            [
                new SessionResult { Kind = SessionKind.Race, PointsTableId = "primary", Entries = [Entry("a", 1)] }, // 25
                new SessionResult { Kind = SessionKind.Race, PointsTableId = "half", Entries = [Entry("a", 1)] },    // 12
            ],
        };

        var a = Driver(Compute(system, 1, round).Final, "a");
        Assert.Equal(new Rational(37), a.GrossPoints);   // 25 + 12
        Assert.Equal(new Rational(25), a.CountedPoints);  // best 1 of the two races
        var dropped = Assert.Single(a.Dropped);
        Assert.Equal(1, dropped.Round);
        Assert.Equal(new Rational(12), dropped.PointsDropped);
    }

    [Fact]
    public void WithoutPerSessionScoring_ASprintPlusRaceStillMergesToOneRoundScore()
    {
        // The shipped shape (PerSessionScoring defaults false): sprint + race merge into ONE
        // round score with a null SessionIndex, the byte-identical guarantee the oracle enforces.
        var system = new PointsSystem
        {
            RacePoints = Table(25, 18, 15),
            SprintPoints = Table(8, 7, 6),
        };

        var round = new RoundResult
        {
            Round = 1,
            Sessions =
            [
                new SessionResult { Kind = SessionKind.Sprint, Entries = [Entry("max", 1)] },
                new SessionResult { Kind = SessionKind.Race, Entries = [Entry("max", 1)] },
            ],
        };

        var score = Assert.Single(Driver(Compute(system, 1, round).Final, "max").RoundScores);
        Assert.Null(score.SessionIndex);
        Assert.Equal(new Rational(33), score.Points); // 8 sprint + 25 race, merged
    }

    [Fact]
    public void AlternateRaceTable_UnknownId_Throws()
    {
        var system = new PointsSystem
        {
            RacePoints = Table(25, 18, 15, 12, 10, 8, 6, 4, 2, 1),
            AlternateRaceTables = new Dictionary<string, IReadOnlyList<Rational>>
            {
                ["reduced-2laps-25pct"] = Table(6, 4, 3, 2, 1),
            },
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            Compute(system, 1, Race(1, [Entry("a", 1)], alternateTableId: "no-such-table")));
        Assert.Contains("no-such-table", exception.Message);
    }

    // ---------- countback tiebreaks ----------

    [Fact]
    public void Countback_EqualPoints_MoreWinsRanksAhead()
    {
        var system = new PointsSystem { RacePoints = Table(9, 6, 4, 3, 2, 1) };

        var rounds = new[]
        {
            Race(1, [Entry("winner-once", 1), Entry("always-second", 2)]),
            Race(2, [Entry("winner-once", 4), Entry("always-second", 2)]),
        };

        var final = Compute(system, 2, rounds).Final;

        // Both on 12 points (9+3 vs 6+6); one win beats none.
        Assert.Equal(new Rational(12), Driver(final, "winner-once").CountedPoints);
        Assert.Equal(new Rational(12), Driver(final, "always-second").CountedPoints);
        Assert.Equal(1, Driver(final, "winner-once").Position);
        Assert.Equal(2, Driver(final, "always-second").Position);
    }

    [Fact]
    public void Countback_EqualPointsAndWins_MoreSecondsRanksAhead()
    {
        var system = new PointsSystem { RacePoints = Table(9, 6, 4, 3, 2, 1) };

        var rounds = new[]
        {
            Race(1, [Entry("two-seconds", 2), Entry("one-second", 4)]),
            Race(2, [Entry("two-seconds", 2), Entry("one-second", 4)]),
            Race(3, [Entry("two-seconds", 7), Entry("one-second", 2)]),
        };

        var final = Compute(system, 3, rounds).Final;

        // Both on 12 points (6+6+0 vs 3+3+6), no wins; two seconds beat one.
        Assert.Equal(new Rational(12), Driver(final, "two-seconds").CountedPoints);
        Assert.Equal(new Rational(12), Driver(final, "one-second").CountedPoints);
        Assert.Equal(1, Driver(final, "two-seconds").Position);
        Assert.Equal(2, Driver(final, "one-second").Position);
    }

    [Fact]
    public void Countback_TrueDeadHeat_SharesThePosition()
    {
        var system = new PointsSystem { RacePoints = Table(9, 6, 4, 3, 2, 1) };

        var rounds = new[]
        {
            Race(1, [Entry("leader", 1), Entry("tied-a", 2), Entry("tied-b", 3), Entry("fourth", 4)]),
            Race(2, [Entry("leader", 1), Entry("tied-a", 3), Entry("tied-b", 2), Entry("fourth", 4)]),
        };

        var final = Compute(system, 2, rounds).Final;

        // tied-a and tied-b: 10 points each, one P2 and one P3 each -> identical countback.
        Assert.Equal(1, Driver(final, "leader").Position);
        Assert.Equal(2, Driver(final, "tied-a").Position);
        Assert.Equal(2, Driver(final, "tied-b").Position);
        Assert.Equal(4, Driver(final, "fourth").Position); // standard competition ranking: 1, 2, 2, 4
    }

    // ---------- excluded drivers (1997 rule) ----------

    [Fact]
    public void ExcludedDriver_AccumulatesPointsButHasNoPosition_AndDisplacesNobody()
    {
        var definition = new SeasonScoringDefinition
        {
            PointsSystem = new PointsSystem { RacePoints = Table(10, 6, 4, 3, 2, 1) },
            RoundCount = 2,
            ExcludedDrivers = new HashSet<string>(StringComparer.Ordinal) { "michael-schumacher" },
        };

        var rounds = new[]
        {
            Race(1, [Entry("michael-schumacher", 1), Entry("jacques-villeneuve", 2), Entry("heinz-harald-frentzen", 3)]),
            Race(2, [Entry("michael-schumacher", 1), Entry("jacques-villeneuve", 2), Entry("heinz-harald-frentzen", 3)]),
        };

        var final = StandingsEngine.ComputeSeason(definition, rounds).Final;

        var schumacher = Driver(final, "michael-schumacher");
        Assert.True(schumacher.Excluded);
        Assert.Null(schumacher.Position);
        Assert.Equal(new Rational(20), schumacher.GrossPoints);
        Assert.Equal(new Rational(20), schumacher.CountedPoints);

        // Nobody shifts down: Villeneuve is champion despite scoring fewer points.
        Assert.Equal(1, Driver(final, "jacques-villeneuve").Position);
        Assert.Equal(2, Driver(final, "heinz-harald-frentzen").Position);
        Assert.Equal(3, final.Drivers.Count);
    }

    // ---------- points eligibility (1958/1984 annulled results) ----------

    [Fact]
    public void PointsEligibleFalse_ClassifiedEntryScoresNothing_AndNobodyIsPromoted()
    {
        // The annul-only shape (F2 cars at the 1958 German GP, the unregistered 1984 Monza
        // second car): the classification stands, the points are simply withheld, drivers
        // below keep their natural points, no redistribution.
        var system = new PointsSystem { RacePoints = Table(9, 6, 4, 3, 2, 1) };

        var final = Compute(system, 1, Race(1,
        [
            Entry("winner", 1),
            Entry("fourth", 4),
            Entry("ineligible", 5, pointsEligible: false),
            Entry("sixth", 6),
            Entry("retired", null, status: FinishStatus.Retired),
        ])).Final;

        var ineligible = Driver(final, "ineligible");
        Assert.Equal(Rational.Zero, ineligible.GrossPoints);
        Assert.Equal(Rational.Zero, ineligible.CountedPoints);

        Assert.Equal(new Rational(9), Driver(final, "winner").CountedPoints);
        Assert.Equal(new Rational(3), Driver(final, "fourth").CountedPoints);
        Assert.Equal(new Rational(1), Driver(final, "sixth").CountedPoints); // natural P6 points, not P5's 2

        // The ineligible entry still appears in the standings and its classified P5 finish
        // still counts for countback: it out-ranks the pointless retiree.
        Assert.Equal(1, Driver(final, "winner").Position);
        Assert.Equal(2, Driver(final, "fourth").Position);
        Assert.Equal(3, Driver(final, "sixth").Position);
        Assert.Equal(4, ineligible.Position);
        Assert.Equal(5, Driver(final, "retired").Position);
    }

    // ---------- points position (1967 German GP redistribution) ----------

    [Fact]
    public void PointsPosition_RedirectsTheTableLookup_ButRawPositionFeedsCountback()
    {
        // 1967 German GP shape: Bonnier classified 6th but paid as 5th (his rank among
        // eligible cars). The points come from the redirected position; the countback
        // keeps the raw classification.
        var system = new PointsSystem { RacePoints = Table(9, 6, 4, 3, 2, 1) };

        var final = Compute(system, 1, Race(1,
        [
            Entry("winner", 1),
            Entry("rival", 5),
            Entry("bonnier", 6, pointsPosition: 5),
        ])).Final;

        // Both P5-money finishers score the 5th-place 2 points.
        Assert.Equal(new Rational(2), Driver(final, "bonnier").CountedPoints);
        Assert.Equal(new Rational(2), Driver(final, "rival").CountedPoints);

        // Equal points, so countback decides: rival's raw P5 beats bonnier's raw P6.
        // Were the redirected position fed into countback, they would dead-heat.
        Assert.Equal(1, Driver(final, "winner").Position);
        Assert.Equal(2, Driver(final, "rival").Position);
        Assert.Equal(3, Driver(final, "bonnier").Position);
    }

    // ---------- constructors race-points override (1961) ----------

    [Fact]
    public void Constructors_RacePointsOverride_1961Shape_WinPays9ToTheDriverAnd8ToTheConstructor()
    {
        // 1961: drivers moved to 9-6-4-3-2-1 while the constructors cup stayed on the old
        // win-8 scale, best car only.
        var system = new PointsSystem
        {
            RacePoints = Table(9, 6, 4, 3, 2, 1),
            Constructors = new ConstructorsRule
            {
                BestCarOnly = true,
                RacePoints = Table(8, 6, 4, 3, 2, 1),
            },
        };

        var final = Compute(system, 1, Race(1,
        [
            Entry("phil-hill", 1, "ferrari"),
            Entry("von-trips", 2, "ferrari"),
            Entry("moss", 3, "lotus-climax"),
        ])).Final;

        Assert.Equal(new Rational(9), Driver(final, "phil-hill").CountedPoints);
        Assert.Equal(new Rational(6), Driver(final, "von-trips").CountedPoints);
        Assert.Equal(new Rational(4), Driver(final, "moss").CountedPoints);

        Assert.Equal(new Rational(8), Constructor(final, "ferrari").CountedPoints); // win-8 table, best car
        Assert.Equal(new Rational(4), Constructor(final, "lotus-climax").CountedPoints);
    }

    // ---------- excluded constructors (2007 spygate) ----------

    [Fact]
    public void ExcludedConstructor_KeepsGrossPoints_LosesCountedPointsAndPosition_EveryoneBelowMovesUp()
    {
        var definition = new SeasonScoringDefinition
        {
            PointsSystem = new PointsSystem
            {
                RacePoints = Table(10, 8, 6, 5, 4, 3, 2, 1),
                Constructors = new ConstructorsRule { BestCarOnly = false },
            },
            RoundCount = 1,
            ExcludedConstructors = new HashSet<string>(StringComparer.Ordinal) { "mclaren" },
        };

        var final = StandingsEngine.ComputeSeason(definition,
        [
            Race(1,
            [
                Entry("hamilton", 1, "mclaren"),
                Entry("alonso", 2, "mclaren"),
                Entry("raikkonen", 3, "ferrari"),
                Entry("heidfeld", 4, "bmw"),
            ]),
        ]).Final;

        var mclaren = Constructor(final, "mclaren");
        Assert.True(mclaren.Excluded);
        Assert.Null(mclaren.Position);
        Assert.Equal(new Rational(18), mclaren.GrossPoints);   // 10 + 8, retained for transparency
        Assert.Equal(Rational.Zero, mclaren.CountedPoints);    // stripped, unlike excluded drivers

        // Everyone below moves up.
        Assert.Equal(1, Constructor(final, "ferrari").Position);
        Assert.Equal(2, Constructor(final, "bmw").Position);

        // Excluded drivers were not implied: the McLaren drivers keep points and positions.
        Assert.Equal(1, Driver(final, "hamilton").Position);
        Assert.Equal(new Rational(10), Driver(final, "hamilton").CountedPoints);
    }

    // ---------- points adjustments ----------

    [Fact]
    public void PointsAdjustments_ApplyToCountedPoints_AndDecideTheRanking()
    {
        var definition = new SeasonScoringDefinition
        {
            PointsSystem = new PointsSystem
            {
                RacePoints = Table(8, 6, 4, 3, 2),
                Constructors = new ConstructorsRule { BestCarOnly = false },
            },
            RoundCount = 2,
            DriverPointsAdjustments = new Dictionary<string, Rational>(StringComparer.Ordinal)
            {
                ["fangio"] = Rational.Half,
            },
            ConstructorPointsAdjustments = new Dictionary<string, Rational>(StringComparer.Ordinal)
            {
                ["benetton"] = new Rational(-10),
            },
        };

        // Mirrored finishes: without adjustments both drivers (and both constructors)
        // dead-heat on 9 points with identical countbacks.
        var final = StandingsEngine.ComputeSeason(definition,
        [
            Race(1, [Entry("fangio", 2, "benetton"), Entry("moss", 4, "williams")]),
            Race(2, [Entry("moss", 2, "williams"), Entry("fangio", 4, "benetton")]),
        ]).Final;

        var fangio = Driver(final, "fangio");
        Assert.Equal(new Rational(9), fangio.GrossPoints);
        Assert.Equal(Rational.Half, fangio.AdjustmentPoints);
        Assert.Equal(new Rational(19, 2), fangio.CountedPoints); // 9 + 1/2

        var moss = Driver(final, "moss");
        Assert.Equal(Rational.Zero, moss.AdjustmentPoints);
        Assert.Equal(new Rational(9), moss.CountedPoints);

        // The half point breaks the would-be dead heat: ranking uses the adjusted value.
        Assert.Equal(1, fangio.Position);
        Assert.Equal(2, moss.Position);

        var benetton = Constructor(final, "benetton");
        Assert.Equal(new Rational(9), benetton.GrossPoints);
        Assert.Equal(new Rational(-10), benetton.AdjustmentPoints);
        Assert.Equal(new Rational(-1), benetton.CountedPoints); // 9 - 10

        Assert.Equal(1, Constructor(final, "williams").Position);
        Assert.Equal(2, benetton.Position);
    }

    // ---------- validation ----------

    [Fact]
    public void Validation_OverlappingBestNSegments_Throws()
    {
        var system = new PointsSystem
        {
            RacePoints = Table(9, 6, 4, 3, 2, 1),
            DriversBestN = new BestNRule
            {
                Segments =
                [
                    new BestNSegment { FromRound = 1, ToRound = 4, Count = 3 },
                    new BestNSegment { FromRound = 3, ToRound = 6, Count = 2 }, // rounds 3-4 counted twice
                ],
            },
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            Compute(system, 6, Race(1, [Entry("a", 1)])));
        Assert.Contains("overlap", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validation_DuplicateClassifiedPositionWithoutSharedDriveFlag_Throws()
    {
        var system = new PointsSystem { RacePoints = Table(8, 6, 4, 3, 2) };

        var exception = Assert.Throws<ArgumentException>(() =>
            Compute(system, 1, Race(1,
            [
                Entry("winner", 1),
                Entry("dupe-a", 2),
                Entry("dupe-b", 2), // same position, nobody flagged SharedDrive
            ])));
        Assert.Contains("SharedDrive", exception.Message);
    }

    [Fact]
    public void Validation_PointsPositionOnASharedEntry_Throws()
    {
        var system = new PointsSystem
        {
            RacePoints = Table(8, 6, 4, 3, 2),
            SharedDrivePolicy = SharedDrivePolicy.Split,
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            Compute(system, 1, Race(1,
            [
                Entry("winner", 1),
                Entry("share-a", 2, shared: true),
                Entry("share-b", 2, shared: true, pointsPosition: 3),
            ])));
        Assert.Contains("PointsPosition", exception.Message);
    }

    [Fact]
    public void Validation_FastestLapHolderWithoutASessionEntry_Throws()
    {
        var system = new PointsSystem
        {
            RacePoints = Table(8, 6, 4, 3, 2),
            FastestLap = new FastestLapRule { Points = Rational.One },
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            Compute(system, 1, Race(1, [Entry("a", 1)], fastestLap: ["ghost"])));
        Assert.Contains("ghost", exception.Message);
    }

    // ---------- zero-point participants ----------

    [Fact]
    public void ZeroPointParticipants_AllAppearInTheStandings()
    {
        var system = new PointsSystem { RacePoints = Table(8, 6, 4, 3, 2) };

        var final = Compute(system, 1, Race(1,
        [
            Entry("winner", 1),
            Entry("backmarker", 6), // classified but beyond the points table
            Entry("retired", null, status: FinishStatus.Retired),
            Entry("non-starter", null, status: FinishStatus.DidNotStart),
            Entry("not-classified", null, status: FinishStatus.NotClassified),
        ])).Final;

        Assert.Equal(5, final.Drivers.Count);
        Assert.Equal(new Rational(8), Driver(final, "winner").CountedPoints);
        Assert.Equal(1, Driver(final, "winner").Position);

        // The classified P6 finish wins the countback over drivers with no finish at all.
        Assert.Equal(Rational.Zero, Driver(final, "backmarker").CountedPoints);
        Assert.Equal(2, Driver(final, "backmarker").Position);

        foreach (var driverId in new[] { "retired", "non-starter", "not-classified" })
        {
            var standing = Driver(final, driverId);
            Assert.Equal(Rational.Zero, standing.CountedPoints);
            Assert.Equal(3, standing.Position); // zero points, empty countback: dead heat
        }
    }
}
