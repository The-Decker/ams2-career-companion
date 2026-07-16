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

/// <summary>Real-WPF coverage for the unified read-only SMGP newsroom.</summary>
public sealed class NewsViewRenderTests
{
    [Theory]
    [InlineData(1600, 900, 1.0)]
    [InlineData(920, 620, 1.3)]
    public void PopulatedFrontPage_RespondsAtWideAndMinimumScaledSizes(
        double width, double height, double scale)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new NewsViewModel(NewsSession.Populated());
            var view = new NewsView
            {
                DataContext = vm,
                LayoutTransform = new ScaleTransform(scale, scale),
            };
            using var host = Host.Show(view, width, height);

            string[] texts = host.VisibleTexts().ToArray();
            Assert.Contains("SMGP NEWSROOM", texts);
            Assert.Contains("TITLE TENSION AT ITALY", texts);
            Assert.Contains("TOP DISPATCHES", texts);
            Assert.Contains("LATEST DISPATCHES", texts);
            Assert.Contains("A. Senna", texts);
            Assert.Contains("Madonna", texts);

            var scroll = Assert.IsType<ScrollViewer>(view.FindName("NewsScroll"));
            Assert.True(scroll.Focusable);
            Assert.True(scroll.ViewportWidth > 0);
            Assert.True(scroll.ScrollableHeight > 0);
            Assert.Equal(0, scroll.ScrollableWidth, precision: 1);
            Assert.Equal(Visibility.Collapsed, scroll.ComputedHorizontalScrollBarVisibility);

            var search = Assert.IsType<TextBox>(view.FindName("NewsSearch"));
            var category = Assert.IsType<ComboBox>(view.FindName("NewsCategoryFilter"));
            Assert.True(search.Focusable);
            Assert.True(search.IsTabStop);
            Assert.True(category.Focusable);
            Assert.True(category.IsTabStop);
            Assert.Equal("Search newsroom stories", AutomationProperties.GetName(search));
            Assert.Equal("Newsroom category filter", AutomationProperties.GetName(category));

            var lead = Assert.IsType<Button>(view.FindName("LeadStoryButton"));
            Assert.True(lead.Focusable);
            Assert.Equal(SoundEffectCue.Navigate, SoundAssist.GetCue(lead));
        });
    }

    [Fact]
    public void SearchAndCategoryFilters_AreKeyboardNative_AndClearWithoutAudioChatter()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new NewsViewModel(NewsSession.Populated());
            var view = new NewsView { DataContext = vm };
            using var host = Host.Show(view, 1100, 760);

            var search = Assert.IsType<TextBox>(view.FindName("NewsSearch"));
            var category = Assert.IsType<ComboBox>(view.FindName("NewsCategoryFilter"));
            Button clear = Descendants<Button>(view).Single(button =>
                AutomationProperties.GetName(button) == "Clear newsroom filters");
            Assert.Equal(SoundEffectCue.None, SoundAssist.GetCue(clear));
            Assert.False(clear.IsEnabled);

            NewsCategoryFilterViewModel rivalry = vm.AvailableCategories.Single(filter =>
                filter.Category == NewsStoryCategory.Rivalry);
            category.SelectedItem = rivalry;
            category.GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateSource();
            search.Text = "word-not-in-the-wire";
            search.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            host.Layout();

            Assert.True(vm.IsFilteredEmpty);
            Assert.True(clear.IsEnabled);
            Border empty = Assert.IsType<Border>(view.FindName("NewsFilteredEmptyState"));
            Assert.Equal(Visibility.Visible, empty.Visibility);
            Assert.Contains(host.VisibleTexts(), text => text.StartsWith(
                "No recorded stories match", StringComparison.Ordinal));

            clear.Command!.Execute(clear.CommandParameter);
            host.Layout();
            Assert.False(vm.HasActiveFilter);
            Assert.NotEmpty(vm.FilteredStories);
            Assert.Equal(Visibility.Collapsed, empty.Visibility);
        });
    }

    [Fact]
    public void EmptyLegacyAndLoadingStates_AreDistinctAndScreenReaderNamed()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var emptyView = new NewsView { DataContext = new NewsViewModel(NewsSession.Empty()) };
            using (var emptyHost = Host.Show(emptyView, 920, 620))
            {
                Border empty = Assert.IsType<Border>(emptyView.FindName("NewsEmptyState"));
                Assert.Equal("Newsroom empty", AutomationProperties.GetName(empty));
                Assert.Contains("THE WIRE IS QUIET", emptyHost.VisibleTexts());
                Assert.Equal(Visibility.Collapsed,
                    Assert.IsType<ScrollViewer>(emptyView.FindName("NewsScroll")).Visibility);
            }

            var legacyView = new NewsView { DataContext = new NewsViewModel(NewsSession.Legacy()) };
            using (var legacyHost = Host.Show(legacyView, 920, 620))
            {
                Assert.Contains("LEGACY NEWS ARCHIVE - AVAILABLE RECORDS ONLY", legacyHost.VisibleTexts());
                Assert.Contains("LEGACY RACE REPORT", legacyHost.VisibleTexts());
            }

            var loadingView = new NewsView { DataContext = new LoadingNewsState() };
            using var loadingHost = Host.Show(loadingView, 920, 620);
            Border loading = Assert.IsType<Border>(loadingView.FindName("NewsLoadingState"));
            Assert.Equal("Newsroom loading", AutomationProperties.GetName(loading));
            Assert.Equal(Visibility.Visible, loading.Visibility);
            Assert.Contains("OPENING THE NEWS DESK", loadingHost.VisibleTexts());
        });
    }

    [Fact]
    public void ArticleOpenClose_RendersReader_AndMovesKeyboardFocus()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new NewsViewModel(NewsSession.Populated());
            var view = new NewsView { DataContext = vm };
            using var host = Host.Show(view, 1180, 820);

            var lead = Assert.IsType<Button>(view.FindName("LeadStoryButton"));
            lead.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            lead.Command!.Execute(lead.CommandParameter);
            host.Layout();

            Assert.True(vm.IsReaderOpen);
            Grid reader = Assert.IsType<Grid>(view.FindName("ArticleReader"));
            Assert.Equal(Visibility.Visible, reader.Visibility);
            Assert.Contains("The recorded championship fight tightens after Italy.", host.VisibleTexts());
            var close = Assert.IsType<Button>(view.FindName("ArticleCloseButton"));
            Assert.True(close.Focusable);
            Assert.True(close.IsKeyboardFocusWithin || close.IsFocused);
            Assert.Equal(SoundEffectCue.Back, SoundAssist.GetCue(close));
            Assert.False(vm.OpenArticleCommand.CanExecute(vm.SelectedArticle));

            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            close.Command!.Execute(close.CommandParameter);
            host.Layout();

            Assert.False(vm.IsReaderOpen);
            Assert.Equal(Visibility.Collapsed, reader.Visibility);
            Assert.True(lead.IsKeyboardFocusWithin || lead.IsFocused);
        });
    }

    [Fact]
    public void HistoryLatestDispatch_OpensExactArticle_AndArticleReturnsToHistory()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var session = NewsSession.Populated();
            using var hub = new HubViewModel(session);
            HubTabViewModel historyTab = hub.Tabs.Single(tab => tab.Key == HubViewModel.HistoryTabKey);
            hub.SelectTabCommand.Execute(historyTab);

            var historyView = new HistoryView { DataContext = hub.History };
            using (var historyHost = HistoryHost.Show(historyView, hub))
            {
                Button story = Descendants<Button>(historyView).Single(button =>
                    AutomationProperties.GetName(button) == "TITLE TENSION AT ITALY");
                story.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                historyHost.Layout();
            }

            Assert.Equal(HubViewModel.NewsTabKey, hub.SelectedTab?.Key);
            Assert.True(hub.News.IsReaderOpen);
            Assert.Equal("smgp:2:8:0", hub.News.SelectedArticle?.Key);

            var newsView = new NewsView { DataContext = hub.News };
            using var newsHost = Host.Show(newsView, 1100, 760, hub);
            Button historyLink = Assert.IsType<Button>(newsView.FindName("HistoryLinkButton"));
            Assert.False(SoundAssist.GetSuppressWhen(historyLink));
            Assert.Equal("race:2:8", historyLink.Tag);
            historyLink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            newsHost.Layout();

            Assert.Equal(HubViewModel.HistoryTabKey, hub.SelectedTab?.Key);
        });
    }

    [Fact]
    public void TearOff_RendersTheArticleCapableDeskAtItsMinimumSize()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var window = new NewsWindow
            {
                DataContext = new NewsViewModel(NewsSession.Populated()),
                Width = 420,
                Height = 360,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                WpfRenderHarness.Pump(DispatcherPriority.Loaded);
                window.UpdateLayout();

                Assert.InRange(window.ActualWidth, 419, 430);
                Assert.InRange(window.ActualHeight, 359, 370);
                NewsView news = Assert.Single(Descendants<NewsView>(window));
                Assert.Equal(Visibility.Collapsed,
                    Assert.IsType<Button>(news.FindName("PopOutButton")).Visibility);
                Assert.True(Assert.IsType<ScrollViewer>(news.FindName("NewsScroll")).ViewportWidth > 0);
            }
            finally
            {
                window.Close();
                WpfRenderHarness.Pump(DispatcherPriority.Background);
            }
        });
    }

    private sealed class Host : IDisposable
    {
        private readonly Window _window;
        private readonly NewsView _view;

        private Host(Window window, NewsView view)
        {
            _window = window;
            _view = view;
        }

        public static Host Show(
            NewsView view,
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
                ShowActivated = true,
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
            WpfRenderHarness.Pump(DispatcherPriority.Input);
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

    private sealed class HistoryHost : IDisposable
    {
        private readonly Window _window;

        private HistoryHost(Window window) => _window = window;

        public static HistoryHost Show(HistoryView view, HubViewModel hub)
        {
            var window = new Window
            {
                Content = view,
                Tag = hub,
                Width = 1100,
                Height = 760,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -10000,
                Top = -10000,
            };
            window.Show();
            window.UpdateLayout();
            WpfRenderHarness.Pump(DispatcherPriority.Loaded);
            return new HistoryHost(window);
        }

        public void Layout()
        {
            _window.UpdateLayout();
            WpfRenderHarness.Pump();
        }

        public void Dispose()
        {
            _window.Close();
            WpfRenderHarness.Pump(DispatcherPriority.Background);
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

    private sealed class LoadingNewsState
    {
        public bool IsLoading => true;
        public bool IsEmpty => false;
        public bool IsReaderOpen => false;
    }

    private sealed class NewsSession : ICareerSession
    {
        public required CareerSummary SummaryValue { get; init; }
        public required CareerTimeline Timeline { get; init; }
        public SmgpPaddockModel? Paddock { get; init; }
        public IReadOnlyList<SmgpDispatch> Dispatches { get; init; } = [];
        public IReadOnlyList<NewsDispatch> Feed { get; init; } = [];

        public CareerSummary Summary => SummaryValue;
        public SeasonPack Pack { get; } = MinimalPack();
        public CareerTimeline CareerTimeline() => Timeline;
        public SmgpPaddockModel? SmgpPaddock() => Paddock;
        public IReadOnlyList<SmgpDispatch> SmgpDispatches() => Dispatches;
        public IReadOnlyList<NewsDispatch> ReadFeed() => Feed;

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

        public static NewsSession Empty() => new()
        {
            SummaryValue = SummaryFor(0, null),
            Timeline = Companion.ViewModels.Services.CareerTimeline.Empty,
        };

        public static NewsSession Legacy() => new()
        {
            SummaryValue = SummaryFor(1, 8),
            Timeline = Companion.ViewModels.Services.CareerTimeline.Empty,
            Feed =
            [
                new NewsDispatch
                {
                    SeasonYear = 1990,
                    Round = 1,
                    Kind = "race",
                    Headline = "LEGACY RACE REPORT",
                    Body = "A preserved article without modern identity metadata.",
                    WhyText = "Stored in the original journal.",
                },
            ],
        };

        public static NewsSession Populated()
        {
            var player = Driver("driver.player", "Mike Racer", "team.bullets", "Bullets", true);
            var rival = Driver("driver.senna", "A. Senna", "team.madonna", "Madonna", false);
            var timeline = new CareerTimeline
            {
                Seasons = [Season(1990), Season(1991)],
                Records = new CareerRecordsBook { Wins = 3, Podiums = 7, TotalPoints = 72 },
            };
            var dispatches = Enumerable.Range(1, 8)
                .Select(round => new SmgpDispatch
                {
                    SortSeason = 2,
                    SortRound = round,
                    SortSeq = 0,
                    WhenLabel = $"Season 2 - Round {round}",
                    Kind = round == 8
                        ? SmgpDispatchKind.TitleRace
                        : round == 6 ? SmgpDispatchKind.RivalWatch : SmgpDispatchKind.RaceResult,
                    Headline = round == 8
                        ? "TITLE TENSION AT ITALY"
                        : round == 6 ? "SENNA ANSWERS BACK" : $"ROUND {round} REPORT",
                    Body = round == 8
                        ? "The recorded championship fight tightens after Italy."
                        : $"The stored round {round} report enters the archive.",
                    DriverArtKey = round is 8 or 6 ? "driver.senna" : "driver.player",
                    TeamArtKey = round is 8 or 6 ? "team.madonna" : "team.bullets",
                })
                .ToArray();

            return new NewsSession
            {
                SummaryValue = SummaryFor(8, 2),
                Timeline = timeline,
                Paddock = new SmgpPaddockModel
                {
                    Drivers = [player, rival],
                    Teams =
                    [
                        Team("team.bullets", "Bullets"),
                        Team("team.madonna", "Madonna"),
                    ],
                },
                Dispatches = dispatches,
                Feed =
                [
                    new NewsDispatch
                    {
                        SeasonYear = 1991,
                        Round = 4,
                        Kind = "race",
                        Headline = "JOURNAL AT FRANCE",
                        Body = "The deterministic journal preserves the French result.",
                        WhyText = "This article comes from the stored round journal.",
                    },
                ],
            };
        }

        private static CareerSeasonCard Season(int year)
        {
            string[] venues =
            [
                "Brazil", "San Marino", "Monaco", "France",
                "Britain", "Germany", "Belgium", "Italy",
            ];
            double points = 0;
            var lines = new List<CareerSeasonRoundLine>();
            for (int index = 0; index < venues.Length; index++)
            {
                points += index % 3 == 0 ? 9 : index % 3 == 1 ? 6 : 3;
                lines.Add(new CareerSeasonRoundLine
                {
                    Round = index + 1,
                    Venue = venues[index],
                    PlayerFinish = index % 4 + 1,
                    RivalName = "A. Senna",
                    RivalFinish = index % 3 + 1,
                    ChampionAfter = index % 2 == 0 ? "Mike Racer" : "A. Senna",
                    PlayerPointsAfter = points,
                });
            }
            return new CareerSeasonCard
            {
                SeasonYear = year,
                RoundsApplied = lines.Count,
                RoundCount = 16,
                RoundLines = lines,
            };
        }

        private static SmgpDriverCard Driver(
            string id,
            string name,
            string teamId,
            string teamName,
            bool isPlayer) => new()
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
            Career = null,
            Season = null,
            Prestige = isPlayer ? 3 : 5,
        };

        private static SmgpTeamCard Team(string id, string name) => new()
        {
            TeamId = id,
            Name = name,
            Motto = "",
            LogoKey = $"smgp/logos/{id}.png",
            History = [],
            Quotes = [],
            DriverNames = [],
            Prestige = 5,
        };

        private static CareerSummary SummaryFor(int round, int? position) => new()
        {
            CareerName = "SMGP News Render",
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
                PackId = "smgp-news-render",
                Name = "SMGP News Render",
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
