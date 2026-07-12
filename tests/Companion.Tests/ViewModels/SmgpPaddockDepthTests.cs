using Companion.Core.Career;
using Companion.Core.Packs;
using Companion.Core.Smgp;
using Companion.Data;
using Companion.ViewModels.Review;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Task 2 Paddock depth over the REAL career machinery: the player's evolving milestone TIMELINE (Slice 1),
/// the per-AI HEAD-TO-HEAD / per-track / form (Slice 2), and TEAM depth — roster season lines + sponsor
/// cross-reference + tier + palette (Slice 3). All are pure display projections over the folded results, so
/// they never touch the fold (the sibling determinism tests cover replay); these assert the projections read
/// the stored results correctly. Mirrors SmgpCrossSeasonStatsTests' real-ladder scaffolding.
/// </summary>
public sealed class SmgpPaddockDepthTests : IDisposable
{
    private const string SeatC = "Stock Livery #3"; // team.c LEVEL C — the player's start
    private const long Seed = 20260712;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-smgp-depth-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void HeadToHead_counts_ahead_and_behind_from_the_stored_results()
    {
        using var session = NewCareer();
        ApplyRound(session, playerWins: true);  // round 1: player P1, ahead of everyone
        ApplyRound(session, playerWins: false); // round 2: player last, behind everyone

        var paddock = session.SmgpPaddock();
        Assert.NotNull(paddock);
        // driver.a (team.a) always races — a real head-to-head record.
        var rival = paddock!.Drivers.Single(d => d.DriverId == "driver.a");

        Assert.NotNull(rival.HeadToHead);
        Assert.Equal(2, rival.HeadToHead!.RacesMet);
        Assert.Equal(1, rival.HeadToHead.PlayerAhead);  // won round 1
        Assert.Equal(1, rival.HeadToHead.DriverAhead);  // lost round 2
        // The player's best-together (P1 in round 1) is recorded with its venue.
        Assert.Equal(1, rival.HeadToHead.PlayerBestTogether);
        Assert.False(string.IsNullOrEmpty(rival.HeadToHead.BestTogetherVenue));

        // Per-track bests and recent form come from the same pass. The player won a race (best P1) and
        // the driver won a race (best P1) at some venue — the compare captures both sides.
        Assert.NotEmpty(rival.PerTrackBest);
        Assert.Contains(rival.PerTrackBest, tb => tb.PlayerBest == 1); // player's win venue
        Assert.Contains(rival.PerTrackBest, tb => tb.DriverBest == 1); // driver's win venue
        Assert.Equal(2, rival.FormRecent.Count); // two races run
    }

    [Fact]
    public void FormRecent_records_null_for_a_race_a_driver_did_not_finish()
    {
        using var session = NewCareer();
        ApplyRound(session, playerWins: true);                       // driver.a classified
        ApplyRound(session, playerWins: true, dnfDriver: "driver.a"); // driver.a retires

        var rival = session.SmgpPaddock()!.Drivers.Single(d => d.DriverId == "driver.a");
        Assert.Equal(2, rival.FormRecent.Count);
        Assert.Contains(rival.FormRecent, f => f is null);   // the DNF shows as a gap
        // Only the round they both finished counts toward the head-to-head.
        Assert.Equal(1, rival.HeadToHead!.RacesMet);
    }

    [Fact]
    public void Beating_a_named_rival_twice_records_a_rivalry_earned_beat()
    {
        using var session = NewCareer();
        // Beat driver.b (tier B, namable from tier C) in both rounds → a two-wins seat offer.
        ApplyRound(session, playerWins: true, rival: Challenge("driver.b"));
        ApplyRound(session, playerWins: true, rival: Challenge("driver.b"));

        var player = session.SmgpPaddock()!.Drivers.Single(d => d.IsPlayer);
        Assert.Contains(player.Timeline, b => b.Kind == SmgpBeatKind.RivalryEarned && b.Detail.Contains("driver.b"));
    }

    [Fact]
    public void Losing_to_a_named_rival_twice_records_a_rivalry_lost_beat()
    {
        using var session = NewCareer();
        // Lose to driver.b in both rounds → a two-losses forfeit (the seat is surrendered).
        ApplyRound(session, playerWins: false, rival: Challenge("driver.b"));
        ApplyRound(session, playerWins: false, rival: Challenge("driver.b"));

        var player = session.SmgpPaddock()!.Drivers.Single(d => d.IsPlayer);
        Assert.Contains(player.Timeline, b => b.Kind == SmgpBeatKind.RivalryLost && b.Detail.Contains("driver.b"));
        // A forfeit's tier drop is folded into the rivalry-lost beat — no bare demotion for that round.
        Assert.DoesNotContain(player.Timeline,
            b => b.Kind == SmgpBeatKind.Demotion && !b.Headline.Contains("OUT OF THE SMGP"));
    }

    [Fact]
    public void The_player_card_carries_an_evolving_timeline_and_a_live_intro()
    {
        using var session = NewCareer();
        ApplyRound(session, playerWins: true); // a winning debut

        var player = session.SmgpPaddock()!.Drivers.Single(d => d.IsPlayer);

        // The story starts: arrived, first start, first win are all present after one winning round.
        var kinds = player.Timeline.Select(b => b.Kind).ToList();
        Assert.Contains(SmgpBeatKind.Arrived, kinds);
        Assert.Contains(SmgpBeatKind.FirstStart, kinds);
        Assert.Contains(SmgpBeatKind.FirstWin, kinds);
        Assert.Equal(SmgpBeatKind.Arrived, player.Timeline[0].Kind); // arrival leads

        // The live intro reflects the current standing.
        Assert.False(string.IsNullOrWhiteSpace(player.NarrativeIntro));
        Assert.Contains("Season", player.NarrativeIntro);
    }

    [Fact]
    public void The_timeline_grows_across_seasons_and_banks_a_title()
    {
        int afterRoundOne;
        using (var s1 = NewCareer())
        {
            ApplyRound(s1, playerWins: true); // mid season 1
            afterRoundOne = s1.SmgpPaddock()!.Drivers.Single(d => d.IsPlayer).Timeline.Count;

            while (!s1.Summary.SeasonComplete)
                ApplyRound(s1, playerWins: true);
            var review = s1.SeasonReview();
            Assert.NotNull(review);
            s1.AcceptOffer(review!.Offers[0].TeamId);
            var reviewVm = new SeasonReviewViewModel(s1);
            reviewVm.SignAndContinueCommand.Execute(null);
            Assert.Null(reviewVm.TransitionError);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using var s2 = CareerSessionService.OpenCareer(CareerPath, Environment());
        ApplyRound(s2, playerWins: true); // one round of season 2
        var player = s2.SmgpPaddock()!.Drivers.Single(d => d.IsPlayer);

        // Winning season 1 banked a title beat...
        Assert.Contains(player.Timeline, b => b.Kind == SmgpBeatKind.Title);
        // ...a new season adds a season-milestone beat...
        Assert.Contains(player.Timeline, b => b.Kind == SmgpBeatKind.SeasonMilestone);
        Assert.Contains(player.Timeline, b => b.WhenLabel.StartsWith("Season 2")); // season-2 content
        // ...so the whole two-season story is strictly longer than one round in.
        Assert.True(player.Timeline.Count > afterRoundOne, "the timeline should grow with the career");

        // Head-to-head ACCUMULATES across seasons: a driver who raced every applied round (2 in season 1 +
        // 1 in season 2) has met the player 3 times — proving the per-season pass reads prior seasons and
        // the per-season player-id skip works (the player's id differs between seasons). The champion's
        // promotion benches ONE seat's driver, so we assert on whichever full-grid driver remains.
        Assert.Contains(s2.SmgpPaddock()!.Drivers, d => !d.IsPlayer && d.HeadToHead is { RacesMet: 3 });
    }

    [Fact]
    public void Team_cards_carry_tier_palette_roster_and_sponsor_cross_reference()
    {
        using var session = NewCareer();
        ApplyRound(session, playerWins: true);

        var paddock = session.SmgpPaddock()!;

        // team.bullets is a real sponsor-backed team (data/rules/smgp/sponsors.json) at prestige 4.
        var bullets = paddock.Teams.Single(t => t.TeamId == "team.bullets");
        Assert.Equal("Level B", bullets.Tier);              // prestige 4 → tier B
        Assert.StartsWith("#", bullets.PaletteHex);          // a real accent colour
        Assert.NotEmpty(bullets.Roster);                     // its live roster
        Assert.All(bullets.Roster, r => Assert.False(string.IsNullOrEmpty(r.SeasonLine)));
        Assert.NotEmpty(bullets.Sponsors);                   // cross-referenced from the sponsor board
        Assert.All(bullets.Sponsors, s => Assert.StartsWith("#", s.BrandColorHex));

        // The player's own team lists the PLAYER in its roster (IsPlayer flag for the GUI highlight).
        var playerTeam = paddock.Teams.Single(t => t.TeamId == "team.c");
        Assert.Contains(playerTeam.Roster, r => r.IsPlayer);
    }

    // ---------- scaffolding (mirrors SmgpCrossSeasonStatsTests' real-machinery ladder) ----------

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
        string packDirectory = Path.Combine(PacksRoot, "smgp-ladder");
        TestPackBuilder.Write(LadderPack(), packDirectory);
        return CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = CareerPath,
            CareerName = "Depth Career",
            MasterSeed = Seed,
            PlayerLiveryName = SeatC,
            SmgpMode = true,
        }, Environment());
    }

    private static SeasonPack LadderPack()
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        return basePack with
        {
            Manifest = basePack.Manifest with { CareerStyle = SmgpRules.CareerStyle },
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

    private static void ApplyRound(ICareerSession session, bool playerWins, SmgpRivalCall? rival = null, string? dnfDriver = null)
    {
        string player = session.Summary.PlayerDriverId;
        var others = session.CurrentGrid()
            .Select(s => s.DriverId)
            .Where(id => !string.Equals(id, player, StringComparison.Ordinal))
            .ToList();
        var dnf = new Dictionary<string, string>(StringComparer.Ordinal);
        if (dnfDriver is not null)
        {
            others.Remove(dnfDriver);
            dnf[dnfDriver] = "engine";
        }
        var classified = playerWins
            ? new List<string> { player }.Concat(others).ToList()
            : others.Append(player).ToList();
        session.Apply(new ResultDraft
        {
            Classified = classified,
            DidNotFinish = dnf,
            Disqualified = [],
            SmgpRival = rival,
        });
    }

    // driver.b races for team.bullets (tier B, directly above the player's tier C) — a namable rival.
    private static SmgpRivalCall Challenge(string driverId) => new() { RivalDriverId = driverId };
}
