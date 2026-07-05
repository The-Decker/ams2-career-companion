using Companion.Core.Career;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Data;
using Companion.Tests.Career;

namespace Companion.Tests.Data;

/// <summary>
/// Structural fix (integration report): the unified fold must resolve scoring with
/// CHAMPIONSHIP-only ordinals exactly like CareerSessionService — on a mixed calendar
/// (a non-championship event between championship rounds) the non-championship result is
/// recorded and folded but NEVER scored, and the journaled standings equal the engine run
/// over championship results with the championship round count.
/// </summary>
public sealed class MixedCalendarFoldTests
{
    private const ulong Seed = 4242;
    private const string Utc = "2026-07-02T00:00:00Z";

    /// <summary>Calendar: R1 championship, R2 NON-championship ("Gold Cup"), R3 + R4
    /// championship. Championship ordinals: R1→1, R3→2, R4→3.</summary>
    private static SeasonPack Pack()
    {
        PackRound Round(int round, string name, bool championship) => new()
        {
            Round = round,
            Name = name,
            Date = $"1967-0{round}-01",
            Track = new PackTrackRef { Id = "kyalami_historic" },
            Laps = 10,
            Championship = championship,
        };

        PackEntry Entry(string teamId, string driverId, string number, string livery) => new()
        {
            TeamId = teamId,
            DriverId = driverId,
            Number = number,
            Rounds = "1-4",
            Ams2LiveryName = livery,
        };

        return new SeasonPack
        {
            Manifest = new PackManifest
            {
                PackId = "mixed-calendar-pack",
                Name = "Mixed Calendar Pack",
                Version = "1.0.0",
                FormatVersion = 1,
            },
            Season = new SeasonDefinition
            {
                Year = 1967,
                SeriesName = "Mixed Series",
                Ams2Class = "F-Vintage_Gen1",
                PointsSystem = new CatalogSeason
                {
                    RacePoints = [new(9), new(6), new(4), new(3)],
                    // Best 2 of the 3 CHAMPIONSHIP rounds — resolving over the 4-round
                    // calendar would be the bug this test pins down.
                    DriversBestN = new CatalogBestN { WholeSeason = 2 },
                    Constructors = new CatalogConstructors { BestCarOnly = false },
                },
                Rounds =
                [
                    Round(1, "First Grand Prix", championship: true),
                    Round(2, "Gold Cup (non-championship)", championship: false),
                    Round(3, "Second Grand Prix", championship: true),
                    Round(4, "Third Grand Prix", championship: true),
                ],
            },
            Teams =
            [
                new PackTeam { Id = "team.red", Name = "Red", CarVehicleIds = ["formula_vintage_g1m1"] },
                new PackTeam { Id = "team.blue", Name = "Blue", CarVehicleIds = ["formula_vintage_g1m1"] },
            ],
            Drivers =
            [
                Driver("driver.a", "Alan Apex"),
                Driver("driver.b", "Bob Brakes"),
                Driver("driver.c", "Cy Curbs"),
                Driver("driver.d", "Dan Dive"),
            ],
            Entries =
            [
                Entry("team.red", "driver.a", "1", "Red #1"),
                Entry("team.red", "driver.b", "2", "Red #2"),
                Entry("team.blue", "driver.c", "3", "Blue #3"),
                Entry("team.blue", "driver.d", "4", "Blue #4"),
            ],
        };
    }

    private static PackDriver Driver(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Ratings = CareerTestData.Ratings(0.8, 0.8),
    };

    private static readonly Dictionary<string, string> TeamByDriver = new(StringComparer.Ordinal)
    {
        ["driver.a"] = "team.red",
        ["driver.b"] = "team.red",
        ["driver.c"] = "team.blue",
        ["driver.d"] = "team.blue",
    };

    /// <summary>An engine-shaped result whose Round is the CHAMPIONSHIP ordinal — exactly
    /// what the app's session service stores for a calendar round.</summary>
    private static RoundResult Result(SeasonPack pack, int calendarRound, params string[] order) => new()
    {
        Round = ChampionshipCalendar.Ordinal(pack, calendarRound),
        Sessions =
        [
            new SessionResult
            {
                Kind = SessionKind.Race,
                Entries = order.Select((driverId, i) => new ClassifiedEntry
                {
                    DriverId = driverId,
                    ConstructorId = TeamByDriver[driverId],
                    Position = i + 1,
                }).ToList(),
            },
        ],
    };

    private static ReplaySimInputs Inputs() => new()
    {
        AgingCurves = CareerTestData.LoadAgingCurves(),
        Archetypes = CareerTestData.LoadArchetypes(),
        Headlines = CareerTestData.LoadHeadlines(),
        PlayerDriverId = "driver.none", // the player never races — pure standings season
        PlayerAge = 27,
    };

    private static (CareerDatabase Db, long SeasonId, SeasonPack Pack) Setup(TempDb tmp)
    {
        var pack = Pack();
        var db = CareerDatabase.Open(tmp.Path);
        CareerStore.CreateCareer(db, "Mixed Calendar Career", Seed, "0.6.0-test", Utc);
        CareerStore.PinPack(db, pack, Utc);
        long seasonId = CareerStore.StartSeason(db, 1967, pack.Manifest.PackId, pack.Manifest.Version);
        StateStore.UpsertPlayerState(db, seasonId, StateStore.StageStart, new PlayerCareerState());
        return (db, seasonId, pack);
    }

    private static void Fold(
        CareerDatabase db, long seasonId, SeasonPack pack, int calendarRound, params string[] order)
    {
        ReplayService.ImportAndFoldRound(
            db, seasonId, pack, Seed, Inputs(), calendarRound,
            new RoundResultEnvelope { Result = Result(pack, calendarRound, order), SliderUsed = 100.0 },
            Utc);
    }

    private static IReadOnlyList<(string Entity, string DeltaJson)> StandingsRows(
        CareerDatabase db, long seasonId, int calendarRound) =>
        JournalStore.ReadSeason(db, seasonId)
            .Where(r => r.Round == calendarRound &&
                        string.Equals(r.Phase, DataJournalPhases.RoundStandings, StringComparison.Ordinal))
            .Select(r => (r.Entity, r.DeltaJson))
            .ToList();

    [Fact]
    public void NonChampionshipResult_IsJournaledButNeverScored()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = Setup(tmp);
        using var _ = db;

        Fold(db, seasonId, pack, 1, "driver.a", "driver.b", "driver.c", "driver.d");
        // The Gold Cup: a DIFFERENT winner — if this scored, the standings would flip.
        Fold(db, seasonId, pack, 2, "driver.d", "driver.c", "driver.b", "driver.a");

        var afterChampionshipRound = StandingsRows(db, seasonId, 1);
        var afterGoldCup = StandingsRows(db, seasonId, 2);

        Assert.NotEmpty(afterGoldCup); // the round still folds (standings restated)...
        Assert.Equal(afterChampionshipRound, afterGoldCup); // ...but nothing moved.
    }

    [Fact]
    public void JournaledStandings_MatchTheEngineOverChampionshipRoundsOnly()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = Setup(tmp);
        using var _ = db;

        var r1 = Result(pack, 1, "driver.a", "driver.b", "driver.c", "driver.d");
        var r3 = Result(pack, 3, "driver.b", "driver.a", "driver.d", "driver.c");
        var r4 = Result(pack, 4, "driver.c", "driver.d", "driver.a", "driver.b");
        Assert.Equal(1, r1.Round); // championship ordinals, not calendar positions
        Assert.Equal(2, r3.Round);
        Assert.Equal(3, r4.Round);

        Fold(db, seasonId, pack, 1, "driver.a", "driver.b", "driver.c", "driver.d");
        Fold(db, seasonId, pack, 2, "driver.d", "driver.c", "driver.b", "driver.a");
        Fold(db, seasonId, pack, 3, "driver.b", "driver.a", "driver.d", "driver.c");
        Fold(db, seasonId, pack, 4, "driver.c", "driver.d", "driver.a", "driver.b");

        // EXACTLY the session service's resolution: championship count (3), champ results only.
        var expected = StandingsEngine.ComputeSeason(
            ChampionshipCalendar.ResolveScoring(pack), [r1, r3, r4]).Final;

        // Same single-line CoreJson conventions the Data layer writes journal cells with.
        var cellOptions = new System.Text.Json.JsonSerializerOptions(Companion.Core.Json.CoreJson.Options)
        {
            WriteIndented = false,
        };
        string Cell(int? position, string points) => System.Text.Json.JsonSerializer.Serialize(
            new { position, points }, cellOptions);

        var journaled = StandingsRows(db, seasonId, 4);
        var expectedRows = new List<(string Entity, string DeltaJson)>();
        foreach (var driver in expected.Drivers)
            expectedRows.Add((driver.DriverId, Cell(driver.Position, driver.CountedPoints.ToString())));
        foreach (var team in expected.Constructors!)
            expectedRows.Add((team.ConstructorId, Cell(team.Position, team.CountedPoints.ToString())));

        Assert.Equal(expectedRows, journaled);

        // Best-2-of-3 really engaged: with three championship rounds, someone dropped points.
        Assert.Contains(expected.Drivers, d => d.Dropped.Count > 0);
    }

    [Fact]
    public void MixedCalendarCareer_ReplaysByteIdentical()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = Setup(tmp);
        using var _ = db;

        Fold(db, seasonId, pack, 1, "driver.a", "driver.b", "driver.c", "driver.d");
        Fold(db, seasonId, pack, 2, "driver.d", "driver.c", "driver.b", "driver.a");
        Fold(db, seasonId, pack, 3, "driver.b", "driver.a", "driver.d", "driver.c");

        var report = ReplayService.Resimulate(db, pack, Seed, Inputs());

        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }

    [Fact]
    public void SharedMapping_MatchesTheSessionServiceRule()
    {
        var pack = Pack();

        Assert.Equal(3, ChampionshipCalendar.RoundCount(pack));
        Assert.Equal(1, ChampionshipCalendar.Ordinal(pack, 1));
        Assert.Equal(1, ChampionshipCalendar.Ordinal(pack, 2)); // non-champ maps to the last champ ordinal
        Assert.Equal(2, ChampionshipCalendar.Ordinal(pack, 3));
        Assert.Equal(3, ChampionshipCalendar.Ordinal(pack, 4));
        Assert.True(ChampionshipCalendar.IsChampionshipRound(pack, 1));
        Assert.False(ChampionshipCalendar.IsChampionshipRound(pack, 2));
        Assert.False(ChampionshipCalendar.IsChampionshipRound(pack, 99));
        Assert.Equal(3, ChampionshipCalendar.ResolveScoring(pack).RoundCount);
    }
}
