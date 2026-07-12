using System.Windows;
using Companion.App.Views;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Briefing;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen render of the Race Day briefing with the SMGP rival panel live (M3 slice
/// 5): a real BriefingView over a real BriefingViewModel whose stub session reports an smgp
/// briefing — the round header, the picker, the dossier card (with a selected rival so the
/// MACHINE block, ladder telegraph and swap answer all bind) and the forced-challenge lock.
/// Guards the binding paths a compiled-XAML build cannot. Self-skips off Windows.</summary>
public sealed class BriefingSmgpRenderTests
{
    private sealed class SmgpSession : ICareerSession
    {
        public bool Forced { get; init; }

        public CareerSummary Summary { get; } = new()
        {
            CareerName = "SMGP Render",
            SeasonYear = 1990,
            SeriesName = "SMGP1 World Championship",
            CurrentRound = 1,
            RoundCount = 16,
            PlayerDriverId = "driver.player",
            PlayerLiveryName = "Minarae #12",
        };

        public BriefingModel? CurrentBriefing() => new()
        {
            Round = new PackRound
            {
                Round = 1,
                Name = "San Marino",
                Date = "1990-01-01",
                Track = new PackTrackRef { Id = "imola" },
                Laps = 5,
            },
            VenueDisplayName = "San Marino",
            IsPlaceholder = false,
            Settings = [new CopyableSetting("Track", "imola"), new CopyableSetting("Laps", "5")],
            RecommendedSlider = 95,
        };

        public SmgpBriefingModel? CurrentSmgpBriefing() => new()
        {
            RoundHeader = "SAN MARINO · ROUND 1",
            SeasonLine = "SEASON  P1 · 9 PTS",
            CareerLine = "CAREER  2 WINS · 1 POLE",
            AdviceLine = "PASS THE CARS AT THE HAIRPIN TURN!",
            Titles = 0,
            SeasonOrdinal = 1,
            SeasonsTotal = Companion.Core.Smgp.SmgpRules.CampaignSeasons,
            CareerOver = false,
            ForcedChallengerDriverId = Forced ? "driver.gilberto_ceara" : null,
            Rivals =
            [
                new SmgpRivalOption
                {
                    DriverId = "driver.gilberto_ceara",
                    DriverName = "Gilberto Ceara",
                    TeamId = "team.bullets",
                    TeamName = "Bullets",
                    MachineLine = "formula_classic_g3m1",
                    Quote = "IT'S INTERESTING.",
                    OfferOnWin = true,
                    ForfeitOnLoss = false,
                },
                new SmgpRivalOption
                {
                    DriverId = "driver.alain_asselin",
                    DriverName = "Alain Asselin",
                    TeamId = "team.madonna",
                    TeamName = "Madonna",
                    MachineLine = "formula_classic_g3m1",
                    Quote = "IT'S INTERESTING.",
                    OfferOnWin = false,
                    ForfeitOnLoss = true,
                },
            ],
        };

        public SeasonPack Pack { get; } = new()
        {
            Manifest = new PackManifest
            {
                PackId = "smgp-render", Name = "SMGP Render", Version = "1.0.0", FormatVersion = 1,
                CareerStyle = "smgp",
            },
            Season = new SeasonDefinition
            {
                Year = 1990,
                SeriesName = "SMGP1 World Championship",
                Ams2Class = "F-Classic_Gen3",
                PointsSystem = new CatalogSeason { RacePoints = [new(9)] },
                Rounds =
                [
                    new PackRound
                    {
                        Round = 1, Name = "San Marino", Date = "1990-01-01",
                        Track = new PackTrackRef { Id = "imola" }, Laps = 5,
                    },
                ],
            },
            Teams = [],
            Drivers = [],
            Entries = [],
        };

        public StageOutcome StageCurrentGrid() => new() { Success = true, Messages = ["staged"] };
        public IReadOnlyList<GridSeat> CurrentGrid() => [];
        public ConfirmModel Preview(ResultDraft draft) => new() { RoundPoints = [], Movements = [], Headline = "" };
        public void Apply(ResultDraft draft) { }
        public StandingsSnapshot? CurrentStandings() => null;
        public IReadOnlyList<StandingsSnapshot> AllSnapshots() => [];
        public int? CurrentSliderRecommendation() => null;
        public SeasonReviewModel? SeasonReview() => null;
        public void AcceptOffer(string teamId) { }
    }

    [Fact]
    public void RivalScreenView_RendersTheDossierCard_OverTheSharedBriefing()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            // The rival step wraps the SHARED briefing — the pick / name-him state lives there and
            // rides into the race draft at Apply, so moving the UI onto its own screen changes nothing.
            var briefing = new BriefingViewModel(new SmgpSession());
            Assert.True(briefing.SmgpActive);
            Assert.True(briefing.SmgpPickEnabled);          // a free pick
            Assert.Null(briefing.SelectedSmgpRival);        // fresh round: unnamed

            // BROWSING commits nothing: selecting shows the dossier, but the draft carries no rival
            // until YES names him.
            briefing.SelectedSmgpRival = briefing.SmgpRivals[0];
            Assert.True(briefing.SmgpCanName);
            Assert.Null(briefing.BuildSmgpRival());
            Assert.False(briefing.SmgpSwapPromptVisible);

            // YES — the commitment the fold counts the two-wins ladder against.
            briefing.SmgpNameRivalCommand.Execute(null);
            Assert.True(briefing.SmgpRivalNamed);
            Assert.False(briefing.SmgpCanName);
            Assert.True(briefing.SmgpSwapPromptVisible);
            Assert.NotNull(briefing.BuildSmgpRival());
            Assert.True(briefing.BuildSmgpRival()!.SeatSwapAccepted);

            var view = new RivalScreenView { DataContext = new RivalScreenViewModel(briefing) };
            view.Measure(new Size(1000, 1400));
            view.Arrange(new Rect(0, 0, 1000, 1400));
            view.UpdateLayout();

            Assert.True(view.ActualWidth > 0);
            Assert.True(view.ActualHeight > 0);

            // Named: the YES button yields to the confirmation banner (pins the actual visibility,
            // which a Bool-converter inversion would render fine but wrong).
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("SmgpNameRivalButton")).Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("SmgpNamedBanner")).Visibility);

            // Withdrawing un-names: the draft goes back to carrying nothing.
            briefing.SmgpDeclineRivalCommand.Execute(null);
            Assert.Null(briefing.BuildSmgpRival());
            Assert.False(briefing.SmgpRivalNamed);
        });
    }

    [Fact]
    public void BriefingView_LocksThePicker_OnAForcedChallenge()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new BriefingViewModel(new SmgpSession { Forced = true });
            Assert.True(vm.SmgpForced);
            Assert.False(vm.SmgpPickEnabled);
            // The forced challenger arrives ALREADY NAMED (no YES needed) and the call marks it.
            Assert.Equal("driver.gilberto_ceara", vm.NamedSmgpRival?.DriverId);
            Assert.True(vm.SmgpRivalNamed);
            Assert.True(vm.BuildSmgpRival()!.Forced);
            // Declining is refused on a forced challenge.
            vm.SmgpDeclineRivalCommand.Execute(null);
            Assert.NotNull(vm.NamedSmgpRival);

            var view = new BriefingView { DataContext = vm };
            view.Measure(new Size(1000, 1200));
            view.Arrange(new Rect(0, 0, 1000, 1200));
            view.UpdateLayout();
            Assert.True(view.ActualHeight > 0);
        });
    }
}
