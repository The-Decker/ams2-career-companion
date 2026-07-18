using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Companion.App.Controls;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.RenderHarness.Tests;

/// <summary>
/// Real-WPF coverage for the campaign timeline strip (Controls/CampaignTimelineStrip.xaml, hosted
/// at the top of the History tab): the SMGP 17-slot arc (completed outcomes, the glowing current
/// slot, spoiler-free locked slots), the Dynasty arc (the synthetic ordinal-0 Formula Junior
/// prologue card first, locked seasons previewed from their pinned packs), and the empty/legacy
/// career, where the strip collapses outright or renders the short played-only arc. The fixtures
/// are read-side stand-in sessions returning a pinned <see cref="CampaignTimelineEntry"/> list,
/// shaped exactly like the real backend's no-spoiler contract (SMGP locked slots carry nothing).
/// </summary>
public sealed class CampaignTimelineRenderTests
{
    [Fact]
    public void SmgpArc_RendersAllStates_AndKeepsLockedSlotsSpoilerFree()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var session = StripSession.Smgp(current: 3, completed: 2);
            using var hub = new HubViewModel(session);
            var strip = new CampaignTimelineStrip();
            using var host = Host.Show(strip, 1500, 320, hub);

            var panel = Assert.IsType<Border>(strip.FindName("CampaignTimelinePanel"));
            var slots = Assert.IsType<ItemsControl>(strip.FindName("CampaignTimelineSlots"));
            var scroll = Assert.IsType<ScrollViewer>(strip.FindName("CampaignTimelineScroll"));

            Assert.Equal(Visibility.Visible, panel.Visibility);
            Assert.Equal("Campaign season timeline", AutomationProperties.GetName(panel));
            Assert.Equal(17, slots.Items.Count);
            Assert.True(scroll.Focusable);
            Assert.True(scroll.IsTabStop);
            Assert.Equal("Campaign season timeline", AutomationProperties.GetName(scroll));
            Assert.Contains("arrow keys", AutomationProperties.GetHelpText(scroll),
                StringComparison.OrdinalIgnoreCase);

            string[] texts = host.VisibleTexts().ToArray();
            Assert.Contains("17-SEASON CAMPAIGN", texts);
            Assert.Contains("CURRENT", texts);

            // A completed slot carries its outcome: year, position, the honest injury absence.
            string[] first = host.VisibleTexts(SlotBorder(slots, 0)).ToArray();
            Assert.Contains("SEASON 1", first);
            Assert.Contains("1990", first);
            Assert.Contains("The First Summer", first);
            Assert.Contains("The Iron Circus", first);
            Assert.Contains("P2", first);
            Assert.Contains("INJURY ABSENCE", first);

            string[] second = host.VisibleTexts(SlotBorder(slots, 1)).ToArray();
            Assert.Contains("SEASON 2", second);
            Assert.Contains("P1", second);
            Assert.Contains("CHAMPION", second);

            // The current slot glows (full opacity, accent border) and is named by its lore.
            Border current = SlotBorder(slots, 2);
            string[] currentTexts = host.VisibleTexts(current).ToArray();
            Assert.Contains("SEASON 3", currentTexts);
            Assert.Contains("1992", currentTexts);
            Assert.Contains("The Heat Builds", currentTexts);
            Assert.Contains("CURRENT", currentTexts);
            Assert.Equal(1.0, current.Opacity);
            Assert.Equal(new Thickness(2), current.BorderThickness);

            // Locked slots are anonymous ordinal chips: no year, no title, no era, no tooltip.
            // (The backend sends nothing for SMGP futures; the strip must not invent anything.)
            foreach (int index in new[] { 3, 6, 16 })
            {
                Border locked = SlotBorder(slots, index);
                Assert.Equal(
                    new[] { $"SEASON {index + 1}" },
                    host.VisibleTexts(locked).ToArray());
                Assert.True(locked.Opacity < 1, "A locked slot stays muted.");
                Assert.Null(ToolTipService.GetToolTip(locked));
            }

            // No locked-season lore leaks anywhere in the strip.
            Assert.DoesNotContain(host.VisibleTexts(), text => text.Contains("Crown"));
        });
    }

    [Fact]
    public void DynastyArc_PutsThePrologueFirst_AndPreviewsLockedSeasons()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var session = StripSession.Dynasty();
            using var hub = new HubViewModel(session);
            var strip = new CampaignTimelineStrip();
            using var host = Host.Show(strip, 1200, 320, hub);

            var slots = Assert.IsType<ItemsControl>(strip.FindName("CampaignTimelineSlots"));
            Assert.Equal(5, slots.Items.Count);

            // The header counts real seasons only, never the synthetic prologue slot.
            Assert.Contains("4-SEASON CAMPAIGN", host.VisibleTexts());

            // Slot 0 is the distinct Formula Junior prologue card: a coming-soon card, never a
            // playable season (no SEASON 0 ordinal badge).
            string[] prologue = host.VisibleTexts(SlotBorder(slots, 0)).ToArray();
            Assert.Contains("PROLOGUE", prologue);
            Assert.Contains("Formula Junior → 1967", prologue);
            Assert.Contains("Pre-championship prologue, coming soon", prologue);
            Assert.Contains("COMING SOON", prologue);
            Assert.DoesNotContain(host.VisibleTexts(), text => text.Contains("SEASON 0"));

            string[] completed = host.VisibleTexts(SlotBorder(slots, 1)).ToArray();
            Assert.Contains("SEASON 1", completed);
            Assert.Contains("1967", completed);
            Assert.Contains("P3", completed);

            string[] current = host.VisibleTexts(SlotBorder(slots, 2)).ToArray();
            Assert.Contains("SEASON 2", current);
            Assert.Contains("1968", current);
            Assert.Contains("CURRENT", current);

            // A locked Dynasty season is previewed, not hidden: year, series, era + round count.
            Border previewSlot = SlotBorder(slots, 3);
            string[] preview = host.VisibleTexts(previewSlot).ToArray();
            Assert.Contains("SEASON 3", preview);
            Assert.Contains("1969", preview);
            Assert.Contains("Formula 1 World Championship", preview);
            Assert.Contains("1960s · 11 rounds", preview);
            Assert.Equal(1.0, previewSlot.Opacity);

            string[] secondPreview = host.VisibleTexts(SlotBorder(slots, 4)).ToArray();
            Assert.Contains("1970", secondPreview);
            Assert.Contains("1970s · 13 rounds", secondPreview);

            // The pack-level preview rides the slot tooltip (venues + teams on hover); a
            // preview-less slot offers no tooltip.
            var tip = Assert.IsType<CampaignSeasonPreview>(ToolTipService.GetToolTip(previewSlot));
            Assert.Equal(1969, tip.Year);
            Assert.Null(ToolTipService.GetToolTip(SlotBorder(slots, 1)));

            // The tooltip's template really renders the venues + teams the pinned pack declares.
            var template = Assert.IsType<DataTemplate>(
                previewSlot.FindResource(new DataTemplateKey(typeof(CampaignSeasonPreview))));
            var tipContent = Assert.IsAssignableFrom<FrameworkElement>(template.LoadContent());
            tipContent.DataContext = tip;
            tipContent.Measure(new Size(300, double.PositiveInfinity));
            tipContent.Arrange(new Rect(0, 0, 300, 400));
            tipContent.UpdateLayout();
            WpfRenderHarness.Pump(DispatcherPriority.Render);
            string[] tipTexts = Descendants<TextBlock>(tipContent).Select(t => t.Text).ToArray();
            Assert.Contains("Monza", tipTexts);
            Assert.Contains("Spa-Francorchamps", tipTexts);
            Assert.Contains("Scuderia Rossa", tipTexts);
            Assert.Contains("Walker Racing", tipTexts);
        });
    }

    [Fact]
    public void EmptyTimeline_CollapsesTheStrip()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var session = StripSession.Empty();
            using var hub = new HubViewModel(session);
            var strip = new CampaignTimelineStrip();
            using var host = Host.Show(strip, 1200, 320, hub);

            var panel = Assert.IsType<Border>(strip.FindName("CampaignTimelinePanel"));
            Assert.Equal(Visibility.Collapsed, panel.Visibility);
            Assert.Empty(host.VisibleTexts());
        });
    }

    [Fact]
    public void LegacyCareer_RendersTheShortPlayedOnlyArc()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var session = StripSession.Legacy();
            using var hub = new HubViewModel(session);
            var strip = new CampaignTimelineStrip();
            using var host = Host.Show(strip, 900, 320, hub);

            var panel = Assert.IsType<Border>(strip.FindName("CampaignTimelinePanel"));
            var slots = Assert.IsType<ItemsControl>(strip.FindName("CampaignTimelineSlots"));

            Assert.Equal(Visibility.Visible, panel.Visibility);
            Assert.Equal(2, slots.Items.Count);
            string[] texts = host.VisibleTexts().ToArray();
            Assert.Contains("2-SEASON CAMPAIGN", texts);
            Assert.Contains("1988", texts);
            Assert.Contains("P5", texts);
            Assert.Contains("1989", texts);
            Assert.Contains("CHAMPION", texts);
        });
    }

    private static Border SlotBorder(ItemsControl slots, int index)
    {
        var presenter = Assert.IsType<ContentPresenter>(
            slots.ItemContainerGenerator.ContainerFromIndex(index));
        return Assert.IsType<Border>(VisualTreeHelper.GetChild(presenter, 0));
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

    private sealed class Host : IDisposable
    {
        private readonly Window _window;
        private readonly CampaignTimelineStrip _strip;

        private Host(Window window, CampaignTimelineStrip strip)
        {
            _window = window;
            _strip = strip;
        }

        /// <summary>Hosts the strip exactly like the History tab's tear-off fallback does: the
        /// window carries the Hub so the strip's MultiBinding resolves Home.Session off the tag.</summary>
        public static Host Show(CampaignTimelineStrip strip, double width, double height, HubViewModel hub)
        {
            var window = new Window
            {
                Content = strip,
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
            return new Host(window, strip);
        }

        /// <summary>Every effectively-visible text under the strip, or under one slot subtree.</summary>
        public IEnumerable<string> VisibleTexts(DependencyObject? within = null)
        {
            DependencyObject boundary = within ?? _strip;
            return Descendants<TextBlock>(boundary)
                .Where(text => IsEffectivelyVisible(text, boundary))
                .Select(text => text.Text)
                .Where(text => !string.IsNullOrEmpty(text))
                .ToArray();
        }

        public void Dispose()
        {
            _window.Close();
            WpfRenderHarness.Pump(DispatcherPriority.Background);
        }

        private static bool IsEffectivelyVisible(UIElement element, DependencyObject boundary)
        {
            for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is UIElement ui && ui.Visibility != Visibility.Visible)
                    return false;
                if (ReferenceEquals(node, boundary))
                    return true;
            }
            return true;
        }
    }

    /// <summary>A read-side session double: the whole contract the strip needs is a pinned
    /// campaign timeline list plus the minimal surface a HubViewModel construction touches.</summary>
    private sealed class StripSession : ICareerSession
    {
        public required IReadOnlyList<CampaignTimelineEntry> Campaign { get; init; }

        public CareerSummary Summary { get; } = new()
        {
            CareerName = "Timeline Fixture",
            SeasonYear = 1991,
            SeriesName = "Timeline Series",
            CurrentRound = 0,
            RoundCount = 16,
            PlayerDriverId = "driver.player",
            PlayerLiveryName = "vehicle-livery",
            PlayerPosition = null,
        };

        public SeasonPack Pack { get; } = new()
        {
            Manifest = new PackManifest
            {
                PackId = "timeline-render",
                Name = "Timeline Render",
                Version = "1.0.0",
                FormatVersion = 1,
                CareerStyle = "",
            },
            Season = new SeasonDefinition
            {
                Year = 1991,
                SeriesName = "Timeline Series",
                Ams2Class = "f1",
                PointsSystem = new CatalogSeason { RacePoints = [new(9), new(6), new(4)] },
                Rounds = [],
            },
            Teams = [],
            Drivers = [],
            Entries = [],
        };

        public IReadOnlyList<CampaignTimelineEntry> CampaignTimeline() => Campaign;
        public Companion.ViewModels.Services.CareerTimeline CareerTimeline() =>
            Companion.ViewModels.Services.CareerTimeline.Empty;
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

        /// <summary>The real backend's SMGP shape: played/current seasons carry year + lore,
        /// locked futures carry nothing at all (the no-spoiler rule).</summary>
        public static StripSession Smgp(int current, int completed)
        {
            string[] titles = ["The First Summer", "The Open Road", "The Heat Builds"];
            return new StripSession
            {
                Campaign = Enumerable.Range(1, 17)
                    .Select(ordinal =>
                    {
                        bool done = ordinal <= completed;
                        bool active = ordinal == current;
                        return new CampaignTimelineEntry
                        {
                            Ordinal = ordinal,
                            State = done
                                ? CampaignSeasonState.Completed
                                : active
                                    ? CampaignSeasonState.Current
                                    : CampaignSeasonState.Locked,
                            Year = done || active ? 1989 + ordinal : null,
                            Title = done || active ? titles[ordinal - 1] : "",
                            Era = done || active ? "The Iron Circus" : "",
                            PlayerPosition = done ? (ordinal == 2 ? 1 : 2) : null,
                            PlayerChampion = done && ordinal == 2,
                            MissedRounds = done && ordinal == 1,
                        };
                    })
                    .ToArray(),
            };
        }

        /// <summary>The real backend's Dynasty shape: the synthetic Formula Junior prologue heads
        /// the arc, and locked seasons carry their pinned pack's preview.</summary>
        public static StripSession Dynasty() => new()
        {
            Campaign =
            [
                new CampaignTimelineEntry
                {
                    Ordinal = 0,
                    State = CampaignSeasonState.Locked,
                    Title = "Formula Junior → 1967",
                    Era = "Pre-championship prologue, coming soon",
                    IsPrologue = true,
                },
                new CampaignTimelineEntry
                {
                    Ordinal = 1,
                    State = CampaignSeasonState.Completed,
                    Year = 1967,
                    PlayerPosition = 3,
                },
                new CampaignTimelineEntry
                {
                    Ordinal = 2,
                    State = CampaignSeasonState.Current,
                    Year = 1968,
                },
                new CampaignTimelineEntry
                {
                    Ordinal = 3,
                    State = CampaignSeasonState.Locked,
                    Preview = new CampaignSeasonPreview
                    {
                        Year = 1969,
                        SeriesName = "Formula 1 World Championship",
                        EraLabel = "1960s",
                        RoundCount = 11,
                        Venues = ["Monza", "Spa-Francorchamps"],
                        Teams = ["Scuderia Rossa", "Walker Racing"],
                    },
                },
                new CampaignTimelineEntry
                {
                    Ordinal = 4,
                    State = CampaignSeasonState.Locked,
                    Preview = new CampaignSeasonPreview
                    {
                        Year = 1970,
                        SeriesName = "Formula 1 World Championship",
                        EraLabel = "1970s",
                        RoundCount = 13,
                        Venues = ["Kyalami"],
                        Teams = ["Scuderia Rossa", "March Engineering"],
                    },
                },
            ],
        };

        public static StripSession Empty() => new() { Campaign = [] };

        /// <summary>A legacy career with no pinned horizon lists its played seasons only.</summary>
        public static StripSession Legacy() => new()
        {
            Campaign =
            [
                new CampaignTimelineEntry
                {
                    Ordinal = 1,
                    State = CampaignSeasonState.Completed,
                    Year = 1988,
                    PlayerPosition = 5,
                },
                new CampaignTimelineEntry
                {
                    Ordinal = 2,
                    State = CampaignSeasonState.Completed,
                    Year = 1989,
                    PlayerPosition = 1,
                    PlayerChampion = true,
                },
            ],
        };
    }
}
