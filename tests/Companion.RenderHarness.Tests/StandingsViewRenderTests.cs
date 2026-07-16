using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Companion.App.Views;
using Companion.Core.Grid;
using Companion.Core.Numerics;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Services;
using Companion.ViewModels.Standings;

namespace Companion.RenderHarness.Tests;

/// <summary>
/// Real-WPF coverage for the modern championship screen. The narrow case reproduces the app's
/// minimum 920×620 window at 130% UI scale; dense statistics must become horizontally scrollable
/// instead of being clipped. The wide case proves the same surface expands without a dead 760px
/// column. Matrix navigation and the three keyboard-focusable tab segments are exercised too.
/// </summary>
public sealed class StandingsViewRenderTests
{
    [Theory]
    [InlineData(1600, 900, 1.0, false)]
    [InlineData(920, 620, 1.3, true)]
    public void ChampionshipTable_RespondsAtWideAndNarrow130Percent(
        double width, double height, double scale, bool expectsHorizontalScroll)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var fixture = Fixture.Create();
            var vm = new StandingsViewModel(fixture.Snapshots, fixture.Pack, session: fixture.Session)
            {
                ShowCountedColumn = true,
                ShowGrossColumn = true,
                ShowDroppedColumn = true,
                ShowPerRoundColumn = true,
            };
            var view = new StandingsView
            {
                DataContext = vm,
                LayoutTransform = new ScaleTransform(scale, scale),
            };

            using var host = Host.Show(view, width, height);

            Assert.True(view.ActualWidth > 0);
            Assert.True(view.ActualHeight > 0);
            Assert.Contains("CHAMPIONSHIP CONTROL", host.VisibleTexts());
            Assert.Contains("SCORING CONTROL", host.VisibleTexts());
            Assert.Contains("Mike Kobra", host.VisibleTexts());

            // The header legend plus the realised player/rival rows make identity explicit without
            // relying on colour alone — important in SMGP's dense live championship table.
            Assert.True(host.VisibleTexts().Count(t => t == "YOU") >= 2);
            Assert.True(host.VisibleTexts().Count(t => t == "RIVAL") >= 2);

            var tabs = Descendants<TabItem>(view).ToArray();
            Assert.Equal(3, tabs.Length);
            Assert.All(tabs, tab =>
            {
                Assert.True(tab.Focusable);
                Assert.True(tab.IsTabStop);
            });

            var scroller = Assert.IsType<ScrollViewer>(view.FindName("DriverTableScroller"));
            Assert.True(scroller.ActualWidth > 0);
            Assert.True(scroller.ViewportWidth > 0);
            if (expectsHorizontalScroll)
            {
                Assert.True(scroller.ScrollableWidth > 0);
                Assert.Equal(Visibility.Visible, scroller.ComputedHorizontalScrollBarVisibility);
            }
            else
            {
                Assert.Equal(0, scroller.ScrollableWidth, precision: 1);
            }
        });
    }

    [Fact]
    public void RoundMatrix_RendersDroppedScores_AndKeyboardReachableWhyTargets()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var fixture = Fixture.Create();
            var vm = new StandingsViewModel(fixture.Snapshots, fixture.Pack, session: fixture.Session)
            {
                SelectedTabIndex = 2,
            };
            var view = new StandingsView { DataContext = vm };
            using var host = Host.Show(view, 1180, 760);

            Assert.Contains("ROUND LEDGER", host.VisibleTexts());
            Assert.Contains("R1", host.VisibleTexts());
            Assert.Contains("R4", host.VisibleTexts());

            var matrixScroller = Assert.IsType<ScrollViewer>(view.FindName("RoundMatrixScroller"));
            Assert.True(matrixScroller.ActualWidth > 0);
            Assert.True(matrixScroller.ActualHeight > 0);

            var scoreTargets = Descendants<Button>(view)
                .Where(button => AutomationProperties.GetName(button).StartsWith(
                    "Inspect round score", StringComparison.Ordinal))
                .ToArray();
            Assert.NotEmpty(scoreTargets);
            Assert.All(scoreTargets, button =>
            {
                Assert.True(button.Focusable);
                Assert.True(button.IsEnabled);
                Assert.NotNull(button.Command);
            });

            // Latest best-3 snapshot drops round one for the player; the rendered cell keeps the
            // explicit strikethrough marker rather than communicating the rule by colour alone.
            Assert.Contains(Descendants<TextBlock>(view), text =>
                text.Text == "9" && text.TextDecorations.Count > 0);
        });
    }

    private sealed class Host : IDisposable
    {
        private readonly Window _window;
        private readonly StandingsView _view;

        private Host(Window window, StandingsView view)
        {
            _window = window;
            _view = view;
        }

        public static Host Show(StandingsView view, double width, double height)
        {
            var window = new Window
            {
                Content = view,
                Width = width,
                Height = height,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -10000,
                Top = -10000,
            };
            window.Show();
            window.UpdateLayout();
            WpfRenderHarness.Pump(DispatcherPriority.Loaded);
            window.UpdateLayout();
            WpfRenderHarness.Pump(DispatcherPriority.Render);
            return new Host(window, view);
        }

        public IReadOnlyList<string> VisibleTexts() => Descendants<TextBlock>(_view)
            .Where(IsEffectivelyVisible)
            .Select(text => text.Text)
            .Where(text => !string.IsNullOrEmpty(text))
            .ToArray();

        public void Dispose()
        {
            _window.Close();
            WpfRenderHarness.Pump(DispatcherPriority.Background);
        }

        private bool IsEffectivelyVisible(UIElement element)
        {
            for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is UIElement ui && ui.Visibility != Visibility.Visible)
                    return false;
                if (ReferenceEquals(node, _view))
                    break;
            }
            return true;
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private sealed record Fixture(
        SeasonPack Pack,
        IReadOnlyList<StandingsSnapshot> Snapshots,
        StandingsSession Session)
    {
        private static readonly string[] Names =
        [
            "Player Seat", "A. Senna", "G. Ceara", "P. Klinger", "R. Firenza", "B. Voden",
            "N. Corsa", "D. Velez", "M. Herbin", "S. Orbis", "T. Rigel", "C. Tegner",
        ];

        public static Fixture Create()
        {
            var pack = PackFixture();
            var snapshots = Enumerable.Range(1, 4).Select(Snapshot).ToArray();
            return new Fixture(pack, snapshots, new StandingsSession(pack, snapshots));
        }

        private static SeasonPack PackFixture() => new()
        {
            Manifest = new PackManifest
            {
                PackId = "smgp-render",
                Name = "Super Monaco GP",
                Version = "1.0.0",
                FormatVersion = 1,
            },
            Season = new SeasonDefinition
            {
                Year = 1989,
                SeriesName = "Super Monaco GP",
                Ams2Class = "F-Classic_Gen3",
                PointsSystem = new CatalogSeason
                {
                    RacePoints = [new(9), new(6), new(4), new(3), new(2), new(1)],
                    DriversBestN = new CatalogBestN { WholeSeason = 3 },
                    Constructors = new CatalogConstructors { BestCarOnly = true, BestN = "sameAsDrivers" },
                    SharedDrivePolicy = SharedDrivePolicy.Zero,
                },
                Rounds = Enumerable.Range(1, 4).Select(round => new PackRound
                {
                    Round = round,
                    Name = $"Grand Prix {round}",
                    Date = $"1989-0{round + 2}-01",
                    Track = new PackTrackRef { Id = $"track-{round}" },
                    Laps = 50,
                }).ToArray(),
            },
            Teams = Enumerable.Range(0, 6).Select(index => new PackTeam
            {
                Id = $"team.{index}",
                Name = $"Team {index + 1}",
                CarVehicleIds = ["car.render"],
            }).ToArray(),
            Drivers = Names.Select((name, index) => new PackDriver
            {
                Id = $"driver.{index}",
                Name = name,
                Ratings = new PackDriverRatings { RaceSkill = 0.9 - index * 0.01, QualifyingSkill = 0.9 - index * 0.01 },
            }).ToArray(),
            Entries = [],
        };

        private static StandingsSnapshot Snapshot(int afterRound) => new()
        {
            AfterRound = afterRound,
            Drivers = Enumerable.Range(0, Names.Length).Select(index => Driver(index, afterRound)).ToArray(),
            Constructors = Enumerable.Range(0, 6).Select(index => Constructor(index, afterRound)).ToArray(),
        };

        private static DriverStanding Driver(int index, int afterRound)
        {
            int pointsPerRound = Math.Max(0, 9 - index);
            bool dropped = afterRound == 4 && index % 4 == 0;
            int gross = pointsPerRound * afterRound;
            return new DriverStanding
            {
                DriverId = $"driver.{index}",
                Position = index + 1,
                GrossPoints = new Rational(gross),
                CountedPoints = new Rational(gross - (dropped ? pointsPerRound : 0)),
                RoundScores = Enumerable.Range(1, afterRound).Select(round => new RoundScore
                {
                    Round = round,
                    Points = new Rational(pointsPerRound),
                }).ToArray(),
                Dropped = dropped
                    ? [new DroppedResult { Round = 1, PointsDropped = new Rational(pointsPerRound) }]
                    : [],
            };
        }

        private static ConstructorStanding Constructor(int index, int afterRound)
        {
            int pointsPerRound = Math.Max(1, 12 - index);
            return new ConstructorStanding
            {
                ConstructorId = $"team.{index}",
                Position = index + 1,
                GrossPoints = new Rational(pointsPerRound * afterRound),
                CountedPoints = new Rational(pointsPerRound * afterRound),
                RoundScores = Enumerable.Range(1, afterRound).Select(round => new RoundScore
                {
                    Round = round,
                    Points = new Rational(pointsPerRound),
                }).ToArray(),
                Dropped = [],
            };
        }
    }

    private sealed class StandingsSession(
        SeasonPack pack,
        IReadOnlyList<StandingsSnapshot> snapshots) : ICareerSession
    {
        public SeasonPack Pack => pack;

        public CareerSummary Summary => new()
        {
            CareerName = "Render SMGP",
            SeasonYear = 1989,
            SeriesName = "Super Monaco GP",
            CurrentRound = 5,
            RoundCount = 16,
            PlayerDriverId = "driver.0",
            PlayerLiveryName = "Player car",
        };

        public (string DriverId, string DisplayName)? PlayerIdentity() => ("driver.0", "Mike Kobra");
        public string? CurrentSmgpRivalDriverId() => "driver.1";
        public JournalChain JournalFor(string entity, int? round = null) => new()
        {
            Entity = entity,
            Title = round is { } value ? $"Why round {value}" : "Why this championship total",
            Summary = "This number comes from the applied race ledger.",
            Contributions =
            [
                new JournalContribution { Label = "Race points", Detail = "Classified result", Value = "+9" },
            ],
        };

        public BriefingModel? CurrentBriefing() => null;
        public StageOutcome StageCurrentGrid() => new() { Success = true, Messages = [] };
        public IReadOnlyList<GridSeat> CurrentGrid() => [];
        public ConfirmModel Preview(ResultDraft draft) => new() { RoundPoints = [], Movements = [], Headline = "" };
        public void Apply(ResultDraft draft) { }
        public StandingsSnapshot? CurrentStandings() => snapshots[^1];
        public IReadOnlyList<StandingsSnapshot> AllSnapshots() => snapshots;
        public int? CurrentSliderRecommendation() => null;
        public SeasonReviewModel? SeasonReview() => null;
        public void AcceptOffer(string teamId) { }
    }
}
