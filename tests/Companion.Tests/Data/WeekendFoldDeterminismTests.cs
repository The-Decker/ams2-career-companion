using Companion.Core.Career;
using Companion.Core.Grid;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// Increment 2 determinism gate for the qualifying weekend path (design §13.2 / the CI matrix):
/// a career whose round carries a captured qualifying order (2b.3) folds the one-lap anchor
/// (2d.2) AND replays byte-identically. Asserts (a) the <c>player.qualiAnchor</c> row is emitted
/// only for the qualified round, a single-race round emits the identical journal sequence it
/// always has, and (b) <see cref="ReplayService.Resimulate"/> reproduces the whole career,
/// qualifying anchor included, with no divergence.
/// </summary>
public sealed class WeekendFoldDeterminismTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-weekend-fold-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // SQLite WAL sidecars can outlive the connection briefly on Windows.
        }
    }

    [Fact]
    public void QualifyingOrder_FoldsTheAnchorAndReplaysByteIdentically_SingleRaceRoundStaysClean()
    {
        string packDirectory = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "qualifying.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const string playerId = "driver.hulme";
        const long seed = 20260705;
        Companion.Core.Packs.SeasonPack pack;

        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "Qualifying Fold",
                       MasterSeed = seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                   },
                   environment))
        {
            pack = session.Pack;

            // Round 1: capture a qualifying order (the player qualifies behind Brabham), then race.
            var grid1 = session.CurrentGrid();
            session.Apply(new ResultDraft
            {
                Classified = grid1.Select(s => s.DriverId).ToList(),
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
                QualifyingOrder = ["driver.brabham", playerId],
            });

            // Round 2: a plain single-race round, no qualifying order, so no qualiAnchor row.
            var grid2 = session.CurrentGrid();
            session.Apply(new ResultDraft
            {
                Classified = grid2.Select(s => s.DriverId).ToList(),
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
            });
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;

        // The qualifying order was stored verbatim in round 1's envelope, absent from round 2's.
        var stored = ResultStore.ReadSeasonResults(db, seasonId);
        Assert.Equal(new[] { "driver.brabham", playerId }, stored[0].ToEnvelope().QualifyingOrder);
        Assert.Null(stored[1].ToEnvelope().QualifyingOrder);

        // The one-lap anchor row is emitted for the qualified round ONLY, round 2's journal
        // sequence is byte-identical to a pre-weekend single-race career.
        var journal = JournalStore.ReadSeason(db, seasonId);
        Assert.Contains(journal, r => r.Round == 1 && r.Phase == JournalPhases.PlayerQualiAnchor);
        Assert.DoesNotContain(journal, r => r.Round == 2 && r.Phase == JournalPhases.PlayerQualiAnchor);

        // The whole career, qualifying anchor included, re-simulates byte-identically
        // (positional per-row compare, so this asserts row-count equality, not just values).
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerId,
            PlayerAge = 30, // TestPackBuilder drivers carry no Born year → season.Year − (Year − 30)
        };

        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), inputs);

        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.True(report.ComparedRows > 0);
    }

    [Fact]
    public void TwoRaceWeekend_FoldsEachRaceIndependently_AndReplaysByteIdentically()
    {
        string packDirectory = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRaceWeekendPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "two-race-fold.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const string playerId = "driver.hulme";
        const long seed = 314159;
        Companion.Core.Packs.SeasonPack pack;

        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "Two-Race Fold",
                       MasterSeed = seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                   },
                   environment))
        {
            pack = session.Pack;

            // Round 1: qualify, then the two-race weekend (feature + sprint).
            session.Apply(new ResultDraft
            {
                Classified = ["driver.hulme", "driver.brabham"],
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
                QualifyingOrder = ["driver.brabham", "driver.hulme"],
                AdditionalRaces =
                [
                    new ExtraRaceResult { Classified = ["driver.brabham", "driver.hulme"] },
                ],
            });
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        var journal = JournalStore.ReadSeason(db, seasonId);

        // Each race folded its own player round-update: TWO race.result + OPI + reputation rows.
        Assert.Equal(2, journal.Count(r => r.Round == 1
            && r.Phase == JournalPhases.RaceResult && r.Entity == "player"));
        Assert.Equal(2, journal.Count(r => r.Round == 1 && r.Phase == JournalPhases.PlayerOpi));
        Assert.Equal(2, journal.Count(r => r.Round == 1 && r.Phase == JournalPhases.PlayerReputation));
        // Qualifying calibrates ONCE per weekend (it sets the grid), not per race.
        Assert.Equal(1, journal.Count(r => r.Round == 1 && r.Phase == JournalPhases.PlayerQualiAnchor));

        // The two-race career, two per-race folds included, re-simulates byte-identically.
        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), Inputs(playerId));
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.True(report.ComparedRows > 0);
    }

    private static ReplaySimInputs Inputs(string playerId)
    {
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        return new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerId,
            PlayerAge = 30, // TestPackBuilder drivers carry no Born year → season.Year − (Year − 30)
        };
    }
}
