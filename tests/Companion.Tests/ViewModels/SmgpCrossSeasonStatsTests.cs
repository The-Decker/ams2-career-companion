using Companion.Core.Career;
using Companion.Core.Packs;
using Companion.Core.Smgp;
using Companion.Data;
using Companion.ViewModels.Review;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Cross-season SMGP stat/points accrual: the career card (SmgpCareerStats) must span EVERY season of
/// the career, not just the current one — the prerequisite for a real 17-season campaign record. Drives
/// a two-season carryover career over the REAL machinery (win season 1, sign on, race season 2) and
/// asserts the player's Paddock card totals both seasons while the season card shows only the current one.
/// A pure display projection — this never touches the fold (the sibling title-defense tests cover replay).
/// </summary>
public sealed class SmgpCrossSeasonStatsTests : IDisposable
{
    private const string SeatC = "Stock Livery #3"; // team.c LEVEL C — the player's start
    private const long Seed = 20260712;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-smgp-xseason-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void PlayerCareerCard_SpansAllSeasons_WhileTheSeasonCardIsCurrentOnly()
    {
        WinSeasonOneAndSign(); // 2 rounds won → season 1: 2 wins / 2 podiums / 2 starts, and the title

        using var s2 = CareerSessionService.OpenCareer(CareerPath, Environment());

        // Season 2, one round in — the player wins it.
        ApplyRound(s2, playerWins: true);

        var paddock = s2.SmgpPaddock();
        Assert.NotNull(paddock);
        var player = paddock!.Drivers.Single(d => d.IsPlayer);

        // The CAREER card spans BOTH seasons: season 1 (2 wins) + season 2 so far (1 win) = 3.
        Assert.NotNull(player.Career);
        Assert.Equal(3, player.Career!.Wins);
        Assert.Equal(3, player.Career.Podiums);
        Assert.Equal(3, player.Career.Starts);
        Assert.Equal(1, player.Career.Titles);          // banked from winning season 1
        Assert.True(player.Career.Points > 0);

        // The SEASON card is the current season ONLY: one win, one start.
        Assert.NotNull(player.Season);
        Assert.Equal(1, player.Season!.Wins);
        Assert.Equal(1, player.Season.Starts);

        // The all-time totals are strictly larger than the current season — proof the prior season rolled up.
        Assert.True(player.Career.Wins > player.Season.Wins);
        Assert.True(player.Career.Points >= player.Season.Points);

        // The player's OWN card has a generated bio (Mike wanted a biography for "you") — three
        // paragraphs that reflect the live record + the 17-season campaign.
        Assert.Equal(3, player.Bio.Count);
        Assert.All(player.Bio, p => Assert.False(string.IsNullOrWhiteSpace(p)));
        Assert.Contains(player.Bio, p => p.Contains("Seventeen seasons") || p.Contains("17"));
    }

    [Fact]
    public void FirstSeason_HasNoPriorRollup_SoCareerEqualsTheCurrentSeason()
    {
        string packDirectory = Path.Combine(PacksRoot, "smgp-ladder");
        TestPackBuilder.Write(LadderPack(), packDirectory);

        using var session = CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = CareerPath,
            CareerName = "XSeason Career",
            MasterSeed = Seed,
            PlayerLiveryName = SeatC,
            SmgpMode = true,
        }, Environment());

        ApplyRound(session, playerWins: true); // one round of season 1

        var player = session.SmgpPaddock()!.Drivers.Single(d => d.IsPlayer);
        // No prior seasons → the career card equals the single accrued round (baseline is zero for the player).
        Assert.Equal(1, player.Career!.Wins);
        Assert.Equal(1, player.Career.Starts);
        Assert.Equal(0, player.Career.Titles);
    }

    // ---------- scaffolding (mirrors SmgpTitleDefenseTests' real-machinery ladder) ----------

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

    private static SeasonPack LadderPack()
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        return basePack with
        {
            Manifest = basePack.Manifest with { CareerStyle = SmgpRules.CareerStyle },
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
                TestPackBuilder.Driver(SmgpSchedule.CearaDriverId) with
                {
                    Ratings = TestPackBuilder.Driver(SmgpSchedule.CearaDriverId).Ratings with { RaceSkill = 0.99 },
                },
            ],
            Entries =
            [
                TestPackBuilder.Entry("team.a", "driver.a", "1", "Stock Livery #1"),
                TestPackBuilder.Entry("team.b", "driver.b", "2", "Stock Livery #2"),
                TestPackBuilder.Entry("team.c", "driver.c", "3", SeatC),
                TestPackBuilder.Entry("team.d", "driver.d", "4", "Stock Livery #4"),
                TestPackBuilder.Entry("team.e", SmgpSchedule.CearaDriverId, "17", "Stock Livery #5"),
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
                    StockLib1563 = ["Stock Livery #1", "Stock Livery #2", SeatC, "Stock Livery #4", "Stock Livery #5"],
                },
            },
        };
    }

    private void WinSeasonOneAndSign()
    {
        string packDirectory = Path.Combine(PacksRoot, "smgp-ladder");
        TestPackBuilder.Write(LadderPack(), packDirectory);

        using var session = CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = CareerPath,
            CareerName = "XSeason Career",
            MasterSeed = Seed,
            PlayerLiveryName = SeatC,
            SmgpMode = true,
        }, Environment());

        while (!session.Summary.SeasonComplete)
            ApplyRound(session, playerWins: true);

        var review = session.SeasonReview();
        Assert.NotNull(review);
        session.AcceptOffer(review!.Offers[0].TeamId);

        var vm = new SeasonReviewViewModel(session);
        vm.SignAndContinueCommand.Execute(null);
        Assert.Null(vm.TransitionError);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    private static void ApplyRound(ICareerSession session, bool playerWins)
    {
        var others = session.CurrentGrid()
            .Select(s => s.DriverId)
            .Where(id => !string.Equals(id, session.Summary.PlayerDriverId, StringComparison.Ordinal))
            .ToList();
        var classified = playerWins
            ? new List<string> { session.Summary.PlayerDriverId }.Concat(others).ToList()
            : others.Append(session.Summary.PlayerDriverId).ToList();
        session.Apply(new ResultDraft
        {
            Classified = classified,
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });
    }
}
