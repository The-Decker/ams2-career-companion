using Companion.Core.Packs;
using Companion.Core.Smgp;
using Companion.Data;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The Tycoon Team Mode read-only DATA SPINE (Task 5) over the REAL career machinery: SmgpTeamDashboard() —
/// the player's team (roster + sponsors + tier + a derived constructors' standing + history) plus every team
/// ranked as the competitive landscape, and a flavour "team of the season" seed. A pure display projection
/// (no fold mechanics → replay-safe); these assert it reads the folded standings + the paddock team cards
/// correctly. Scaffolding mirrors <see cref="SmgpDispatchesTests"/>.
/// </summary>
public sealed class SmgpTeamDashboardTests : IDisposable
{
    private const string SeatC = "Stock Livery #3"; // team.c LEVEL C — the player's start
    private const long Seed = 20260712;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-smgp-tycoon-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void Outside_the_smgp_mode_the_dashboard_is_null()
    {
        using var session = NewCareer(smgp: false);
        ApplyRound(session, playerWins: true);
        Assert.Null(session.SmgpTeamDashboard());
    }

    [Fact]
    public void The_dashboard_lists_every_team_with_the_player_flagged_and_no_standing_before_racing()
    {
        using var session = NewCareer();

        var dash = session.SmgpTeamDashboard();
        Assert.NotNull(dash);
        Assert.Equal(5, dash!.Teams.Count);                         // every team on the grid

        // The player's team is the LEVEL C seat, flagged, and present in the ranked table.
        Assert.True(dash.PlayerTeam.IsPlayerTeam);
        Assert.Equal("team.c", dash.PlayerTeam.TeamId);
        Assert.Contains(dash.Teams, t => t.TeamId == "team.c" && t.IsPlayerTeam);

        // Rival table excludes the player's team.
        Assert.Equal(4, dash.RivalTeams.Count);
        Assert.DoesNotContain(dash.RivalTeams, t => t.IsPlayerTeam);

        // Every entry carries the ladder tier + budget-tier flavour.
        Assert.All(dash.Teams, t => Assert.StartsWith("Level ", t.Tier));
        Assert.All(dash.Teams, t => Assert.False(string.IsNullOrWhiteSpace(t.BudgetTier)));

        // Nothing has raced yet: no standing, no team of the season.
        Assert.Null(dash.PlayerTeam.ChampionshipPosition);
        Assert.All(dash.Teams, t => Assert.Equal(0, t.ChampionshipPoints));
        Assert.Null(dash.TeamOfSeason);
    }

    [Fact]
    public void A_win_gives_the_player_team_points_a_top_standing_and_a_team_of_the_season()
    {
        using var session = NewCareer();
        ApplyRound(session, playerWins: true);

        var dash = session.SmgpTeamDashboard();
        Assert.NotNull(dash);

        // The player's team scored and leads the derived constructors' running.
        Assert.True(dash!.PlayerTeam.ChampionshipPoints > 0);
        Assert.Equal(1, dash.PlayerTeam.ChampionshipPosition);

        // The table is ranked by points, descending.
        for (int i = 1; i < dash.Teams.Count; i++)
            Assert.True(dash.Teams[i - 1].ChampionshipPoints >= dash.Teams[i].ChampionshipPoints);

        // A flavour team-of-the-season now exists, explicitly labelled as flavour (no economy model).
        Assert.NotNull(dash.TeamOfSeason);
        Assert.Contains("Flavour", dash.TeamOfSeason!.Note, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(dash.TeamOfSeason.Headline));
    }

    [Fact]
    public void The_player_team_faithfully_carries_the_paddock_roster_and_sponsors()
    {
        using var session = NewCareer();
        ApplyRound(session, playerWins: true);

        var dash = session.SmgpTeamDashboard();
        var paddock = session.SmgpPaddock();
        Assert.NotNull(dash);
        Assert.NotNull(paddock);

        var paddockTeam = paddock!.Teams.Single(t => t.TeamId == dash!.PlayerTeam.TeamId);
        // The dashboard reuses the paddock team card's roster + sponsors (same data, no divergence).
        Assert.Equal(paddockTeam.Roster, dash!.PlayerTeam.Roster);
        Assert.Equal(paddockTeam.Sponsors, dash.PlayerTeam.Sponsors);
        Assert.Equal(paddockTeam.History, dash.PlayerTeam.History);
        // The player appears on their own team's roster.
        Assert.Contains(dash.PlayerTeam.Roster, r => r.IsPlayer);
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

    private CareerSessionService NewCareer(bool smgp = true)
    {
        string packDirectory = Path.Combine(PacksRoot, "smgp-ladder");
        TestPackBuilder.Write(LadderPack(smgp), packDirectory);
        return CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = CareerPath,
            CareerName = "Tycoon Career",
            MasterSeed = Seed,
            PlayerLiveryName = SeatC,
            SmgpMode = smgp,
        }, Environment());
    }

    private static SeasonPack LadderPack(bool smgp = true)
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        return basePack with
        {
            Manifest = basePack.Manifest with { CareerStyle = smgp ? SmgpRules.CareerStyle : "" },
            Teams =
            [
                Team("team.a", 5), Team("team.bullets", 4), Team("team.c", 3), Team("team.d", 2), Team("team.e", 3),
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
                TestPackBuilder.Entry("team.bullets", "driver.b", "2", "Stock Livery #2"),
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
}
