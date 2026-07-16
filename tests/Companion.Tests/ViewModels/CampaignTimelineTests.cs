using Companion.Core.Packs;
using Companion.Core.Smgp;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// <see cref="ICareerSession.CampaignTimeline"/> — the whole campaign arc as one timeline. An SMGP
/// career pins the full 17-season horizon up front (played seasons Completed/Current, the future
/// Locked); a legacy historical career with no pinned plan lists only the seasons it has actually
/// played. Scaffolding mirrors <c>SmgpMultiSeasonDnqTests</c>' real-machinery DNQ ladder (synthetic
/// five-seat pack + library, career carried over via AcceptOffer/StartNextSeason + reopen).
/// </summary>
public sealed class CampaignTimelineTests : IDisposable
{
    private const string SeatC = "Stock Livery #3"; // team.c — the player's start
    private const long Seed = 20260712;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-campaign-tl-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ---------- SMGP: the pinned 17-season horizon ----------

    [Fact]
    public void FreshSmgpCareer_PinsTheFullSeventeenSeasonArc()
    {
        using var session = CreateSmgpCareer("fresh");

        var timeline = session.CampaignTimeline();

        Assert.Equal(SmgpRules.CampaignSeasons, timeline.Count);
        Assert.Equal(17, timeline.Count);
        Assert.Equal(Enumerable.Range(1, 17), timeline.Select(e => e.Ordinal));

        Assert.Equal(CampaignSeasonState.Current, timeline[0].State);
        Assert.False(timeline[0].PlayerChampion);
        Assert.Null(timeline[0].PlayerPosition);   // no completed outcome yet

        Assert.All(timeline.Skip(1), entry => Assert.Equal(CampaignSeasonState.Locked, entry.State));
    }

    [Fact]
    public void AfterSeasonOne_TheArcAdvances_CompletedThenCurrentThenLocked()
    {
        PlaySmgpSeasonOneAndSign("advance");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using var session = CareerSessionService.OpenCareer(CareerPath("advance"), Environment("advance"));
        var timeline = session.CampaignTimeline();

        Assert.Equal(17, timeline.Count);

        Assert.Equal(CampaignSeasonState.Completed, timeline[0].State);
        Assert.NotNull(timeline[0].PlayerPosition);                // the season's final outcome
        Assert.InRange(timeline[0].PlayerPosition!.Value, 1, 5);   // five-seat field

        Assert.Equal(CampaignSeasonState.Current, timeline[1].State);
        Assert.Null(timeline[1].PlayerPosition);

        Assert.All(timeline.Skip(2), entry => Assert.Equal(CampaignSeasonState.Locked, entry.State));
    }

    // ---------- legacy historical: only the played seasons ----------

    [Fact]
    public void HistoricalCareerWithNoPlan_ListsOnlyPlayedSeasons()
    {
        string packDirectory = Path.Combine(_root, "hist", "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        using var session = CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = Path.Combine(_root, "hist", "hist.ams2career"),
            CareerName = "Plain 1967",
            MasterSeed = Seed,
            PlayerLiveryName = TestPackBuilder.StockLivery2,
        }, ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "hist", "docs"),
            library: TestPackBuilder.Library()));

        // Fresh: one in-progress season, no locked future horizon invented.
        var fresh = session.CampaignTimeline();
        var current = Assert.Single(fresh);
        Assert.Equal(1, current.Ordinal);
        Assert.Equal(CampaignSeasonState.Current, current.State);
        Assert.Equal(1967, current.Year);

        // Play the whole (two-round) season: still exactly the played season, now Completed.
        ApplyRound(session);
        ApplyRound(session);
        Assert.True(session.Summary.SeasonComplete);

        var done = session.CampaignTimeline();
        var completed = Assert.Single(done);
        Assert.Equal(CampaignSeasonState.Completed, completed.State);
        Assert.NotNull(completed.PlayerPosition);
    }

    // ---------- scaffolding (mirrors SmgpMultiSeasonDnqTests) ----------

    private string PacksRoot(string name) => Path.Combine(_root, name, "packs");

    private string CareerPath(string name) => Path.Combine(_root, name, "career.ams2career");

    private CareerEnvironment Environment(string name) => new()
    {
        ContentLibrary = FiveSeatLibrary(),
        LocateInstall = static () => null,
        DocumentsDirectory = Path.Combine(_root, name, "docs"),
        RulesDirectory = ViewModelTestData.RulesDirectory,
        PackSearchRoots = () => [PacksRoot(name), Path.Combine(AppContext.BaseDirectory, "packs")],
    };

    private CareerSessionService CreateSmgpCareer(string name)
    {
        string packDirectory = Path.Combine(PacksRoot(name), "smgp-dnq-ladder");
        TestPackBuilder.Write(DnqLadderPack(), packDirectory);
        return CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = CareerPath(name),
            CareerName = "Campaign Timeline Career",
            MasterSeed = Seed,
            PlayerLiveryName = SeatC,
            SmgpMode = true,
        }, Environment(name));
    }

    /// <summary>Creates the SMGP career, plays season 1 to completion, accepts the top offer and
    /// signs into the same-pack carryover season 2 (then this session is spent — reopen to land there).</summary>
    private void PlaySmgpSeasonOneAndSign(string name)
    {
        using var session = CreateSmgpCareer(name);

        while (!session.Summary.SeasonComplete)
            ApplyRound(session);

        var review = session.SeasonReview();
        Assert.NotNull(review);
        Assert.NotEmpty(review!.Offers);
        string teamId = review.Offers[0].TeamId;
        session.AcceptOffer(teamId);
        session.StartNextSeason(teamId);
    }

    /// <summary>Five one-driver teams down the ladder, TWO rounds, each capping the grid at 4 → one car
    /// DNQs per round (the seeded roll picks which). SMGP style. The player takes Seat C.</summary>
    private static SeasonPack DnqLadderPack()
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        var grid = new PackRoundGrid
        {
            Size = 4,
            StarterDriverIds = ["driver.a", "driver.b", "driver.c", "driver.d"], // baked; the transform re-rolls
        };
        return basePack with
        {
            Manifest = basePack.Manifest with { CareerStyle = SmgpRules.CareerStyle },
            Teams =
            [
                Team("team.a", 5), Team("team.b", 4), Team("team.c", 3), Team("team.d", 2), Team("team.e", 3),
            ],
            Drivers =
            [
                TestPackBuilder.Driver("driver.a"), TestPackBuilder.Driver("driver.b"),
                TestPackBuilder.Driver("driver.c"), TestPackBuilder.Driver("driver.d"),
                TestPackBuilder.Driver("driver.e"),
            ],
            Entries =
            [
                TestPackBuilder.Entry("team.a", "driver.a", "1", "Stock Livery #1"),
                TestPackBuilder.Entry("team.b", "driver.b", "2", "Stock Livery #2"),
                TestPackBuilder.Entry("team.c", "driver.c", "3", SeatC),
                TestPackBuilder.Entry("team.d", "driver.d", "4", "Stock Livery #4"),
                TestPackBuilder.Entry("team.e", "driver.e", "5", "Stock Livery #5"),
            ],
            Season = basePack.Season with
            {
                Rounds = basePack.Season.Rounds.Select(r => r with { Grid = grid }).ToList(),
            },
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

    /// <summary>Applies one round in resolved-grid order (the DNQ field determines who is present;
    /// the player is always cap-protected).</summary>
    private static void ApplyRound(ICareerSession session)
    {
        var grid = session.CurrentGrid().Select(s => s.DriverId).ToList();
        session.Apply(new ResultDraft
        {
            Classified = grid,
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });
    }
}
