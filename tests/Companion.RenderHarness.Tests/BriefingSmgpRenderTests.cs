using System.Windows;
using Companion.App.Views;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Briefing;
using Companion.ViewModels.Services;

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
            PointsLine = "9 D.P.",
            AdviceLine = "PASS THE CARS AT THE HAIRPIN TURN!",
            Titles = 0,
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
    public void BriefingView_RendersTheRivalPanel_WithADossierCard()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new BriefingViewModel(new SmgpSession());
            Assert.True(vm.SmgpActive);
            Assert.True(vm.SmgpPickEnabled);          // a free pick
            Assert.Null(vm.SelectedSmgpRival);        // fresh round: unnamed

            // Name a rival so the dossier card + ladder telegraph + swap answer all render.
            vm.SelectedSmgpRival = vm.SmgpRivals[0];
            Assert.True(vm.SmgpSwapPromptVisible);
            Assert.NotNull(vm.BuildSmgpRival());
            Assert.True(vm.BuildSmgpRival()!.SeatSwapAccepted);

            var view = new BriefingView { DataContext = vm };
            view.Measure(new Size(1000, 1200));
            view.Arrange(new Rect(0, 0, 1000, 1200));
            view.UpdateLayout();

            Assert.True(view.ActualWidth > 0);
            Assert.True(view.ActualHeight > 0);

            // Pin the ACTUAL visibility (a Bool converter inversion renders fine and passes any
            // height assertion — this is the check that catches it).
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("SmgpPanel")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("SmgpCareerOverPanel")).Visibility);
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
            // The forced challenger is pre-named and the call marks it forced.
            Assert.Equal("driver.gilberto_ceara", vm.SelectedSmgpRival?.DriverId);
            Assert.True(vm.BuildSmgpRival()!.Forced);
            // Declining is refused on a forced challenge.
            vm.SmgpDeclineRivalCommand.Execute(null);
            Assert.NotNull(vm.SelectedSmgpRival);

            var view = new BriefingView { DataContext = vm };
            view.Measure(new Size(1000, 1200));
            view.Arrange(new Rect(0, 0, 1000, 1200));
            view.UpdateLayout();
            Assert.True(view.ActualHeight > 0);
        });
    }
}
