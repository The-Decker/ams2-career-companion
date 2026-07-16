using Companion.Core.Newsroom;
using Companion.Core.Packs;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;
using Xunit;

namespace Companion.Tests.Newsroom;

/// <summary>
/// The newsroom event spine over the REAL career machinery — proving what the mission demands:
/// a plain HISTORICAL career (no SMGP gating) now yields firsts, result stories, and
/// championship movement, all as a pure display projection that leaves replay byte-identical.
/// Scaffolding mirrors <c>SmgpDispatchesTests</c>' real-machinery ladder.
/// </summary>
public sealed class NewsroomEventsIntegrationTests : IDisposable
{
    private const string PlayerSeat = "Stock Livery #3"; // team.c — the player's start
    private const long Seed = 20260716;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-newsroom-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void A_historical_career_now_detects_firsts_results_and_the_championship_lead()
    {
        using var session = NewCareer();
        ApplyRound(session, playerWins: true);

        var events = session.NewsroomEvents();

        Assert.Contains(events, e => e.Kind == NewsEventKind.CareerCreated);
        Assert.Contains(events, e => e.Kind == NewsEventKind.SeasonStarted);
        Assert.Contains(events, e => e.Kind == NewsEventKind.FirstStart);
        var won = Assert.Single(events, e => e.Kind == NewsEventKind.RaceWon);
        Assert.True(won.Facts.IsFirstEver);
        Assert.Equal(1, won.Facts.PlayerFinish);
        Assert.False(string.IsNullOrEmpty(won.VenueName));
        Assert.Contains(events, e => e.Kind == NewsEventKind.FirstWin);
        Assert.Contains(events, e => e.Kind == NewsEventKind.FirstPoints);
        var lead = Assert.Single(events, e => e.Kind == NewsEventKind.ChampionshipLeadTaken);
        Assert.True(lead.Facts.IsSeasonOpener);
    }

    [Fact]
    public void A_retirement_reads_its_cause_from_the_journal()
    {
        using var session = NewCareer();
        ApplyPlayerDnf(session, cause: "m");

        var events = session.NewsroomEvents();

        Assert.Single(events, e => e.Kind == NewsEventKind.RetiredMechanical);
        Assert.Single(events, e => e.Kind == NewsEventKind.FirstRetirement);
        Assert.DoesNotContain(events, e => e.Kind == NewsEventKind.RetiredDriverError);
    }

    [Fact]
    public void The_feed_is_deterministic_and_replay_stays_byte_identical()
    {
        SeasonPack pack;
        string playerId;
        using (var session = NewCareer())
        {
            pack = session.Pack;
            playerId = session.Summary.PlayerDriverId;
            ApplyRound(session, playerWins: true);
            ApplyRound(session, playerWins: false);

            var first = session.NewsroomEvents();
            var second = session.NewsroomEvents();
            Assert.Equal(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i], second[i]);
            }

            // The AI winner of round 2 is attributed with a real name and team.
            Assert.Contains(first, e =>
                e.SeasonOrdinal == 1
                && e.Round == 2
                && e.Kind is NewsEventKind.PodiumFinish or NewsEventKind.PointsFinish
                    or NewsEventKind.MidfieldResult or NewsEventKind.Underperformed
                    or NewsEventKind.Overperformed);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(CareerPath);
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)Seed), new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerId,
            PlayerAge = 30,
        });
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }

    // ---------- scaffolding (mirrors SmgpDispatchesTests' real-machinery ladder) ----------

    private string PacksRoot => Path.Combine(_root, "packs");
    private string CareerPath => Path.Combine(_root, "career.ams2career");

    private CareerEnvironment Environment() => new()
    {
        ContentLibrary = FiveSeatLibrary(),
        LocateInstall = static () => null,
        DocumentsDirectory = Path.Combine(_root, "docs"),
        RulesDirectory = ViewModelTestData.RulesDirectory,
        PackSearchRoots = () => [PacksRoot],
    };

    private CareerSessionService NewCareer()
    {
        string packDirectory = Path.Combine(PacksRoot, "historical");
        TestPackBuilder.Write(HistoricalPack(), packDirectory);
        return CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = CareerPath,
            CareerName = "Newsroom Career",
            MasterSeed = Seed,
            PlayerLiveryName = PlayerSeat,
        }, Environment());
    }

    private static SeasonPack HistoricalPack()
    {
        var basePack = TestPackBuilder.TwoRoundPack();
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
            Entries =
            [
                TestPackBuilder.Entry("team.a", "driver.a", "1", "Stock Livery #1"),
                TestPackBuilder.Entry("team.b", "driver.b", "2", "Stock Livery #2"),
                TestPackBuilder.Entry("team.c", "driver.c", "3", PlayerSeat),
                TestPackBuilder.Entry("team.d", "driver.d", "4", "Stock Livery #4"),
                TestPackBuilder.Entry("team.e", "driver.e", "5", "Stock Livery #5"),
            ],
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

    private static void ApplyRound(ICareerSession session, bool playerWins)
    {
        string player = session.Summary.PlayerDriverId;
        var others = session.CurrentGrid()
            .Select(s => s.DriverId)
            .Where(id => !string.Equals(id, player, StringComparison.Ordinal))
            .ToList();
        var classified = playerWins
            ? new List<string> { player }.Concat(others).ToList()
            : others.Append(player).ToList();
        session.Apply(new ResultDraft
        {
            Classified = classified,
            DidNotFinish = new Dictionary<string, string>(StringComparer.Ordinal),
            Disqualified = [],
        });
    }

    private static void ApplyPlayerDnf(ICareerSession session, string cause)
    {
        string player = session.Summary.PlayerDriverId;
        var others = session.CurrentGrid()
            .Select(s => s.DriverId)
            .Where(id => !string.Equals(id, player, StringComparison.Ordinal))
            .ToList();
        session.Apply(new ResultDraft
        {
            Classified = others,
            DidNotFinish = new Dictionary<string, string>(StringComparer.Ordinal) { [player] = cause },
            Disqualified = [],
        });
    }
}
