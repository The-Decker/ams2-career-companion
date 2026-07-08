using Companion.Core.Packs;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// A player who picked a car that did NOT qualify for a round (1988 was a pre-qualifying year — more
/// cars than grid slots) must still appear on the grid the app stages into AMS2, because the grid size
/// is hardcoded. The session's grid resolution seats the player from their own pack entry and drops the
/// slowest qualifier to hold the size — matching the fold, which already scores the player every round.
/// (Regression for the display/staging vs fold discrepancy on DNQ / gap-seat careers.)
/// </summary>
public sealed class PlayerDnqSeatingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-dnq-seat-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void CurrentGrid_PlayerDidNotQualifyThisRound_StillSeatsThePlayer_AtGridSize()
    {
        string packDirectory = Path.Combine(_root, "pack");

        // A three-car pack whose round 1 authors a hardcoded two-slot grid that EXCLUDES the player's
        // driver (driver.hulme) — as if the player's car failed to qualify that round.
        var basePack = TestPackBuilder.TwoRoundPack();
        var pack = basePack with
        {
            Drivers = basePack.Drivers.Append(TestPackBuilder.Driver("driver.clark")).ToList(),
            Entries = basePack.Entries
                .Append(TestPackBuilder.Entry("team.brabham", "driver.clark", "3", "Stock Livery #3")).ToList(),
            Season = basePack.Season with
            {
                Rounds = basePack.Season.Rounds.Select(r => r.Round == 1
                    ? r with
                    {
                        Grid = new PackRoundGrid
                        {
                            Size = 2,
                            StarterDriverIds = ["driver.brabham", "driver.clark"],   // hulme DNQ'd
                        },
                    }
                    : r).ToList(),
            },
        };
        TestPackBuilder.Write(pack, packDirectory);

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        using var session = CareerSessionService.CreateCareer(
            new CareerCreationRequest
            {
                PackDirectory = packDirectory,
                CareerFilePath = Path.Combine(_root, "careers", "dnq.ams2career"),
                CareerName = "DNQ Seat",
                MasterSeed = 20260707,
                PlayerLiveryName = TestPackBuilder.StockLivery2,   // the player's (hulme's) car
            },
            environment);

        var grid = session.CurrentGrid();

        // The player is on the grid even though their car didn't qualify, and the field is held at the
        // hardcoded size (one qualifier was dropped to make room) — so the staged AMS2 file includes
        // the player's car instead of leaving them off the grid.
        Assert.Equal(2, grid.Count);
        Assert.Contains(grid, s => s.IsPlayer && s.Ams2LiveryName == TestPackBuilder.StockLivery2);
    }
}
