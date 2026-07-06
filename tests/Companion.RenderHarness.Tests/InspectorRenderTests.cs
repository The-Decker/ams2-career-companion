using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Companion.App.Views;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Services;
using Companion.ViewModels.Standings;

namespace Companion.RenderHarness.Tests;

/// <summary>
/// Off-screen render coverage for the "Why?" inspector overlay wired into the REAL StandingsView
/// (career-hub-design.md §5): with an inspector open, the overlay panel renders its title, summary
/// and contribution rows, and the Close command clears it. Runs through the same STA
/// <see cref="WpfRenderHarness"/> as the result-entry render tests; self-skips off Windows.
/// </summary>
public sealed class InspectorRenderTests
{
    [Fact]
    public void OpenInspector_RendersThePanel_WithTitleSummaryAndRows()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var session = new InspectorRenderFakeSession
            {
                Chain = new JournalChain
                {
                    Entity = "driver.hulme",
                    Title = "Why P2 — Denny Hulme, Round 3",
                    Summary = "Finished P2 against an expected P5.",
                    Contributions =
                    [
                        new JournalContribution { Label = "Expected finish", Detail = "Finished P2.", Value = "P2" },
                        new JournalContribution { Label = "Reputation", Detail = "Reputation moved.", Value = "43.2" },
                    ],
                },
            };
            var vm = new StandingsViewModel([], session.Pack, settings: null, session: session);

            var view = new StandingsView { DataContext = vm };
            using var host = Host.Show(view);

            // Nothing open yet: the overlay is collapsed.
            Assert.False(vm.IsInspectorOpen);

            // Click a driver number (the row Why? button target) → the inspector opens.
            vm.OpenInspectorCommand.Execute("driver.hulme");
            host.Layout();

            Assert.True(vm.IsInspectorOpen);

            var texts = host.VisibleTexts();
            Assert.Contains("Why P2 — Denny Hulme, Round 3", texts);
            Assert.Contains("Finished P2 against an expected P5.", texts);
            // Both contribution row labels + values render in the realised tree.
            Assert.Contains(texts, t => t.Contains("Expected finish"));
            Assert.Contains(texts, t => t.Contains("P2"));
            Assert.Contains(texts, t => t.Contains("Reputation"));

            // Close (the panel's Close button / Esc both bind here) clears the overlay.
            vm.CloseInspectorCommand.Execute(null);
            host.Layout();
            Assert.False(vm.IsInspectorOpen);
        });
    }

    // ---------- a minimal off-screen host for one StandingsView ----------

    private sealed class Host : IDisposable
    {
        private readonly Window _window;
        private readonly StandingsView _view;

        private Host(Window window, StandingsView view)
        {
            _window = window;
            _view = view;
        }

        public static Host Show(StandingsView view)
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

        /// <summary>Every non-empty text string visible in the realised visual tree (a TextBlock's
        /// Text, or its concatenated Runs).</summary>
        public IReadOnlyList<string> VisibleTexts()
        {
            var texts = new List<string>();
            foreach (var tb in Descendants<TextBlock>(_view))
            {
                if (!IsEffectivelyVisible(tb))
                    continue;
                string text = tb.Text;
                if (string.IsNullOrEmpty(text))
                    text = string.Concat(tb.Inlines.OfType<Run>().Select(r => r.Text));
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

    /// <summary>Minimal session returning a controlled chain for the standings inspector command.</summary>
    private sealed class InspectorRenderFakeSession : ICareerSession
    {
        public JournalChain Chain { get; init; } = JournalChain.Empty;

        public JournalChain JournalFor(string entity, int? round = null) => Chain;

        public SeasonPack Pack { get; } = MinimalPack();

        public CareerSummary Summary => new()
        {
            CareerName = "Fake",
            SeasonYear = 1967,
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
                Year = 1967,
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
