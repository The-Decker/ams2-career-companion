using System.Windows;
using Companion.App.Views;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.RenderHarness.Tests;

/// <summary>Off-screen render of the Calendar tab: a real CalendarView over a real CalendarViewModel
/// whose fake session returns a mixed schedule (real venue / stand-in / applied alternate + an unused
/// alternate). Proves the per-round bindings, the kind DataTriggers, and every StaticResource resolve
/// without a render-time crash. Self-skips off Windows.</summary>
public sealed class CalendarRenderTests
{
    private sealed class CalendarSession : ICareerSession
    {
        public CareerSummary Summary { get; } = new()
        {
            CareerName = "Render", SeasonYear = 1978, SeriesName = "Test",
            CurrentRound = 1, RoundCount = 3, PlayerDriverId = "driver.player", PlayerLiveryName = "L",
        };

        public IReadOnlyList<SeasonScheduleEntry> SeasonSchedule() =>
        [
            new() { Round = 1, Name = "Monaco GP", Date = "1978-05-07", RealVenue = "Monaco",
                    Ams2TrackName = "Monaco", Laps = 78, Kind = SeasonTrackKind.RealVenue },
            new() { Round = 2, Name = "Belgian GP", Date = "1978-05-21", RealVenue = "Circuit Zolder",
                    Ams2TrackName = "Zolder", Laps = 74, Kind = SeasonTrackKind.Alternate,
                    CircuitLayoutId = "zolder", CircuitCaption = "Zolder · 4.01 km · 10 turns",
                    CircuitHistory = "Zolder hosted the Belgian GP 1973-1984." },
            new() { Round = 3, Name = "Dutch GP", Date = "1978-08-27", RealVenue = "Circuit Zandvoort",
                    Ams2TrackName = "Hockenheim 1988", Laps = 44, Kind = SeasonTrackKind.StandIn,
                    UnusedAlternateName = "Zolder" },
        ];

        public SeasonPack Pack { get; } = new()
        {
            Manifest = new PackManifest { PackId = "r", Name = "r", Version = "1", FormatVersion = 1 },
            Season = new SeasonDefinition
            {
                Year = 1978, SeriesName = "t", Ams2Class = "F-Vintage_Gen1",
                PointsSystem = new CatalogSeason { RacePoints = [new(9)] },
                Rounds = [new PackRound { Round = 1, Name = "R1", Date = "1978-05-07", Track = new PackTrackRef { Id = "monaco" }, Laps = 78 }],
            },
            Teams = [], Drivers = [], Entries = [],
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

    [Fact]
    public void CalendarView_RendersMixedSchedule()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new CalendarViewModel(new CalendarSession());
            Assert.Equal(3, vm.Rounds.Count);
            Assert.True(vm.HasUnusedAlternates);

            // Expand the round with circuit data so the original-circuit map + facts + history render.
            vm.Rounds[1].ToggleCommand.Execute(null);
            Assert.True(vm.Rounds[1].IsExpanded);

            var view = new CalendarView { DataContext = vm };
            view.Measure(new Size(1000, 900));
            view.Arrange(new Rect(0, 0, 1000, 900));
            view.UpdateLayout();

            Assert.True(view.ActualWidth > 0);
            Assert.True(view.ActualHeight > 0);
        });
    }
}
