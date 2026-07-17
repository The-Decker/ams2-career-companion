using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Companion.App.Audio;
using Companion.App.Views;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Core.Smgp;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.RenderHarness.Tests;

/// <summary>
/// Real-WPF coverage for the living History archive. The fixture is entirely read-side data: it
/// never applies a result, creates a fold, or invokes the deterministic news generator.
/// </summary>
public sealed class HistoryArchiveRenderTests
{
    [Theory]
    [InlineData(1600, 900, 1.0)]
    [InlineData(920, 620, 1.3)]
    public void PopulatedSmgpArchive_RespondsAtWideAndMinimumScaledSizes(
        double width, double height, double scale)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new HistoryViewModel(ArchiveSession.Populated());
            var view = new HistoryView
            {
                DataContext = vm,
                LayoutTransform = new ScaleTransform(scale, scale),
            };
            using var host = Host.Show(view, width, height);

            string[] texts = host.VisibleTexts().ToArray();
            Assert.Contains("HISTORY ARCHIVE", texts);
            Assert.Contains("CAREER SNAPSHOT", texts);
            Assert.Contains("LATEST DISPATCHES", texts);
            Assert.Contains("CAREER TIMELINE", texts);
            Assert.Contains("RECORDS AND MILESTONES", texts);
            Assert.Contains("RACE ARCHIVE", texts);
            Assert.Contains("Mike Racer", texts);
            Assert.Contains("A. Senna", texts);
            Assert.Contains("Monaco", texts);

            var scroll = Assert.IsType<ScrollViewer>(view.FindName("HistoryScroll"));
            Assert.True(scroll.Focusable);
            Assert.True(scroll.ViewportWidth > 0);
            Assert.True(scroll.ScrollableHeight > 0,
                "A populated career must remain vertically scrollable at both supported sizes.");
            Assert.Equal(0, scroll.ScrollableWidth, precision: 1);
            Assert.Equal(Visibility.Collapsed, scroll.ComputedHorizontalScrollBarVisibility);

            var search = Assert.IsType<TextBox>(view.FindName("HistoryRaceSearch"));
            Assert.True(search.Focusable);
            Assert.True(search.IsTabStop);
            Assert.Equal("Search the career race archive", AutomationProperties.GetName(search));

            Button openNews = Assert.IsType<Button>(view.FindName("OpenNewsButton"));
            Assert.True(openNews.Focusable);
            Assert.Equal(SoundEffectCue.Navigate, SoundAssist.GetCue(openNews));
            Assert.True(SoundAssist.GetSuppressWhen(openNews),
                "An isolated History surface has no successful News destination and must stay silent.");
        });
    }

    [Fact]
    public void FreshCampaign_ShowsAllSeventeenSlotsBeforeRaceOne_WithCurrentState()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var session = ArchiveSession.Fresh();
            using var hub = new HubViewModel(session);
            var view = new HistoryView { DataContext = hub.History };
            using var host = Host.Show(view, 1200, 760, hub);

            var panel = Assert.IsType<Border>(view.FindName("CampaignTimelinePanel"));
            var slots = Assert.IsType<ItemsControl>(view.FindName("CampaignTimelineSlots"));
            var timelineScroll = Assert.IsType<ScrollViewer>(view.FindName("CampaignTimelineScroll"));

            Assert.Equal(Visibility.Visible, panel.Visibility);
            Assert.Equal(17, slots.Items.Count);
            Assert.True(timelineScroll.Focusable);
            Assert.True(timelineScroll.IsTabStop);
            Assert.Equal("Campaign season timeline", AutomationProperties.GetName(timelineScroll));
            Assert.Contains("arrow keys", AutomationProperties.GetHelpText(timelineScroll),
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("17-SEASON CAMPAIGN", host.VisibleTexts());
            Assert.Contains("The First Summer", host.VisibleTexts());
            Assert.Contains("CURRENT", host.VisibleTexts());
            Assert.Equal(
                Visibility.Collapsed,
                Assert.IsType<ScrollViewer>(view.FindName("HistoryScroll")).Visibility);
        });
    }

    [Fact]
    public void SearchAndFilters_AreKeyboardReachable_AndExposeANamedFilteredEmptyState()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new HistoryViewModel(ArchiveSession.Populated());
            var view = new HistoryView { DataContext = vm };
            using var host = Host.Show(view, 1100, 760);

            var search = Assert.IsType<TextBox>(view.FindName("HistoryRaceSearch"));
            ComboBox filter = Descendants<ComboBox>(view).Single(combo =>
                AutomationProperties.GetName(combo) == "Race archive outcome filter");
            Button clear = Descendants<Button>(view).Single(button =>
                AutomationProperties.GetName(button) == "Clear race archive filters");

            Assert.True(search.Focusable);
            Assert.True(filter.Focusable);
            Assert.True(clear.Focusable);
            Assert.False(clear.IsEnabled);

            search.Text = "venue-that-does-not-exist";
            search.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            host.Layout();

            Assert.True(vm.IsRaceFilterEmpty);
            Assert.True(clear.IsEnabled);
            Border empty = Descendants<Border>(view).Single(border =>
                AutomationProperties.GetName(border) == "No races match the filters");
            Assert.Equal(Visibility.Visible, empty.Visibility);
            Assert.Contains(host.VisibleTexts(), text => text.StartsWith(
                "No recorded races match", StringComparison.Ordinal));

            clear.Command!.Execute(clear.CommandParameter);
            host.Layout();
            Assert.False(vm.HasActiveRaceFilter);
            Assert.NotEmpty(vm.FilteredRaces);
            Assert.Equal(Visibility.Collapsed, empty.Visibility);
        });
    }

    [Fact]
    public void FreshLegacyAndLoadingStates_AreDistinctAndScreenReaderNamed()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var freshView = new HistoryView
            {
                DataContext = new HistoryViewModel(ArchiveSession.Fresh()),
            };
            using (var freshHost = Host.Show(freshView, 920, 620))
            {
                Assert.Contains(freshHost.VisibleTexts(), text => text.StartsWith(
                    "No history yet", StringComparison.Ordinal));
                var archive = Assert.IsType<ScrollViewer>(freshView.FindName("HistoryScroll"));
                Assert.Equal(Visibility.Collapsed, archive.Visibility);
            }

            var legacyView = new HistoryView
            {
                DataContext = new HistoryViewModel(ArchiveSession.Legacy()),
            };
            using (var legacyHost = Host.Show(legacyView, 920, 620))
            {
                Assert.Contains("LEGACY ARCHIVE - AVAILABLE RECORDS ONLY", legacyHost.VisibleTexts());
                Assert.DoesNotContain("LATEST DISPATCHES", legacyHost.VisibleTexts());
            }

            var loadingView = new HistoryView { DataContext = new LoadingHistoryState() };
            using var loadingHost = Host.Show(loadingView, 920, 620);
            Border loading = Assert.IsType<Border>(loadingView.FindName("HistoryLoadingState"));
            Assert.Equal("History archive loading", AutomationProperties.GetName(loading));
            Assert.Equal(Visibility.Visible, loading.Visibility);
            Assert.Contains("OPENING THE ARCHIVE", loadingHost.VisibleTexts());
        });
    }

    [Fact]
    public void LatestDispatchOpenNews_UsesTheRealHubDestination_AndOnlyThenAllowsSound()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var session = ArchiveSession.Populated();
            using var hub = new HubViewModel(session);
            HubTabViewModel historyTab = hub.Tabs.Single(tab => tab.Key == HubViewModel.HistoryTabKey);
            hub.SelectTabCommand.Execute(historyTab);

            var view = new HistoryView { DataContext = hub.History };
            using var host = Host.Show(view, 1100, 760, hub);
            Button openNews = Assert.IsType<Button>(view.FindName("OpenNewsButton"));

            Assert.False(SoundAssist.GetSuppressWhen(openNews));
            Assert.Equal(HubViewModel.HistoryTabKey, hub.SelectedTab?.Key);

            openNews.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            host.Layout();

            Assert.Equal(HubViewModel.NewsTabKey, hub.SelectedTab?.Key);
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

        public static Host Show(
            HistoryView view,
            double width,
            double height,
            HubViewModel? hub = null)
        {
            var window = new Window
            {
                Content = view,
                Tag = hub,
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

    private sealed class LoadingHistoryState
    {
        public bool IsLoading => true;
        public bool IsFresh => false;
        public bool HasAnyRace => false;
    }

    private sealed class ArchiveSession : ICareerSession
    {
        public required CareerSummary SummaryValue { get; init; }
        public required CareerTimeline Timeline { get; init; }
        public SmgpPaddockModel? Paddock { get; init; }
        public IReadOnlyList<SmgpDispatch> Dispatches { get; init; } = [];
        public string? RivalId { get; init; }
        public IReadOnlyList<CampaignTimelineEntry> Campaign { get; init; } = [];

        public CareerSummary Summary => SummaryValue;
        public SeasonPack Pack { get; } = MinimalPack();

        public CareerTimeline CareerTimeline() => Timeline;
        public IReadOnlyList<CampaignTimelineEntry> CampaignTimeline() => Campaign;
        public SmgpPaddockModel? SmgpPaddock() => Paddock;
        public IReadOnlyList<SmgpDispatch> SmgpDispatches() => Dispatches;
        public string? CurrentSmgpRivalDriverId() => RivalId;

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

        public static ArchiveSession Fresh() => new()
        {
            SummaryValue = SummaryFor(round: 0, position: null),
            Timeline = Companion.ViewModels.Services.CareerTimeline.Empty,
            Campaign = CampaignFor(current: 1, completed: 0),
        };

        public static ArchiveSession Legacy() => new()
        {
            SummaryValue = SummaryFor(round: 1, position: 8),
            Timeline = new CareerTimeline
            {
                Seasons =
                [
                    new CareerSeasonCard
                    {
                        SeasonYear = 1990,
                        RoundsApplied = 1,
                        RoundCount = 16,
                        RoundLines = [Race(1, "Brazil", 8, 1)],
                    },
                ],
                Records = new CareerRecordsBook { TotalPoints = 1 },
            },
            Campaign = CampaignFor(current: 1, completed: 0),
        };

        public static ArchiveSession Populated()
        {
            var player = Driver(
                "driver.player", "Mike Racer", "team.bullets", "Bullets", true,
                new SmgpCareerStats
                {
                    Starts = 18,
                    Wins = 3,
                    Podiums = 7,
                    Poles = 2,
                    Top5s = 10,
                    Points = 72,
                    Titles = 1,
                },
                [
                    new SmgpCareerBeat
                    {
                        Season = 1,
                        Round = SmgpDispatch.SeasonStartRound,
                        WhenLabel = "Season 1",
                        Kind = SmgpBeatKind.Arrived,
                        Headline = "THE JOURNEY BEGINS",
                        Detail = "A first seat on the SMGP grid.",
                    },
                    new SmgpCareerBeat
                    {
                        Season = 2,
                        Round = 2,
                        WhenLabel = "Season 2 · Monaco",
                        Kind = SmgpBeatKind.RivalryEarned,
                        Headline = "RIVALRY IGNITES",
                        Detail = "A recorded championship battle with Senna.",
                        SubjectId = "driver.senna",
                    },
                ]);
            var rival = Driver(
                "driver.senna", "A. Senna", "team.madonna", "Madonna", false, null, []);

            return new ArchiveSession
            {
                SummaryValue = SummaryFor(round: 8, position: 2),
                Timeline = new CareerTimeline
                {
                    Seasons =
                    [
                        Season(1990, 1),
                        Season(1991, 2),
                    ],
                    Records = new CareerRecordsBook
                    {
                        Wins = 3,
                        Podiums = 7,
                        Championships = 1,
                        TotalPoints = 72,
                    },
                },
                Paddock = new SmgpPaddockModel { Drivers = [player, rival], Teams = [] },
                RivalId = "driver.senna",
                Dispatches =
                [
                    new SmgpDispatch
                    {
                        SortSeason = 2,
                        SortRound = 8,
                        SortSeq = 0,
                        WhenLabel = "Season 2 · Round 8",
                        Kind = SmgpDispatchKind.TitleRace,
                        Headline = "TITLE SWING AT MONACO",
                        Body = "The recorded standings tighten after Monaco.",
                        DriverArtKey = "driver.senna",
                        TeamArtKey = "team.madonna",
                    },
                    new SmgpDispatch
                    {
                        SortSeason = 2,
                        SortRound = 7,
                        SortSeq = 0,
                        WhenLabel = "Season 2 · Round 7",
                        Kind = SmgpDispatchKind.RaceResult,
                        Headline = "BULLETS ON THE PODIUM",
                        Body = "Mike brings home another recorded podium.",
                        DriverArtKey = "driver.player",
                        TeamArtKey = "team.bullets",
                    },
                ],
                Campaign = CampaignFor(current: 2, completed: 1),
            };
        }

        private static IReadOnlyList<CampaignTimelineEntry> CampaignFor(int current, int completed)
        {
            string[] titles =
            [
                "The First Summer", "The Open Road", "The Heat Builds", "The Long Climb",
                "The Fifth Campaign", "The New Order", "The Pressure Years", "The Narrow Margin",
                "The Turning Point", "The Tenth Summer", "The Horsepower War", "The Reckoning",
                "The Last Old Guard", "The Golden Chase", "The Penultimate Test",
                "The Silver Jubilee", "The Crown of Crowns",
            ];

            return Enumerable.Range(1, 17)
                .Select(ordinal => new CampaignTimelineEntry
                {
                    Ordinal = ordinal,
                    State = ordinal <= completed
                        ? CampaignSeasonState.Completed
                        : ordinal == current
                            ? CampaignSeasonState.Current
                            : CampaignSeasonState.Locked,
                    Year = ordinal <= completed ? 1989 + ordinal : null,
                    Title = titles[ordinal - 1],
                    Era = ordinal < 10 ? "The Iron Circus" : "The Golden Circus",
                    PlayerPosition = ordinal <= completed ? 2 : null,
                    PlayerChampion = ordinal == completed && completed > 0,
                    MissedRounds = ordinal == 1 && completed > 0,
                })
                .ToArray();
        }

        private static CareerSeasonCard Season(int year, int seasonOrdinal)
        {
            var lines = new List<CareerSeasonRoundLine>();
            double points = 0;
            string[] venues =
            [
                "Brazil", "San Marino", "Monaco", "France",
                "Britain", "Germany", "Belgium", "Italy",
            ];
            for (int index = 0; index < venues.Length; index++)
            {
                points += index % 3 == 0 ? 9 : index % 3 == 1 ? 6 : 3;
                lines.Add(Race(
                    index + 1,
                    venues[index],
                    index % 4 + 1,
                    points,
                    rival: seasonOrdinal == 2 ? "A. Senna" : null));
            }
            return new CareerSeasonCard
            {
                SeasonYear = year,
                RoundsApplied = lines.Count,
                RoundCount = 16,
                RoundLines = lines,
                ChampionName = seasonOrdinal == 1 ? "A. Senna" : "Mike Racer",
                Headlines = ["A season written into the ledger."],
            };
        }

        private static CareerSeasonRoundLine Race(
            int round,
            string venue,
            int? finish,
            double points,
            string? rival = null) => new()
        {
            Round = round,
            Venue = venue,
            PlayerFinish = finish,
            RivalName = rival,
            RivalFinish = rival is null ? null : round % 3 + 1,
            ChampionAfter = round % 2 == 0 ? "A. Senna" : "Mike Racer",
            PlayerPointsAfter = points,
        };

        private static SmgpDriverCard Driver(
            string id,
            string name,
            string teamId,
            string teamName,
            bool isPlayer,
            SmgpCareerStats? career,
            IReadOnlyList<SmgpCareerBeat> timeline) => new()
        {
            DriverId = id,
            Name = name,
            TeamId = teamId,
            TeamName = teamName,
            Number = null,
            PortraitKey = $"portraits/{id}.jpg",
            CarKey = $"cars/{id}.png",
            Epithet = "",
            Bio = [],
            Quotes = [],
            IsPlayer = isPlayer,
            Career = career,
            Season = null,
            Prestige = isPlayer ? 3 : 5,
            Timeline = timeline,
            NarrativeIntro = isPlayer ? "From midfield promise to a title fight." : "",
        };

        private static CareerSummary SummaryFor(int round, int? position) => new()
        {
            CareerName = "SMGP Archive",
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
