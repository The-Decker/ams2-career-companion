using System.Windows;
using Companion.App.Views;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Briefing;
using Companion.ViewModels.Services;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen render of the sectioned Race Day briefing: a real BriefingView over a real
/// BriefingViewModel whose fake session returns a multi-section checklist (Event / Practice /
/// Qualifying / Race / Rules, with repeated per-session "Weather slot 1" labels) plus a fuel note.
/// Proves the nested Sections/Items ItemsControl bindings + the section-header StringVisible binding
/// resolve and the fuel panel renders — the view-layer surface a compiled-XAML build cannot check.
/// Self-skips off Windows.</summary>
public sealed class BriefingSectionRenderTests
{
    private sealed class SectionSession : ICareerSession
    {
        public CareerSummary Summary { get; } = new()
        {
            CareerName = "Render Career",
            SeasonYear = 1967,
            SeriesName = "Test Championship",
            CurrentRound = 1,
            RoundCount = 11,
            PlayerDriverId = "driver.player",
            PlayerLiveryName = "Player Livery",
        };

        public BriefingModel? CurrentBriefing() => new()
        {
            Round = new PackRound
            {
                Round = 1,
                Name = "Test Grand Prix",
                Date = "1967-01-02",
                Track = new PackTrackRef { Id = "test_track" },
                Laps = 80,
            },
            VenueDisplayName = "Test Circuit",
            IsPlaceholder = false,
            Settings =
            [
                new CopyableSetting("Track", "test_track") { Section = "Event" },
                new CopyableSetting("Class", "F-Vintage_Gen1") { Section = "Event" },
                new CopyableSetting("Opponents", "10") { Section = "Event" },
                new CopyableSetting("Duration", "60 min") { Section = "Practice" },
                new CopyableSetting("Weather slot 1", "Clear") { Section = "Practice" },
                new CopyableSetting("Duration", "60 min") { Section = "Qualifying" },
                new CopyableSetting("Weather slot 1", "Clear") { Section = "Qualifying" },
                new CopyableSetting("Laps", "80") { Section = "Race" },
                new CopyableSetting("Weather slot 1", "Light Rain") { Section = "Race" },
                new CopyableSetting("Mandatory pit stop", "No") { Section = "Rules" },
                new CopyableSetting("Refuelling", "No") { Section = "Rules" },
            ],
            FuelNote = "⛽ 80 laps is beyond the ~58-lap range of the ~190 L tank — save fuel.",
        };

        public SeasonPack Pack { get; } = SectionPack();

        // A circuit with facts so the right-column panel (scaling map Viewbox + FUN FACTS list)
        // renders; the geometry converter degrades to no path data for the unknown layout id.
        public HistoricalSeason? HistoricalSeason(int year) => new()
        {
            Year = year,
            Rounds =
            [
                new HistoricalRound
                {
                    Round = 1, Name = "Test Grand Prix",
                    Circuit = new HistoricalCircuit
                    {
                        LayoutId = "render-layout", Name = "Kyalami", Place = "Midrand",
                        LengthKm = "4.10", Turns = 10,
                        History = "The Kyalami circuit in Midrand.",
                        Facts = ["Hosts its first World Championship Grand Prix this season."],
                    },
                },
            ],
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

    private static SeasonPack SectionPack() => new()
    {
        Manifest = new PackManifest { PackId = "render", Name = "Render", Version = "1.0.0", FormatVersion = 1 },
        Season = new SeasonDefinition
        {
            Year = 1967,
            SeriesName = "Test",
            Ams2Class = "F-Vintage_Gen1",
            RefuellingAllowed = false,
            PointsSystem = new CatalogSeason { RacePoints = [new(9)] },
            Rounds =
            [
                new PackRound
                {
                    Round = 1, Name = "Test Grand Prix", Date = "1967-01-02",
                    Track = new PackTrackRef { Id = "test_track" }, Laps = 80,
                },
            ],
        },
        Teams = [],
        Drivers = [],
        Entries = [],
    };

    [Fact]
    public void BriefingView_RendersSectionedChecklist_AndFuelNote()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new BriefingViewModel(new SectionSession());

            // Sectioning is live: five groups, and same-label rows in different sessions are
            // independently tickable (composite key), never cross-ticked.
            Assert.Equal(11, vm.Settings.Count);
            Assert.NotNull(vm.FuelNote);
            var practiceWeather = vm.Settings.First(s => s.Section == "Practice" && s.Label == "Weather slot 1");
            var raceWeather = vm.Settings.First(s => s.Section == "Race" && s.Label == "Weather slot 1");
            practiceWeather.IsChecked = true;
            Assert.False(raceWeather.IsChecked);

            Assert.True(vm.HasCircuitFacts); // the fake circuit's facts reached the VM

            // The two-column layout must survive the responsive extremes: the 920 minimum, the old
            // default, and a 2560 monitor (star columns + proportional caps, no fixed 860 wall).
            var view = new BriefingView { DataContext = vm };
            foreach (double width in new[] { 920.0, 1000.0, 2560.0 })
            {
                view.Measure(new Size(width, 1400));
                view.Arrange(new Rect(0, 0, width, 1400));
                view.UpdateLayout();

                Assert.True(view.ActualWidth > 0);
                Assert.True(view.ActualHeight > 0);
            }

            // 510 ≈ the briefing viewport at 130% UI scale on a 920px window (920/1.3 − rail −
            // margins). The right column (circuit panel, fuel, gamble buttons) must stay INSIDE
            // the viewport — a fixed 520 left-column floor used to shove it out of reach.
            view.Measure(new Size(510, 1400));
            view.Arrange(new Rect(0, 0, 510, 1400));
            view.UpdateLayout();
            var circuitPanel = (FrameworkElement?)view.FindName("CircuitPanel");
            Assert.NotNull(circuitPanel);
            Assert.True(circuitPanel!.ActualWidth > 80,
                $"right column collapsed: circuit panel is {circuitPanel.ActualWidth:0} wide at a 510 viewport");
            double rightEdge = circuitPanel.TransformToAncestor(view).Transform(new Point(0, 0)).X
                               + circuitPanel.ActualWidth;
            Assert.True(rightEdge <= 510.5,
                $"right column clipped outside the 510 viewport (right edge {rightEdge:0})");
        });
    }
}
