using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Companion.App.Views;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.RenderHarness.Tests;

/// <summary>
/// Off-screen render coverage for the season-scoped "Why?" inspector wired into the REAL HistoryView
/// (career-hub-design.md §4/§5, decision 18): a scrapbook card's final-standing number is a click
/// target that opens the shared inspector overlay for THAT card's season — including a FINISHED
/// earlier season, not just the current one — and Close clears it. Mirrors the Standings inspector
/// render test; self-skips off Windows.
/// </summary>
public sealed class HistoryInspectorRenderTests
{
    [Fact]
    public void OpenSeasonInspector_ForAFinishedSeasonCard_RendersThePanel_ThenCloses()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var session = new HistoryRenderFakeSession
            {
                Timeline = new CareerTimeline
                {
                    Seasons =
                    [
                        // A finished earlier season the card renders as a Why? target.
                        new CareerSeasonCard
                        {
                            SeasonYear = 1967, PlayerPosition = 4, RoundsApplied = 2, RoundCount = 2,
                            IsComplete = true, FinalReputation = 42.5, FinalOpi = 0.4,
                            ChampionName = "Denny Hulme",
                        },
                    ],
                },
                // The season-scoped chain returned for that finished season's year.
                SeasonChain = new JournalChain
                {
                    Entity = "player",
                    Title = "Why P4 — You, 1967",
                    Summary = "You finished P4, below your expected P2.",
                    Contributions =
                    [
                        new JournalContribution { Label = "Expected finish", Detail = "Finished P4.", Value = "P4" },
                        new JournalContribution { Label = "Reputation", Detail = "Reputation moved.", Value = "42.5" },
                    ],
                },
            };
            var vm = new HistoryViewModel(session);

            var view = new HistoryView { DataContext = vm };
            using var host = Host.Show(view);

            // Nothing open yet.
            Assert.False(vm.IsInspectorOpen);

            // Open the season-scoped inspector for the finished 1967 card (the card number's target).
            vm.OpenSeasonInspectorCommand.Execute(1967);
            host.Layout();

            Assert.True(vm.IsInspectorOpen);

            var texts = host.VisibleTexts();
            Assert.Contains("Why P4 — You, 1967", texts);
            Assert.Contains("You finished P4, below your expected P2.", texts);
            Assert.Contains(texts, t => t.Contains("Expected finish"));
            Assert.Contains(texts, t => t.Contains("P4"));
            Assert.Contains(texts, t => t.Contains("Reputation"));

            // Close clears the overlay (the Close button / Esc both bind CloseInspectorCommand).
            vm.CloseInspectorCommand.Execute(null);
            host.Layout();
            Assert.False(vm.IsInspectorOpen);
        });
    }

    // ---------- a minimal off-screen host for one HistoryView ----------

    private sealed class Host : IDisposable
    {
        private readonly Window _window;
        private readonly HistoryView _view;

        private Host(Window window, HistoryView view)
        {
            _window = window;
            _view = view;
        }

        public static Host Show(HistoryView view)
        {
            var window = new Window
            {
                Content = view,
                Width = 1000,
                Height = 700,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -10000,
                Top = -10000,
            };
            window.Show();
            window.UpdateLayout();
            WpfRenderHarness.Pump(DispatcherPriority.Loaded);
            return new Host(window, view);
        }

        public void Layout()
        {
            _window.UpdateLayout();
            WpfRenderHarness.Pump(DispatcherPriority.Render);
            WpfRenderHarness.Pump();
        }

        public IReadOnlyList<string> VisibleTexts()
        {
            var texts = new List<string>();
            foreach (var tb in Descendants<TextBlock>(_view))
            {
                if (!IsEffectivelyVisible(tb))
                    continue;
                string text = tb.Text;
                if (string.IsNullOrEmpty(text))
                    text = string.Concat(tb.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text));
                if (!string.IsNullOrEmpty(text))
                    texts.Add(text);
            }
            return texts;
        }

        private bool IsEffectivelyVisible(UIElement? element)
        {
            for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is UIElement ui && ui.Visibility != Visibility.Visible)
                    return false;
                if (ReferenceEquals(node, _view))
                    break;
            }
            return element is not null;
        }

        public void Dispose()
        {
            _window.Close();
            WpfRenderHarness.Pump(DispatcherPriority.Background);
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    yield return match;
                foreach (var descendant in Descendants<T>(child))
                    yield return descendant;
            }
        }
    }

    /// <summary>Minimal session for the History lens: a controlled timeline + season-scoped chain.
    /// The season-scoped <see cref="JournalForSeason(string,int,int?)"/> member returns the finished
    /// season's chain; every other member is the additive default.</summary>
    private sealed class HistoryRenderFakeSession : ICareerSession
    {
        public Companion.ViewModels.Services.CareerTimeline Timeline { get; init; } =
            Companion.ViewModels.Services.CareerTimeline.Empty;

        public JournalChain SeasonChain { get; init; } = JournalChain.Empty;

        public Companion.ViewModels.Services.CareerTimeline CareerTimeline() => Timeline;

        public JournalChain JournalForSeason(string entity, int seasonYear, int? round = null) => SeasonChain;

        public SeasonPack Pack { get; } = MinimalPack();

        public CareerSummary Summary => new()
        {
            CareerName = "Fake",
            SeasonYear = 1969,
            SeriesName = "Test",
            CurrentRound = 1,
            RoundCount = 1,
            PlayerDriverId = "driver.hulme",
            PlayerLiveryName = "Livery",
        };

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

        private static SeasonPack MinimalPack() => new()
        {
            Manifest = new PackManifest { PackId = "p", Name = "P", Version = "1.0.0", FormatVersion = 1 },
            Season = new SeasonDefinition
            {
                Year = 1969,
                SeriesName = "Test",
                Ams2Class = "c",
                PointsSystem = new CatalogSeason { RacePoints = [new(9)] },
                Rounds = [],
            },
            Teams = [],
            Drivers = [],
            Entries = [],
        };
    }
}
