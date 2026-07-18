using Companion.Core.Scoring;
using Companion.Core.Smgp;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;
using Companion.ViewModels.Standings;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Task 3.3 screen-data projections over a fake session: the Standings player + rival highlight flags, and
/// the Driver/Dossier tab surfacing the Task-2 evolving narrative. Display-only, the fake supplies the ids
/// and the paddock card; the VMs just flag/surface them.
/// </summary>
public sealed class SmgpScreenDataTests
{
    // ---- Standings: player + rival highlight ----

    private static StandingsSnapshot OneRoundSnapshot() => new()
    {
        AfterRound = 1,
        Drivers =
        [
            new DriverStanding { Position = 1, DriverId = "driver.senna", CountedPoints = new(9), GrossPoints = new(9), RoundScores = [], Dropped = [] },
            new DriverStanding { Position = 2, DriverId = "driver.you", CountedPoints = new(6), GrossPoints = new(6), RoundScores = [], Dropped = [] },
            new DriverStanding { Position = 3, DriverId = "driver.ceara", CountedPoints = new(4), GrossPoints = new(4), RoundScores = [], Dropped = [] },
        ],
        Constructors = [],
    };

    [Fact]
    public void Standings_flags_the_player_and_the_named_rival_rows()
    {
        var session = new FakeCareerSession
        {
            Identity = ("driver.you", "You"),
            SmgpRivalDriverId = "driver.ceara",
        };
        var vm = new StandingsViewModel([OneRoundSnapshot()], session.Pack, settings: null, session: session);

        var player = vm.DriverRows.Single(r => r.CompetitorId == "driver.you");
        var rival = vm.DriverRows.Single(r => r.CompetitorId == "driver.ceara");
        Assert.True(player.IsPlayer);
        Assert.False(player.IsRival);
        Assert.True(rival.IsRival);
        Assert.False(rival.IsPlayer);
        // Exactly one of each across the table.
        Assert.Single(vm.DriverRows, r => r.IsPlayer);
        Assert.Single(vm.DriverRows, r => r.IsRival);
    }

    [Fact]
    public void Standings_flags_no_rival_when_none_is_named()
    {
        var session = new FakeCareerSession { Identity = ("driver.you", "You") }; // SmgpRivalDriverId null
        var vm = new StandingsViewModel([OneRoundSnapshot()], session.Pack, settings: null, session: session);
        Assert.DoesNotContain(vm.DriverRows, r => r.IsRival);
        Assert.Single(vm.DriverRows, r => r.IsPlayer);
    }

    // ---- Driver / Dossier: the evolving narrative ----

    [Fact]
    public void Dossier_surfaces_the_players_paddock_timeline_and_intro()
    {
        var beats = new List<SmgpCareerBeat>
        {
            new() { WhenLabel = "Season 1", Kind = SmgpBeatKind.Arrived, Headline = "ARRIVED", Detail = "d" },
            new() { WhenLabel = "Season 1 · Monaco", Kind = SmgpBeatKind.FirstWin, Headline = "FIRST WIN", Detail = "d" },
        };
        var session = new FakeCareerSession
        {
            Paddock = new SmgpPaddockModel
            {
                Drivers =
                [
                    new SmgpDriverCard
                    {
                        DriverId = "driver.you", Name = "You", TeamId = "team.c", TeamName = "C", Number = null,
                        PortraitKey = "p", CarKey = "c", Epithet = "YOU", Bio = ["b"], Quotes = [], IsPlayer = true,
                        Career = null, Season = null, Prestige = 3,
                        Timeline = beats, NarrativeIntro = "Season 1 of 17 · racing for C · yet to score",
                    },
                ],
                Teams = [],
            },
        };

        var vm = new DossierViewModel(session);
        Assert.True(vm.HasSmgpNarrative);
        Assert.Equal(2, vm.Timeline.Count);
        Assert.Contains("Season 1 of 17", vm.NarrativeIntro);
    }

    [Fact]
    public void Dossier_has_no_narrative_for_a_non_smgp_career()
    {
        var vm = new DossierViewModel(new FakeCareerSession()); // no paddock
        Assert.False(vm.HasSmgpNarrative);
        Assert.Empty(vm.Timeline);
        Assert.Equal("", vm.NarrativeIntro);
    }
}
