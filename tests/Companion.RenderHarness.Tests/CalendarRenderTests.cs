using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private const double TrackBannerAspect = 440d / 1920d;

    private sealed class CalendarSession : ICareerSession
    {
        public CareerSummary Summary { get; } = new()
        {
            CareerName = "Render", SeasonYear = 1978, SeriesName = "Test",
            CurrentRound = 1, RoundCount = 3, PlayerDriverId = "driver.player", PlayerLiveryName = "L",
        };

        public IReadOnlyList<SeasonScheduleEntry> SeasonSchedule() =>
        [
            new() { Round = 1, Name = "San Marino GP", Date = "1988-05-01",
                    RealVenue = "Autodromo Internazionale Enzo e Dino Ferrari",
                    TrackId = "imola_88", Ams2TrackName = "Imola_GP_1988", Laps = 61,
                    Kind = SeasonTrackKind.RealVenue,
                    PlayerStatus = SchedulePlayerStatus.SatOut },
            new() { Round = 2, Name = "Belgian GP", Date = "1978-05-21", RealVenue = "Circuit Zolder",
                    TrackId = "Heusden", Ams2TrackName = "Zolder", Laps = 74, Kind = SeasonTrackKind.Alternate,
                    CircuitLayoutId = "zolder", CircuitCaption = "Zolder · 4.01 km · 10 turns",
                    CircuitHistory = "Zolder hosted the Belgian GP 1973-1984.",
                    CircuitFacts = ["Most wins here: Niki Lauda (2).", "Home-crowd wins: 1."],
                    PlayerStatus = SchedulePlayerStatus.WillMiss },
            new() { Round = 3, Name = "Dutch GP", Date = "1978-08-27", RealVenue = "Circuit Zandvoort",
                    TrackId = "hockenheim_1988", Ams2TrackName = "Hockenheim 1988", Laps = 44,
                    Kind = SeasonTrackKind.StandIn,
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
            Assert.Equal("SAT OUT, injured", vm.Rounds[0].PlayerStatusLabel);
            Assert.Equal("WILL MISS, injured", vm.Rounds[1].PlayerStatusLabel);
            Assert.True(vm.Rounds[0].HasPlayerStatus);
            Assert.True(vm.Rounds[1].HasPlayerStatus);
            Assert.False(vm.Rounds[2].HasPlayerStatus);

            // Expand the round with circuit data so the original-circuit map + facts + history render.
            vm.Rounds[1].ToggleCommand.Execute(null);
            Assert.True(vm.Rounds[1].IsExpanded);

            var view = new CalendarView { DataContext = vm };
            view.Measure(new Size(1000, 900));
            view.Arrange(new Rect(0, 0, 1000, 900));
            view.UpdateLayout();

            Assert.True(view.ActualWidth > 0);
            Assert.True(view.ActualHeight > 0);
            Assert.True(CountText(view, "SAT OUT, injured") >= 1);
            Assert.True(CountText(view, "WILL MISS, injured") >= 1);
        });
    }

    /// <summary>The season board: every round is a card in the 4-column grid, its full name shown
    /// once, and the round that carries circuit data shows its map caption + fun facts inline (no
    /// lazy expand, no separate overview chips).</summary>
    [Fact]
    public void CalendarView_RendersEveryRoundAsACard_WithCircuitDataInline()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new CalendarViewModel(new CalendarSession());

            var view = new CalendarView { DataContext = vm };
            view.Measure(new Size(1200, 2000));
            view.Arrange(new Rect(0, 0, 1200, 2000));
            view.UpdateLayout();

            // Every round's FULL name renders exactly once (there are no short-name chips now).
            Assert.Equal(1, CountText(view, "San Marino GP"));
            Assert.Equal(1, CountText(view, "Belgian GP"));
            Assert.Equal(1, CountText(view, "Dutch GP"));

            // The round with circuit data shows its map caption inline (no expand needed).
            Assert.Equal(1, CountText(view, "Zolder · 4.01 km · 10 turns"));
        });
    }

    [Fact]
    public void CalendarView_BindsDrivenTrackIdToEmbeddedRoundHero()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new CalendarViewModel(new CalendarSession());
            var view = new CalendarView { DataContext = vm };
            var roundList = Assert.IsType<ListBox>(view.FindName("RoundList"));
            roundList.SelectedItem = vm.Rounds[0];

            view.Measure(new Size(1200, 900));
            view.Arrange(new Rect(0, 0, 1200, 900));
            view.UpdateLayout();

            Assert.Equal("imola_88", vm.Rounds[0].TrackId);
            var hero = Assert.Single(Descendants<Image>(view), image => image.Name == "RoundHeroArt");
            var bitmap = Assert.IsAssignableFrom<BitmapSource>(hero.Source);
            Assert.Equal(1920, bitmap.PixelWidth);
            Assert.Equal(440, bitmap.PixelHeight);
            Assert.Equal(Visibility.Visible, hero.Visibility);
        });
    }

    [Fact]
    public void CalendarHero_UsesFullPanoramicAspect_WhenDetailColumnIsWide()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new CalendarViewModel(new CalendarSession());
            var view = new CalendarView { DataContext = vm };
            var roundList = Assert.IsType<ListBox>(view.FindName("RoundList"));
            roundList.SelectedItem = vm.Rounds[0];

            view.Measure(new Size(2048, 1200));
            view.Arrange(new Rect(0, 0, 2048, 1200));
            view.UpdateLayout();

            var frame = Assert.Single(
                Descendants<Border>(view),
                border => border.Name == "RoundHeroFrame");
            var hero = Assert.Single(
                Descendants<Image>(view),
                image => image.Name == "RoundHeroArt");

            Assert.True(frame.ActualHeight > 300,
                $"A wide Calendar should grow beyond the 244px compact floor; got {frame.ActualHeight:0.0}px.");
            Assert.True(Math.Abs(frame.ActualHeight - frame.ActualWidth * TrackBannerAspect) <= 1.5,
                $"Calendar hero should preserve 1920:440: width {frame.ActualWidth:0.0} -> expected " +
                $"{frame.ActualWidth * TrackBannerAspect:0.0}, got {frame.ActualHeight:0.0}.");
            Assert.Equal(Stretch.Uniform, hero.Stretch);
        });
    }

    private static int CountText(DependencyObject root, string text) =>
        Descendants<System.Windows.Controls.TextBlock>(root).Count(tb => tb.Text == text);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }
}
