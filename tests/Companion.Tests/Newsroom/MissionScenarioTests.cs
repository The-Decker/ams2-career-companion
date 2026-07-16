using Companion.Core.Newsroom;
using Companion.Core.Packs;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;
using Xunit;

namespace Companion.Tests.Newsroom;

/// <summary>
/// THE MISSION FIXTURE (newsroom overhaul acceptance): a user-created driver joins a real
/// historical season (1967 — the shipped verified record), qualifies midfield, suffers a major
/// incident alongside a championship-level opponent, finishes outside the points — and the
/// engine produces several DISTINCT but CONNECTED stories for the weekend, plus the
/// real-history comparison, deterministically. Nothing here is hardcoded into production
/// behavior; the scenario validates the engine end to end over the real machinery.
/// </summary>
public sealed class MissionScenarioTests : IDisposable
{
    private const string PlayerSeat = "Stock Livery #3"; // team.c — the user-created entrant's seat
    private const long Seed = 19670101;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-mission-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void A_new_career_opens_with_preseason_coverage_before_any_race()
    {
        using var session = NewCareer();

        var feed = session.NewsroomFeed();

        Assert.Contains(feed, a => a.EventKind == NewsEventKind.CareerCreated);
        Assert.Contains(feed, a => a.EventKind == NewsEventKind.SeasonStarted);
        Assert.All(feed, a => Assert.False(string.IsNullOrWhiteSpace(a.Headline)));
    }

    [Fact]
    public void The_incident_weekend_produces_distinct_connected_stories_and_the_history_comparison()
    {
        using var session = NewCareer();
        var playerId = session.Summary.PlayerDriverId;

        // Round 1 — qualifies midfield (P3 of 5); a major incident takes out both the player
        // and the pre-race favourite (the tier-5 team's driver); the player scores nothing.
        var grid = session.CurrentGrid().Select(s => s.DriverId).ToList();
        var others = grid.Where(id => id != playerId).ToList();
        var favourite = "driver.a"; // team.a, prestige 5 — the championship-level opponent
        var quali = new List<string> { others[1], others[2], playerId }
            .Concat(others.Where(id => id != others[1] && id != others[2]))
            .ToList();
        session.Apply(new ResultDraft
        {
            Classified = others.Where(id => id != favourite).ToList(),
            DidNotFinish = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [playerId] = "a",
                [favourite] = "a",
            },
            Disqualified = [],
            QualifyingOrder = quali,
        });

        var feed = session.NewsroomFeed();
        var weekend = feed.Where(a => a.SeasonOrdinal == 1 && a.Round == 1).ToList();

        // Distinct, connected coverage of ONE weekend: the race/incident report, the debut
        // feature, the first-retirement angle, and the alternate-history comparison.
        var incident = Assert.Single(weekend, a => a.EventKind == NewsEventKind.RetiredDriverError);
        Assert.Contains(weekend, a => a.EventKind == NewsEventKind.FirstStart);
        Assert.Contains(weekend, a => a.EventKind == NewsEventKind.FirstRetirement);
        var diverged = Assert.Single(weekend, a => a.EventKind == NewsEventKind.HistoryDiverged);

        // The comparison quotes the REAL 1967 round-one winner from the verified record —
        // read from the store, never hardcoded here.
        var real1967 = session.HistoricalSeason(1967);
        Assert.NotNull(real1967);
        var realWinner = real1967!.Rounds.First(r => r.Round == 1).Winner;
        Assert.False(string.IsNullOrEmpty(realWinner));
        Assert.Contains(realWinner!, diverged.Body);

        // Provenance separation: career coverage is labeled career-universe, and every
        // story renders clean copy with real facts.
        Assert.Equal(ContentProvenance.CareerUniverse, incident.Provenance);
        foreach (var article in weekend)
        {
            Assert.DoesNotContain("{", article.Headline + article.Body);
            Assert.DoesNotContain("[[", article.Headline + article.Body);
        }
        Assert.Equal(weekend.Count, weekend.Select(a => a.Key).Distinct(StringComparer.Ordinal).Count());

        // The championship-level opponent's incident survived into storage (envelope v9).
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var causes = ResultStore
            .ReadSeasonResults(Database(session), SeasonId(session))[0]
            .ToEnvelope().AiDnfCauses;
        Assert.NotNull(causes);
        Assert.Equal("a", causes![favourite]);

        // The editorial selector shapes the weekend into a coherent package, lead first.
        var package = session.WeekendPackage(1, 1);
        Assert.InRange(package.Count, 4, EditorialSelector.MaxWeekendStories);
        for (int i = 1; i < package.Count; i++)
        {
            Assert.True(package[i - 1].Score >= package[i].Score);
        }
    }

    [Fact]
    public void The_recovery_arc_develops_the_story_across_weekends_and_stays_deterministic()
    {
        using var session = NewCareer(threeRounds: true);
        var playerId = session.Summary.PlayerDriverId;
        var others = session.CurrentGrid().Select(s => s.DriverId).Where(id => id != playerId).ToList();

        // Round 1: the incident. Round 2 and 3: back-to-back wins while round one's winner
        // fades — by round 3 the user-created driver heads the championship.
        session.Apply(new ResultDraft
        {
            Classified = ["driver.b", "driver.d", "driver.e"],
            DidNotFinish = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [playerId] = "a",
                ["driver.a"] = "a",
            },
            Disqualified = [],
        });
        session.Apply(new ResultDraft
        {
            Classified = [playerId, "driver.d", "driver.e", "driver.a", "driver.b"],
            DidNotFinish = new Dictionary<string, string>(StringComparer.Ordinal),
            Disqualified = [],
        });
        session.Apply(new ResultDraft
        {
            Classified = [playerId, "driver.e", "driver.a", "driver.b", "driver.d"],
            DidNotFinish = new Dictionary<string, string>(StringComparer.Ordinal),
            Disqualified = [],
        });

        var events = session.NewsroomEvents();
        Assert.Contains(events, e => e.Kind == NewsEventKind.FirstWin && e.Round == 2);
        Assert.Contains(events, e => e.Kind == NewsEventKind.WinStreak && e.Round == 3
            && e.Facts.StreakLength == 2);
        Assert.Contains(events, e => e.Kind == NewsEventKind.ChampionshipLeadTaken && e.Round == 3);
        Assert.Contains(events, e => e.Kind == NewsEventKind.HistoryDiverged && e.Round == 2);
        Assert.Contains(events, e => e.Kind == NewsEventKind.HistoryDiverged && e.Round == 3);

        // The championship story is now an ongoing THREAD the recovery arc set in motion.
        var titleThread = Assert.Single(session.StoryThreads(), t => t.Type == StoryThreadType.TitleFight);
        Assert.NotEmpty(titleThread.Entries);

        // And the whole newsroom re-renders byte-identically — nothing rewrites itself.
        var feed = session.NewsroomFeed();
        var again = session.NewsroomFeed();
        Assert.Equal(
            feed.Select(a => a.Key + "|" + a.Headline + "|" + a.Body),
            again.Select(a => a.Key + "|" + a.Headline + "|" + a.Body));

        // The season comparison surface reads both labeled sides without blending them.
        var report = session.SeasonDivergence(1);
        Assert.NotNull(report);
        Assert.Equal(3, report!.AlternateOutcomes);
        Assert.Contains(report.Rounds, r => r.NonHistoricalWinner); // the user-created driver
        Assert.False(string.IsNullOrEmpty(report.HistoricalChampion));
        Assert.NotEmpty(report.HistoricalSource); // f1db attribution rides along
    }

    // ---------- scaffolding (NewsroomEventsIntegrationTests' ladder + the real 1967 record) ----------

    private string PacksRoot => Path.Combine(_root, "packs");
    private string CareerPath => Path.Combine(_root, "career.ams2career");

    private static string FixtureHistoryDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "history");

    private CareerEnvironment Environment() => new()
    {
        ContentLibrary = FiveSeatLibrary(),
        LocateInstall = static () => null,
        DocumentsDirectory = Path.Combine(_root, "docs"),
        RulesDirectory = ViewModelTestData.RulesDirectory,
        HistoryDirectory = FixtureHistoryDirectory,
        PackSearchRoots = () => [PacksRoot],
    };

    private CareerSessionService NewCareer(bool threeRounds = false)
    {
        string packDirectory = Path.Combine(PacksRoot, threeRounds ? "historical-1967-x3" : "historical-1967");
        TestPackBuilder.Write(ScenarioPack(threeRounds), packDirectory);
        return CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = CareerPath,
            CareerName = "Mission Scenario",
            MasterSeed = Seed,
            PlayerLiveryName = PlayerSeat,
        }, Environment());
    }

    private static SeasonPack ScenarioPack(bool threeRounds = false)
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        if (threeRounds)
        {
            var extraRound = basePack.Season.Rounds[1] with { Round = 3 };
            basePack = basePack with
            {
                Season = basePack.Season with
                {
                    Rounds = [.. basePack.Season.Rounds, extraRound],
                },
            };
        }
        return basePack with
        {
            Teams =
            [
                Team("team.a", 5), Team("team.b", 4), Team("team.c", 3), Team("team.d", 2), Team("team.e", 3),
            ],
            Drivers =
            [
                TestPackBuilder.Driver("driver.a"),
                TestPackBuilder.Driver("driver.b"),
                TestPackBuilder.Driver("driver.c"),
                TestPackBuilder.Driver("driver.d"),
                TestPackBuilder.Driver("driver.e"),
            ],
            Entries = new[]
            {
                TestPackBuilder.Entry("team.a", "driver.a", "1", "Stock Livery #1"),
                TestPackBuilder.Entry("team.b", "driver.b", "2", "Stock Livery #2"),
                TestPackBuilder.Entry("team.c", "driver.c", "3", PlayerSeat),
                TestPackBuilder.Entry("team.d", "driver.d", "4", "Stock Livery #4"),
                TestPackBuilder.Entry("team.e", "driver.e", "5", "Stock Livery #5"),
            }
            .Select(entry => threeRounds ? entry with { Rounds = "1-3" } : entry)
            .ToList(),
        };
    }

    private static PackTeam Team(string id, int prestige) => new()
    {
        Id = id,
        Name = id,
        CarVehicleIds = [TestPackBuilder.VintageCar],
        Reliability = 0.93,
        Prestige = prestige,
        BudgetTier = prestige,
    };

    private static Companion.Ams2.ContentLibrary.Ams2ContentLibrary FiveSeatLibrary()
    {
        var library = TestPackBuilder.Library();
        return new()
        {
            ExtractedFrom = library.ExtractedFrom,
            Classes = library.Classes,
            Vehicles = library.Vehicles,
            Tracks = library.Tracks,
            Liveries = new Dictionary<string, Companion.Ams2.ContentLibrary.Ams2LiveryClassEntry>(StringComparer.Ordinal)
            {
                [TestPackBuilder.VintageClass] = new()
                {
                    Name = TestPackBuilder.VintageClass,
                    StockLib1563 = ["Stock Livery #1", "Stock Livery #2", PlayerSeat, "Stock Livery #4", "Stock Livery #5"],
                },
            },
        };
    }

    private static CareerDatabase Database(CareerSessionService session) =>
        (CareerDatabase)typeof(CareerSessionService)
            .GetField("_database", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(session)!;

    private static long SeasonId(CareerSessionService session) =>
        CareerStore.ReadSeasons(Database(session))[0].Id;
}
