using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Character death &amp; injury, Slice 4 (shell routing — the review-confirmed gap): an injured round must
/// route to the auto-sim sit-out screen, NEVER manual result entry, and its Continue must fold the
/// auto-simulated round. Without this the just-built injury mechanic is silently bypassed (the player
/// "races while injured" and never heals).
/// </summary>
public sealed class SitOutRoutingTests
{
    [Fact]
    public void InjuredRound_RoutesToSitOut_BlocksManualEntry_AndContinueAutoSimulates()
    {
        var session = new InjuredSession
        {
            SitOut = new SitOutStatus
            {
                RaceSuspensionRemaining = 1,
                SeasonEnding = false,
                Headline = "INJURED — auto-simulating round (1 remaining)",
            },
        };
        using var home = new HomeViewModel(session);

        // Opening onto an injured round leads with the sit-out screen, not the briefing/result entry.
        Assert.True(home.IsSitOutStep);
        var screen = Assert.IsType<SitOutViewModel>(home.CurrentContent);
        Assert.Contains("1 remaining", screen.Status.Headline);

        // Even the explicit "Enter Result" action refuses to open manual entry while injured.
        home.EnterResultCommand.Execute(null);
        Assert.True(home.IsSitOutStep);
        Assert.False(home.IsSessionIntroState);
        Assert.False(session.ManualApplied);

        // Continue folds the auto-simulated round; the injury heals, so the shell advances off the screen.
        screen.ContinueCommand.Execute(null);
        Assert.Equal(1, session.AutoSims);
        Assert.False(session.ManualApplied); // no manual result was ever entered for the skipped round
        Assert.False(home.IsSitOutStep);     // healed → advanced to the next round's briefing
        Assert.True(home.IsBriefingState);
    }

    [Fact]
    public void ConsecutiveInjuredRounds_ChainSitOutScreens_OneContinueEach()
    {
        var session = new InjuredSession
        {
            SitOut = new SitOutStatus { RaceSuspensionRemaining = 2, SeasonEnding = false, Headline = "INJURED" },
            HealAfter = 2, // stays injured for two auto-sims, then fit
        };
        using var home = new HomeViewModel(session);

        Assert.True(home.IsSitOutStep);
        ((SitOutViewModel)home.CurrentContent!).ContinueCommand.Execute(null); // round 1 auto-sim
        Assert.True(home.IsSitOutStep);                                        // still injured → next sit-out
        ((SitOutViewModel)home.CurrentContent!).ContinueCommand.Execute(null); // round 2 auto-sim
        Assert.False(home.IsSitOutStep);                                       // healed → briefing

        Assert.Equal(2, session.AutoSims);
    }

    private static GridSeat Seat(string driverId, bool isPlayer) => new()
    {
        DriverId = driverId,
        DriverName = driverId,
        TeamId = "team.brabham",
        TeamName = "Brabham",
        Number = "1",
        Ams2LiveryName = driverId,
        Ratings = TestPackBuilder.Driver(driverId).Ratings,
        Reliability = 0.9,
        WeightScalar = 1,
        PowerScalar = 1,
        DragScalar = 1,
        IsPlayer = isPlayer,
    };

    /// <summary>A session that is injured (CurrentSitOut non-null) and heals after N auto-sims.</summary>
    private sealed class InjuredSession : ICareerSession
    {
        private readonly FakeCareerSession _inner = new()
        {
            Grid = [Seat("driver.hulme", isPlayer: true), Seat("driver.brabham", isPlayer: false)],
        };

        public SitOutStatus? SitOut { get; set; }
        public int HealAfter { get; init; } = 1;
        public int AutoSims { get; private set; }
        public bool ManualApplied { get; private set; }

        public SitOutStatus? CurrentSitOut() => SitOut;

        public void AutoSimulateRound()
        {
            AutoSims++;
            if (AutoSims >= HealAfter)
                SitOut = null; // healed → fit again
        }

        public void Apply(ResultDraft draft) => ManualApplied = true;

        public CareerSummary Summary => _inner.Summary;
        public SeasonPack Pack => _inner.Pack;
        public BriefingModel? CurrentBriefing() => _inner.CurrentBriefing();
        public StageOutcome StageCurrentGrid() => _inner.StageCurrentGrid();
        public IReadOnlyList<GridSeat> CurrentGrid() => _inner.CurrentGrid();
        public ConfirmModel Preview(ResultDraft draft) => _inner.Preview(draft);
        public StandingsSnapshot? CurrentStandings() => _inner.CurrentStandings();
        public IReadOnlyList<StandingsSnapshot> AllSnapshots() => _inner.AllSnapshots();
        public int? CurrentSliderRecommendation() => _inner.CurrentSliderRecommendation();
        public SeasonReviewModel? SeasonReview() => _inner.SeasonReview();
        public void AcceptOffer(string teamId) => _inner.AcceptOffer(teamId);
    }
}
