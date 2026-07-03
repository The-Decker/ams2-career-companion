using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Data;
using Companion.Tests.Career;

namespace Companion.Tests.Data;

/// <summary>
/// SeasonEndPipeline must resolve scoring over CHAMPIONSHIP rounds only (its rounds input
/// already carries championship ordinals — the ChampionshipCalendar rule). A split-season
/// best-N pins the fix hard: split segments must sum to the resolved round count, so
/// resolving over the full mixed calendar does not just mis-score — it throws.
/// </summary>
public sealed class MixedCalendarSeasonEndTests
{
    private const ulong Seed = 24242;
    private const string Utc = "2026-07-02T00:00:00Z";

    /// <summary>Calendar: R1 champ, R2 NON-champ Gold Cup, R3 + R4 champ (ordinals 1..3).
    /// Split best-N: best 1 of ordinals 1–2 plus best 1 of ordinal 3 — sums to the THREE
    /// championship rounds, never to the four calendar rounds.</summary>
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
                PackId = "mixed-calendar-season-end-pack",
                Name = "Mixed Calendar Season End Pack",
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
                    DriversBestN = new CatalogBestN
                    {
                        Split = new CatalogSplitSeason
                        {
                            FirstRounds = 2,
                            FirstCount = 1,
                            SecondRounds = 1,
                            SecondCount = 1,
                        },
                    },
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
        PlayerDriverId = "driver.none",
        PlayerAge = 27,
    };

    private static (CareerDatabase Db, long SeasonId, SeasonPack Pack) Setup(TempDb tmp)
    {
        var pack = Pack();
        var db = CareerDatabase.Open(tmp.Path);
        CareerStore.CreateCareer(db, "Mixed Calendar Season End", Seed, "0.6.0-test", Utc);
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

    private static void FoldWholeSeason(CareerDatabase db, long seasonId, SeasonPack pack)
    {
        Fold(db, seasonId, pack, 1, "driver.a", "driver.b", "driver.c", "driver.d");
        // Gold Cup: a completely flipped result — must not touch the championship.
        Fold(db, seasonId, pack, 2, "driver.d", "driver.c", "driver.b", "driver.a");
        Fold(db, seasonId, pack, 3, "driver.b", "driver.a", "driver.d", "driver.c");
        Fold(db, seasonId, pack, 4, "driver.a", "driver.d", "driver.b", "driver.c");
    }

    [Fact]
    public void SplitBestN_ResolvedOverTheFullCalendar_WouldThrow()
    {
        // The bug shape this suite pins: 2+1 split segments cover the 3 championship rounds,
        // never the 4-round calendar.
        var pack = Pack();
        Assert.ThrowsAny<Exception>(
            () => pack.Season.PointsSystem.ResolveScoringDefinition(pack.Season.Rounds.Count));
        _ = pack.Season.PointsSystem.ResolveScoringDefinition(
            pack.Season.Rounds.Count(r => r.Championship)); // the pipeline's resolution — must not throw
    }

    [Fact]
    public void SeasonEnd_OnAMixedCalendar_ScoresChampionshipRoundsOnly()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = Setup(tmp);
        using var _ = db;

        FoldWholeSeason(db, seasonId, pack);

        // Would throw under the old full-calendar resolution (split 2+1 vs 4 rounds).
        ReplayService.RunSeasonEnd(db, seasonId, pack, Seed, Inputs(), Utc);

        // Expected: the engine over championship results only, championship round count.
        var expected = StandingsEngine.ComputeSeason(
            ChampionshipCalendar.ResolveScoring(pack),
            [
                Result(pack, 1, "driver.a", "driver.b", "driver.c", "driver.d"),
                Result(pack, 3, "driver.b", "driver.a", "driver.d", "driver.c"),
                Result(pack, 4, "driver.a", "driver.d", "driver.b", "driver.c"),
            ]).Final;

        var expectedChampion = expected.Drivers.Single(d => d.Position == 1);
        Assert.Equal("driver.a", expectedChampion.DriverId); // best-1+1: 9 + 9 = 18

        // The journaled season.championship rows must agree with the engine — champion id,
        // counted points, and the Gold Cup winner (driver.d) not promoted by his flip win.
        var championshipRows = JournalStore.ReadSeason(db, seasonId)
            .Where(r => string.Equals(r.Phase, JournalPhases.Championship, StringComparison.Ordinal) &&
                        r.Entity.StartsWith("driver.", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(championshipRows);

        var journaledByDriver = championshipRows.ToDictionary(
            r => r.Entity,
            r => JsonDocument.Parse(r.DeltaJson).RootElement,
            StringComparer.Ordinal);

        foreach (var driver in expected.Drivers)
        {
            var row = journaledByDriver[driver.DriverId];
            int? journaledPosition =
                row.TryGetProperty("position", out var p) && p.ValueKind != JsonValueKind.Null
                    ? p.GetInt32()
                    : null;
            Assert.Equal(driver.Position, journaledPosition);
            Assert.Equal(driver.CountedPoints.ToString(), row.GetProperty("points").GetString());
        }
    }

    [Fact]
    public void SeasonEnd_OnAMixedCalendar_ReplaysByteIdentical()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = Setup(tmp);
        using var _ = db;

        FoldWholeSeason(db, seasonId, pack);
        ReplayService.RunSeasonEnd(db, seasonId, pack, Seed, Inputs(), Utc);

        var report = ReplayService.Resimulate(db, pack, Seed, Inputs());

        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }
}
