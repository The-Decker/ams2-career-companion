using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Companion.App.Views;
using Companion.Core.Grid;
using Companion.Core.HistoryArchive;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Core.Smgp;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.RenderHarness.Tests;

/// <summary>
/// Real-WPF coverage for the History tab's encyclopedia surface: the featured trio, era and
/// subject browsers, record books, the verified timeline, the real-history-vs-this-universe
/// comparison, and the unified archive search. The fixture is entirely read-side data — it
/// never applies a result, creates a fold, or touches the deterministic news generator.
/// </summary>
public sealed class HistoryEncyclopediaRenderTests
{
    [Theory]
    [InlineData(1600, 900)]
    [InlineData(920, 620)]
    public void PopulatedEncyclopedia_RendersHeadedSectionsAndDivergence_WithoutHorizontalScroll(
        double width, double height)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new HistoryViewModel(EncyclopediaSession.Populated());
            var view = new HistoryView { DataContext = vm };
            using var host = Host.Show(view, width, height);

            string[] texts = host.VisibleTexts().ToArray();

            // The encyclopedia block and every headed sub-section render from the bound data.
            Assert.Contains("THE ENCYCLOPEDIA", texts);
            Assert.Contains("MOTORSPORT REFERENCE ARCHIVE", texts);
            Assert.Contains("VERIFIED REFERENCE DATA", texts);
            Assert.Contains("SEARCH THE ARCHIVE", texts);
            Assert.Contains("FEATURED", texts);
            Assert.Contains("rotates daily", texts);
            Assert.Contains("BROWSE THE ERAS", texts);
            Assert.Contains("TECHNOLOGY, REGULATION AND SAFETY", texts);
            Assert.Contains("THE RECORD BOOKS", texts);
            Assert.Contains("VERIFIED TIMELINE", texts);

            // The featured trio is the deterministic date-aware rotation — always populated here.
            Assert.NotNull(vm.FeaturedEra);
            Assert.NotNull(vm.FeaturedDriver);
            Assert.NotNull(vm.FeaturedTeam);
            Assert.Contains("FEATURED ERA", texts);
            Assert.Contains("FEATURED DRIVER", texts);
            Assert.Contains("FEATURED TEAM", texts);

            // Era + subject headers are visible collapsed; an incomplete subject says so via
            // its text chip (never colour alone).
            Assert.Contains("The Golden Era", texts);
            Assert.Contains("Ground Effect", texts);
            Assert.Contains("PARTIALLY DOCUMENTED", texts);

            // Record books: driver titles use the honest text label; the circuit card shows only
            // its first two facts.
            Assert.Contains("World Champion 1963, 1965", texts);
            Assert.Contains("Silverwood Park", texts);
            Assert.DoesNotContain("A third fact the card never shows", texts);

            // The divergence panel labels both sides and flags the alternate outcome by text.
            Assert.Contains("REAL HISTORY VS THIS UNIVERSE", texts);
            Assert.Contains("HISTORICAL RECORD", texts);
            Assert.Contains("THIS UNIVERSE", texts);
            Assert.Contains("ALTERNATE OUTCOME", texts);
            Assert.Contains("TWO RECORDS / NEVER MERGED", texts);
            Assert.Contains("LOCKED", texts);
            Assert.Contains("VS", texts);
            Assert.Contains(texts, text => text.StartsWith("Divergence point:", StringComparison.Ordinal));

            var masthead = Assert.IsType<Border>(view.FindName("EncyclopediaMasthead"));
            Assert.Equal("Motorsport encyclopedia masthead", AutomationProperties.GetName(masthead));
            var eraArt = Assert.IsType<Grid>(view.FindName("FeaturedEraArtwork"));
            Assert.InRange(eraArt.ActualHeight, 95, 97);

            // Every comparison row repeats two independently boxed and named surfaces with a
            // visible separator. The labels and borders survive the minimum viewport, so the
            // career universe can never be visually mistaken for the source record.
            var divergenceRows = Assert.IsType<ItemsControl>(view.FindName("DivergenceRowsList"));
            Assert.True(VirtualizingPanel.GetIsVirtualizing(divergenceRows));
            Assert.Equal(3, divergenceRows.Items.Count);
            Border[] historicalColumns = Descendants<Border>(divergenceRows)
                .Where(border => AutomationProperties.GetName(border) == "Historical record column")
                .ToArray();
            Border[] universeColumns = Descendants<Border>(divergenceRows)
                .Where(border => AutomationProperties.GetName(border) == "This universe column")
                .ToArray();
            Assert.Equal(3, historicalColumns.Length);
            Assert.Equal(3, universeColumns.Length);
            Assert.All(historicalColumns, column => Assert.True(column.ActualWidth > 120));
            Assert.All(universeColumns, column => Assert.True(column.ActualWidth > 120));
            Assert.NotEqual(
                historicalColumns[0].Background?.ToString(),
                universeColumns[0].Background?.ToString());

            // The ~90-entry timeline is virtualized inside a bounded viewport: the first year is
            // realized, the last is not (its container is never created).
            var timeline = Assert.IsType<ItemsControl>(view.FindName("VerifiedTimelineList"));
            Assert.True(VirtualizingPanel.GetIsVirtualizing(timeline));
            Assert.Equal(90, timeline.Items.Count);
            Assert.Contains("1950", texts);
            Assert.DoesNotContain("2039", texts);

            // The page itself never scrolls horizontally.
            var scroll = Assert.IsType<ScrollViewer>(view.FindName("HistoryScroll"));
            Assert.True(scroll.ScrollableHeight > 0);
            Assert.Equal(0, scroll.ScrollableWidth, precision: 1);
            Assert.Equal(Visibility.Collapsed, scroll.ComputedHorizontalScrollBarVisibility);
        });
    }

    [Fact]
    public void ArchiveSearch_IsKeyboardReachable_ShowsMatchReasons_AndAnHonestEmptyState()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new HistoryViewModel(EncyclopediaSession.Populated());
            var view = new HistoryView { DataContext = vm };
            using var host = Host.Show(view, 1100, 760);

            var search = Assert.IsType<TextBox>(view.FindName("EncyclopediaSearchBox"));
            Assert.True(search.Focusable);
            Assert.True(search.IsTabStop);
            Assert.Equal("Search the encyclopedia and news archive", AutomationProperties.GetName(search));

            // A real query: the circuit title matches and the result says WHY it matched.
            search.Text = "Silverwood";
            search.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            host.Layout();

            Assert.True(vm.HasArchiveSearchResults);
            Assert.False(vm.IsArchiveSearchEmpty);
            var results = Assert.IsType<ItemsControl>(view.FindName("ArchiveSearchResultsList"));
            Assert.Equal(Visibility.Visible, results.Visibility);
            Assert.Contains("matched: title", host.VisibleTexts());
            Assert.Contains("circuit", host.VisibleTexts());

            // A query nothing matches: the section says so instead of showing silence.
            search.Text = "zzzz-nothing-here";
            search.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            host.Layout();

            Assert.False(vm.HasArchiveSearchResults);
            Assert.True(vm.IsArchiveSearchEmpty);
            Assert.Equal(Visibility.Collapsed, results.Visibility);
            Assert.Contains("No matches in the archive.", host.VisibleTexts());
        });
    }

    [Fact]
    public void EmptyReferenceData_CollapsesEncyclopediaAndDivergence_Honestly()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new HistoryViewModel(EncyclopediaSession.WithoutReferenceData());
            var view = new HistoryView { DataContext = vm };
            using var host = Host.Show(view, 1100, 760);

            Assert.False(vm.HasEncyclopedia);
            Assert.False(vm.HasDivergence);

            var encyclopedia = Assert.IsType<StackPanel>(view.FindName("EncyclopediaSection"));
            Assert.Equal(Visibility.Collapsed, encyclopedia.Visibility);
            var divergence = Assert.IsType<Border>(view.FindName("DivergencePanel"));
            Assert.Equal(Visibility.Collapsed, divergence.Visibility);

            string[] texts = host.VisibleTexts().ToArray();
            Assert.DoesNotContain("THE ENCYCLOPEDIA", texts);
            Assert.DoesNotContain("SEARCH THE ARCHIVE", texts);
            Assert.DoesNotContain("REAL HISTORY VS THIS UNIVERSE", texts);

            // The rest of the archive keeps working untouched.
            Assert.Contains("HISTORY ARCHIVE", texts);
            Assert.Contains("RECORDS AND MILESTONES", texts);
        });
    }

    private sealed class Host : IDisposable
    {
        private readonly Window _window;
        private readonly HistoryView _view;

        private Host(Window window, HistoryView view)
        {
            _window = window;
            _view = view;
        }

        public static Host Show(HistoryView view, double width, double height)
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

        public void Layout()
        {
            _window.UpdateLayout();
            WpfRenderHarness.Pump(DispatcherPriority.Render);
            WpfRenderHarness.Pump();
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
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (T descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    /// <summary>The stand-in session: the same shape as HistoryArchiveRenderTests' fixture plus
    /// the encyclopedia archive index and a season divergence report — all read-side data.</summary>
    private sealed class EncyclopediaSession : ICareerSession
    {
        public required CareerSummary SummaryValue { get; init; }
        public required CareerTimeline Timeline { get; init; }
        public HistoryArchiveIndex Archive { get; init; } = HistoryArchiveIndex.Empty;
        public SeasonDivergenceReport? Divergence { get; init; }

        public CareerSummary Summary => SummaryValue;
        public SeasonPack Pack { get; } = MinimalPack();

        public CareerTimeline CareerTimeline() => Timeline;
        public HistoryArchiveIndex HistoryArchive() => Archive;
        public SeasonDivergenceReport? SeasonDivergence(int seasonOrdinal) =>
            seasonOrdinal == Timeline.Seasons.Count ? Divergence : null;

        public BriefingModel? CurrentBriefing() => null;
        public StageOutcome StageCurrentGrid() => new() { Success = true, Messages = [] };
        public IReadOnlyList<GridSeat> CurrentGrid() => [];
        public ConfirmModel Preview(ResultDraft draft) =>
            new() { RoundPoints = [], Movements = [], Headline = "" };
        public void Apply(ResultDraft draft) { }
        public StandingsSnapshot? CurrentStandings() => null;
        public IReadOnlyList<StandingsSnapshot> AllSnapshots() => [];
        public int? CurrentSliderRecommendation() => null;
        public SeasonReviewModel? SeasonReview() => null;
        public void AcceptOffer(string teamId) { }

        public static EncyclopediaSession Populated() => new()
        {
            SummaryValue = SummaryFor(round: 8, position: 2),
            Timeline = PopulatedTimeline(),
            Archive = BuildArchive(),
            Divergence = new SeasonDivergenceReport
            {
                SeasonYear = 1991,
                SeasonOrdinal = 1,
                Rounds =
                [
                    new RoundDivergence
                    {
                        Round = 1,
                        Venue = "Phoenix",
                        Kind = DivergenceKind.UnchangedEvent,
                        HistoricalWinner = "A. Senna",
                        HistoricalWinnerTeam = "McLaren",
                        CareerWinner = "A. Senna",
                        CareerWinnerTeam = "McLaren",
                    },
                    new RoundDivergence
                    {
                        Round = 2,
                        Venue = "Interlagos",
                        Kind = DivergenceKind.AlternateOutcome,
                        HistoricalWinner = "A. Senna",
                        HistoricalWinnerTeam = "McLaren",
                        CareerWinner = "Mike Racer",
                        CareerWinnerTeam = "Bullets",
                        NonHistoricalWinner = true,
                    },
                    new RoundDivergence
                    {
                        Round = 3,
                        Venue = "Imola",
                        Kind = DivergenceKind.NotYetRaced,
                        HistoricalWinner = "A. Senna",
                        HistoricalWinnerTeam = "McLaren",
                    },
                ],
                HistoricalChampion = "A. Senna",
                HistoricalChampionTeam = "McLaren",
                CareerChampion = "Mike Racer",
                ChampionChanged = true,
            },
        };

        public static EncyclopediaSession WithoutReferenceData() => new()
        {
            SummaryValue = SummaryFor(round: 8, position: 2),
            Timeline = PopulatedTimeline(),
            Archive = HistoryArchiveIndex.Empty,
            Divergence = null,
        };

        private static CareerTimeline PopulatedTimeline() => new()
        {
            Seasons = [Season(1991)],
            Records = new CareerRecordsBook
            {
                Wins = 3,
                Podiums = 7,
                TotalPoints = 72,
            },
        };

        private static HistoryArchiveIndex BuildArchive()
        {
            var reference = new HistoryArchiveData
            {
                Eras =
                [
                    new HistoricalEra
                    {
                        Key = "golden",
                        Name = "The Golden Era",
                        FromYear = 1950,
                        ToYear = 1969,
                        Overview = "Front-engined pioneers race on public roads and airfields.",
                        DefiningCharacteristics =
                            ["Front-engined grand prix cars", "Privateer entries share the grid"],
                        EngineTrends = "Naturally aspirated engines dominate.",
                        SafetyContext = "Circuits run between trees and houses; protection is primitive.",
                        RegulationChanges = ["1958: the constructors' championship is introduced"],
                        Legacy = "The founding mythology of the world championship.",
                        Sources = ["f1db"],
                    },
                    new HistoricalEra
                    {
                        Key = "wings",
                        Name = "The Wing Era",
                        FromYear = 1970,
                        ToYear = 1994,
                        Overview = "Aerodynamics and sponsorship remake the sport.",
                        Sources = ["f1db"],
                    },
                ],
                Subjects =
                [
                    new HistorySubject
                    {
                        Id = "ground-effect",
                        Title = "Ground Effect",
                        Category = "technology",
                        Summary = "Sculpted underbodies glue cars to the road.",
                        Body = ["Venturi tunnels arrive in the late 1970s.", "Sliding skirts are banned soon after."],
                        FromYear = 1977,
                        ToYear = 1982,
                        IsComplete = true,
                        Sources = ["f1db"],
                    },
                    new HistorySubject
                    {
                        Id = "barriers",
                        Title = "Barrier Design",
                        Category = "safety",
                        Summary = "How circuits learned to absorb impacts.",
                        Body = ["From straw bales to guardrail to energy-absorbing walls."],
                        FromYear = 1952,
                        IsComplete = false,
                        Sources = ["f1db"],
                    },
                ],
                Teams = [],
            };

            var timeline = new List<HistoryTimelineEntry>();
            for (var year = 1950; year <= 2039; year++)
            {
                timeline.Add(new HistoryTimelineEntry
                {
                    Year = year,
                    Category = "championship",
                    Title = $"{year}: documented champion",
                    Summary = "The verified season record.",
                    Provenance = "verifiedHistorical",
                    RelatedKey = $"season:{year}",
                });
            }

            return new HistoryArchiveIndex
            {
                Drivers =
                [
                    new DriverHistoryProfile
                    {
                        Name = "Jim Clocker",
                        FirstYear = 1960,
                        LastYear = 1968,
                        SeasonsEntered = 9,
                        Starts = 72,
                        Wins = 25,
                        Podiums = 32,
                        FastestLaps = 27,
                        ChampionshipYears = [1963, 1965],
                        Stints = [new DriverTeamStint("Lotus Racing", 1960, 1968)],
                    },
                    new DriverHistoryProfile
                    {
                        Name = "Ronnie Steady",
                        FirstYear = 1970,
                        LastYear = 1978,
                        SeasonsEntered = 9,
                        Starts = 123,
                        Wins = 10,
                        Podiums = 26,
                        FastestLaps = 14,
                    },
                ],
                Teams =
                [
                    new TeamHistoryProfile
                    {
                        Canonical = "Lotus Racing",
                        FirstYear = 1958,
                        LastYear = 1994,
                        Wins = 79,
                        DriversFielded = 58,
                        ConstructorsChampionshipYears = [1963, 1965, 1970],
                        Lineage = [new TeamLineageLink { RelatedTo = "Team Chapman", Relationship = "renamed" }],
                        IsComplete = true,
                    },
                    new TeamHistoryProfile
                    {
                        Canonical = "Scuderia Mystery",
                        FirstYear = 1971,
                        LastYear = 1973,
                        Wins = 0,
                        DriversFielded = 4,
                        IsComplete = false,
                    },
                ],
                Circuits =
                [
                    new CircuitHistoryProfile
                    {
                        LayoutId = "silverwood",
                        Name = "Silverwood Park",
                        Place = "Northamptonshire, Great Britain",
                        LengthKm = "5.9",
                        Turns = 18,
                        Facts =
                        [
                            "Fastest average speed of the calendar",
                            "Hosted the first documented round",
                            "A third fact the card never shows",
                        ],
                        Editions =
                        [
                            new CircuitEdition(1950, 1, "Grand Prix of Silverwood", "G. Farina", "Alfa Romeo"),
                            new CircuitEdition(1951, 1, "Grand Prix of Silverwood", "J. Gonzalez", "Ferrari"),
                        ],
                    },
                ],
                Timeline = timeline,
                Reference = reference,
                YearsCovered = [1950, 1951],
            };
        }

        private static CareerSeasonCard Season(int year)
        {
            var lines = new List<CareerSeasonRoundLine>();
            double points = 0;
            string[] venues =
            [
                "Phoenix", "Interlagos", "Imola", "Monaco",
                "Montreal", "Mexico City", "Magny-Cours", "Silverstone",
            ];
            for (int index = 0; index < venues.Length; index++)
            {
                points += index % 3 == 0 ? 9 : index % 3 == 1 ? 6 : 3;
                lines.Add(new CareerSeasonRoundLine
                {
                    Round = index + 1,
                    Venue = venues[index],
                    PlayerFinish = index % 4 + 1,
                    ChampionAfter = index % 2 == 0 ? "A. Senna" : "Mike Racer",
                    PlayerPointsAfter = points,
                });
            }
            return new CareerSeasonCard
            {
                SeasonYear = year,
                RoundsApplied = lines.Count,
                RoundCount = 16,
                RoundLines = lines,
                ChampionName = "Mike Racer",
                Headlines = ["A season written into the ledger."],
            };
        }

        private static CareerSummary SummaryFor(int round, int? position) => new()
        {
            CareerName = "Encyclopedia Archive",
            SeasonYear = 1991,
            SeriesName = "F1 Super Monaco GP",
            CurrentRound = round,
            RoundCount = 16,
            PlayerDriverId = "driver.player",
            PlayerLiveryName = "vehicle-livery",
            PlayerPosition = position,
        };

        private static SeasonPack MinimalPack() => new()
        {
            Manifest = new PackManifest
            {
                PackId = "smgp-render",
                Name = "SMGP Render",
                Version = "1.0.0",
                FormatVersion = 1,
                CareerStyle = SmgpRules.CareerStyle,
            },
            Season = new SeasonDefinition
            {
                Year = 1991,
                SeriesName = "F1 Super Monaco GP",
                Ams2Class = "smgp",
                PointsSystem = new CatalogSeason { RacePoints = [new(9), new(6), new(4)] },
                Rounds = [],
            },
            Teams = [],
            Drivers = [],
            Entries = [],
        };
    }
}
