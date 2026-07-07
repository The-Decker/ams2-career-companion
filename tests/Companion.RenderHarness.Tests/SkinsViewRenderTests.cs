using System.Windows;
using Companion.Ams2.Skins;
using Companion.App.Views;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen render of the Skins lens: a real SkinsView over a real SkinsViewModel whose
/// fake session returns a mixed skin picture (a custom-skin car, a default car, an unbound car with a
/// near-miss, and the player's own car). Exercises the player crib, the coloured status chips, the
/// copy buttons and the required-skin-packs block — the binding/crash surface a compiled-XAML build
/// cannot. Self-skips off Windows.</summary>
public sealed class SkinsViewRenderTests
{
    private sealed class SkinsSession : ICareerSession
    {
        public CareerSummary Summary { get; } = new()
        {
            CareerName = "Render Career",
            SeasonYear = 1969,
            SeriesName = "Formula One",
            CurrentRound = 1,
            RoundCount = 11,
            PlayerDriverId = "driver.player",
            PlayerLiveryName = "Player Livery",
        };

        public SeasonPack Pack { get; } = SkinPack();

        public SkinAssignmentPlan CurrentSkinAssignments() => new()
        {
            Ams2Class = "F-Vintage_Gen2",
            Assignments =
            [
                new SkinAssignment
                {
                    DriverName = "J. Brabham", TeamName = "Brabham-Ford", Number = "3",
                    LiveryName = "Brabham-Ford Cosworth #3 J. Brabham", IsPlayer = false,
                    Status = SkinStatus.CustomSkin, VehicleFolder = "brabham_bt26",
                },
                new SkinAssignment
                {
                    DriverName = "C. Amon", TeamName = "Ferrari", Number = "11",
                    LiveryName = "Ferrari #11 C. Amon", IsPlayer = false,
                    Status = SkinStatus.NameOnly,
                },
                new SkinAssignment
                {
                    DriverName = "Nobody", TeamName = "Privateer", Number = "99",
                    LiveryName = "typo #99 nobody", IsPlayer = false,
                    Status = SkinStatus.Unbound, NearMiss = "Typo #99 Nobody",
                },
                new SkinAssignment
                {
                    DriverName = "K. Acheson", TeamName = "Skoal Bandit", Number = "10",
                    LiveryName = "Skoal Bandit Formula 1 Team #10", IsPlayer = false,
                    Status = SkinStatus.InstalledInactive,
                },
                new SkinAssignment
                {
                    DriverName = "Nova Reyes", TeamName = "Lotus-Ford", Number = "1",
                    LiveryName = "Lotus-Ford Cosworth #1 G. Hill", IsPlayer = true,
                    Status = SkinStatus.CustomSkin, VehicleFolder = "lotus_49c",
                },
            ],
            InactiveLiveries = ["Skoal Bandit Formula 1 Team #10", "RAM #11 Winkelhock"],
            // A tiny cap so the over-cap warning panel + budget line both render (XAML validation).
            LiveryCap = 4,
        };

        public BriefingModel? CurrentBriefing() => null;
        public StageOutcome StageCurrentGrid() => new() { Success = true, Messages = [] };
        public IReadOnlyList<GridSeat> CurrentGrid() => [];
        public ConfirmModel Preview(ResultDraft draft) => new() { RoundPoints = [], Movements = [], Headline = "" };
        public void Apply(ResultDraft draft) { }
        public StandingsSnapshot? CurrentStandings() => null;
        public IReadOnlyList<StandingsSnapshot> AllSnapshots() => [];
        public int? CurrentSliderRecommendation() => null;
        public SeasonReviewModel? SeasonReview() => null;
        public void AcceptOffer(string teamId) { }
    }

    private static SeasonPack SkinPack() => new()
    {
        Manifest = new PackManifest
        {
            PackId = "render", Name = "Render", Version = "1.0.0", FormatVersion = 1,
            Requires = new PackRequirements
            {
                SkinPacks = [new PackSkinPackRequirement
                {
                    Name = "F1 1969 Season (Alain Fry)",
                    Url = "https://overtake.gg/example",
                    OverridesFolder = "F1_Season_1969",
                }],
            },
        },
        Season = new SeasonDefinition
        {
            Year = 1969, SeriesName = "Formula One", Ams2Class = "F-Vintage_Gen2",
            PointsSystem = new CatalogSeason { RacePoints = [new(9)] },
            Rounds =
            [
                new PackRound
                {
                    Round = 1, Name = "Test Grand Prix", Date = "1969-03-01",
                    Track = new PackTrackRef { Id = "test_track" }, Laps = 60,
                },
            ],
        },
        Teams = [],
        Drivers = [],
        Entries = [],
    };

    [Fact]
    public void SkinsView_RendersThePlayerCribAndCarStatuses()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new SkinsViewModel(new SkinsSession());
            Assert.True(vm.HasPlayerCar);
            Assert.Equal(5, vm.Cars.Count);
            Assert.True(vm.HasUnbound);
            Assert.True(vm.HasActivatable);
            Assert.Equal(2, vm.ActivatableLiveries.Count);
            Assert.Single(vm.RequiredSkinPacks);

            var view = new SkinsView { DataContext = vm };
            view.Measure(new Size(1000, 900));
            view.Arrange(new Rect(0, 0, 1000, 900));
            view.UpdateLayout();

            Assert.True(view.ActualWidth > 0);
            Assert.True(view.ActualHeight > 0);
        });
    }
}
