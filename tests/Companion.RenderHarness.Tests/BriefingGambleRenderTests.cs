using System.Windows;
using Companion.App.Views;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Briefing;
using Companion.ViewModels.Services;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen render of the Race Day briefing with the Setup Gamble panel live (4b): a real
/// BriefingView over a real BriefingViewModel whose fake session reports an expected finish, so the
/// "call your shot" panel is visible and its command buttons + stake preview actually render. Guards
/// the view layer a compiled-XAML build cannot (binding paths, no render-time crash). Self-skips off
/// Windows.</summary>
public sealed class BriefingGambleRenderTests
{
    private sealed class GambleSession : ICareerSession
    {
        public CareerSummary Summary { get; } = new()
        {
            CareerName = "Render Career",
            SeasonYear = 1988,
            SeriesName = "Test Championship",
            CurrentRound = 1,
            RoundCount = 16,
            PlayerDriverId = "driver.player",
            PlayerLiveryName = "Player Livery",
        };

        public BriefingModel? CurrentBriefing() => new()
        {
            Round = new PackRound
            {
                Round = 1,
                Name = "Test Grand Prix",
                Date = "1988-04-03",
                Track = new PackTrackRef { Id = "test_track" },
                Laps = 60,
            },
            VenueDisplayName = "Test Circuit",
            IsPlaceholder = false,
            Settings = [new CopyableSetting("Track", "test_track"), new CopyableSetting("Laps", "60")],
            RecommendedSlider = 95,
        };

        public SeasonPack Pack { get; } = TestBriefingPack();
        public StageOutcome StageCurrentGrid() => new() { Success = true, Messages = ["staged"] };
        public IReadOnlyList<GridSeat> CurrentGrid() => [];
        public int? CurrentExpectedFinish() => 8; // a mid-grid expectation → the gamble panel shows
        public ConfirmModel Preview(ResultDraft draft) => new() { RoundPoints = [], Movements = [], Headline = "" };
        public void Apply(ResultDraft draft) { }
        public StandingsSnapshot? CurrentStandings() => null;
        public IReadOnlyList<StandingsSnapshot> AllSnapshots() => [];
        public int? CurrentSliderRecommendation() => 95;
        public SeasonReviewModel? SeasonReview() => null;
        public void AcceptOffer(string teamId) { }
    }

    private static SeasonPack TestBriefingPack() => new()
    {
        Manifest = new PackManifest { PackId = "render", Name = "Render", Version = "1.0.0", FormatVersion = 1 },
        Season = new SeasonDefinition
        {
            Year = 1988,
            SeriesName = "Test",
            Ams2Class = "F-Classic_Gen2",
            PointsSystem = new Companion.Core.Scoring.CatalogSeason { RacePoints = [new(9)] },
            Rounds =
            [
                new PackRound
                {
                    Round = 1, Name = "Test Grand Prix", Date = "1988-04-03",
                    Track = new PackTrackRef { Id = "test_track" }, Laps = 60,
                },
            ],
        },
        Teams = [],
        Drivers = [],
        Entries = [],
    };

    [Fact]
    public void BriefingView_RendersTheGamblePanel_AndACommittedCall()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new BriefingViewModel(new GambleSession());
            Assert.True(vm.CanGamble); // the panel is shown

            // Commit a bold call so the stake-preview branch + the "No bet" button both render.
            vm.CallBolderCommand.Execute(null);
            Assert.True(vm.HasCalledShot);

            var view = new BriefingView { DataContext = vm };
            view.Measure(new Size(1000, 900));
            view.Arrange(new Rect(0, 0, 1000, 900));
            view.UpdateLayout();

            Assert.True(view.ActualWidth > 0);
            Assert.True(view.ActualHeight > 0);
        });
    }
}
