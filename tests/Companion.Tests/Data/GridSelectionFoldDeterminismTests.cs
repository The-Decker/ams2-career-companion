using Companion.Core.Career;
using Companion.Core.Determinism;
using Companion.Core.Grid;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// v0.6.0 determinism gate for "choose the entire grid": a career that carries a
/// <see cref="GridSelection"/> folds the CHOSEN field (the resolver keeps only the selected liveries
/// + always the player) and re-simulates byte-identically — the <c>player.gridSelection</c> creation
/// row is provenance-excluded while its data rides in the start player state. A whole-pack career is
/// unaffected (the rest of the suite + the oracle stay byte-identical).
/// </summary>
public sealed class GridSelectionFoldDeterminismTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-gridsel-fold-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void ChosenField_FoldsTheChosenGrid_AndReplaysByteIdentically()
    {
        string packDirectory = Path.Combine(_root, "pack");
        // A three-seat pack so a chosen field of two (player + one AI) is a real narrowing that still
        // leaves the fold an AI seat to calibrate against.
        var basePack = TestPackBuilder.TwoRoundPack();
        var threeSeatPack = basePack with
        {
            Drivers = basePack.Drivers.Append(TestPackBuilder.Driver("driver.clark")).ToList(),
            Entries = basePack.Entries
                .Append(TestPackBuilder.Entry("team.brabham", "driver.clark", "3", "Stock Livery #3")).ToList(),
        };
        TestPackBuilder.Write(threeSeatPack, packDirectory);
        string careerPath = Path.Combine(_root, "careers", "grid.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const string playerId = "driver.hulme";
        const long seed = 20260707;
        Companion.Core.Packs.SeasonPack pack;

        // Choose a two-car field (Brabham + the player); the third seat (Stock Livery #3) is excluded.
        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "Chosen Grid",
                       MasterSeed = seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                       GridSelection = new GridSelection
                       {
                           IncludedLiveries = [TestPackBuilder.StockLivery1, TestPackBuilder.StockLivery2],
                       },
                   },
                   environment))
        {
            pack = session.Pack;

            // The chosen field took effect: two seats, the player present, the excluded seat gone.
            var grid = session.CurrentGrid();
            Assert.Equal(2, grid.Count);
            Assert.Contains(grid, s => s.IsPlayer && s.Ams2LiveryName == TestPackBuilder.StockLivery2);
            Assert.DoesNotContain(grid, s => s.Ams2LiveryName == "Stock Livery #3");

            for (int round = 0; round < 2; round++)
            {
                var seats = session.CurrentGrid();
                session.Apply(new ResultDraft
                {
                    Classified = seats.Select(s => s.DriverId).ToList(),
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                });
            }
            Assert.NotNull(session.SeasonReview());
        }

        using var db = CareerDatabase.Open(careerPath);
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);

        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerId,
            PlayerAge = 30,
            CharacterRules = rules.Character,
        };

        // The chosen field rides in the start player state (provenance-excluded row), so replay
        // resolves the same narrowed grid every round → byte-identical.
        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), inputs);
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }
}
