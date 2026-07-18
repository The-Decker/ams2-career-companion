using Companion.Core.Career;
using Companion.Core.Determinism;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// Player-as-own-entrant: a career created on a CUSTOM livery that matches no pack entry seats the
/// player as their own independent synthetic entrant (stable <see cref="RoundGridResolver.SyntheticPlayerDriverId"/>,
/// neutral team + baseline ratings), so a non-standard skin works and the career never dead-ends, and
/// re-simulates BYTE-IDENTICALLY. Careers on a pack-entry livery never reach the synthetic branch.
/// </summary>
public sealed class OwnEntrantFoldDeterminismTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-own-entrant-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void PlayerOnACustomLivery_SeatsAnOwnEntrant_AndReplaysByteIdentically()
    {
        string packDirectory = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "own.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const string customLivery = "My Custom Skin #99 - The Player"; // matches NO pack entry
        const long seed = 20260708;
        SeasonPack pack;

        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "Own Entrant",
                       MasterSeed = seed,
                       PlayerLiveryName = customLivery,
                   },
                   environment))
        {
            pack = session.Pack;

            // The player is on the grid as their own synthetic entrant, on the custom livery.
            var grid = session.CurrentGrid();
            var player = Assert.Single(grid, s => s.IsPlayer);
            Assert.Equal(customLivery, player.Ams2LiveryName);
            Assert.Equal(RoundGridResolver.SyntheticPlayerDriverId, player.DriverId);

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
            PlayerDriverId = RoundGridResolver.SyntheticPlayerDriverId,
            PlayerAge = 30,
            CharacterRules = rules.Character,
        };

        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), inputs);
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }
}
