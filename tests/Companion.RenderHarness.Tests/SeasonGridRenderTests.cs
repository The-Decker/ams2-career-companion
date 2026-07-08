using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Companion.App.Views;
using Companion.ViewModels.Services;
using Companion.ViewModels.Wizard;

namespace Companion.RenderHarness.Tests;

/// <summary>
/// Off-screen render tests for the New-career SEASON-PICK grid (Mike's ask: "4 cards per row so
/// 4 columns of seasons", cards resizing with the screen). Drives the REAL <see cref="WizardView"/>
/// over a minimal stand-in DataContext parked on the Season step, so these assert the view-layer
/// layout that only a live render exposes: the season cards flow through a 4-column
/// <see cref="UniformGrid"/> and each year-pic hero keeps its 16:9 aspect as the responsive column
/// flexes. WPF binding failures are non-fatal, so a tiny POCO (Step + Packs) is enough to render the
/// panel without the wizard's full environment/factory. Self-skips on a non-Windows / non-STA host.
/// </summary>
public sealed class SeasonGridRenderTests
{
    /// <summary>The exact 16:9 ratio the season hero binds through <c>AspectHeight</c>.</summary>
    private const double Aspect = 0.5625;

    /// <summary>Just enough of the wizard VM's surface for the season-pick panel to render: the
    /// step it is on and the discovered packs. Everything else the view binds (commands, character
    /// step, errors) fails silently, which is fine for a static render.</summary>
    private sealed class SeasonPickStub
    {
        public WizardStep Step => WizardStep.SeasonPick;
        public bool HasCharacterStep => true;
        public string? PackLoadError => null;
        public ObservableCollection<DiscoveredPack> Packs { get; }
        public DiscoveredPack? SelectedPack { get; set; }

        public SeasonPickStub(params string[] titles)
        {
            Packs = [.. titles.Select(t => new DiscoveredPack { Directory = @"C:\fake\" + t })];
        }
    }

    private static SeasonPickStub SixSeasons() => new(
        "Formula One 1967", "Formula One 1974", "Formula One 1985", "Formula One 1988",
        "Formula One 1991", "Formula One 2000");

    [Fact]
    public void SeasonPickCards_FlowThroughAFourColumnUniformGrid()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            using var host = Host.Show(SixSeasons(), width: 1400);

            var grid = host.Descendants<UniformGrid>().FirstOrDefault();
            Assert.NotNull(grid);
            Assert.Equal(4, grid!.Columns); // Mike's ask: 4 columns of seasons
        });
    }

    [Fact]
    public void SeasonCardHero_KeepsSixteenNineAspect_AsTheColumnFlexes()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            using var host = Host.Show(SixSeasons(), width: 1400);

            // The hero is the only Grid carrying the aspect floor (MinHeight 120); after layout its
            // height must track its own width at 16:9 (well above the 120 floor at this width).
            var hero = host.Descendants<Grid>()
                .FirstOrDefault(g => Math.Abs(g.MinHeight - 120) < 0.01 && g.ActualWidth > 0);
            Assert.NotNull(hero);
            Assert.True(hero!.ActualWidth > 120,
                $"At a 1400px window / 4 columns the card hero should be wide; got {hero.ActualWidth:0}.");
            Assert.True(Math.Abs(hero.ActualHeight - hero.ActualWidth * Aspect) <= 1.5,
                $"Hero should be 16:9: width {hero.ActualWidth:0} → expected height " +
                $"{hero.ActualWidth * Aspect:0.0}, got {hero.ActualHeight:0.0}.");
        });
    }

    [Fact]
    public void SeasonCards_StretchToEqualWidths_FillingTheRow()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            using var host = Host.Show(SixSeasons(), width: 1400);

            // Every hero (one per card) should have stretched to the same column width — proof the
            // cards flex to fill rather than sitting at the old fixed 248px.
            var widths = host.Descendants<Grid>()
                .Where(g => Math.Abs(g.MinHeight - 120) < 0.01 && g.ActualWidth > 0)
                .Select(g => g.ActualWidth)
                .ToArray();
            Assert.Equal(6, widths.Length);
            Assert.All(widths, w => Assert.True(Math.Abs(w - widths[0]) < 0.5, "cards are not equal width"));
            Assert.True(widths[0] > 248, $"a 4-of-1400 column should exceed the old 248px card; got {widths[0]:0}.");
        });
    }

    // ---------- an off-screen host for one WizardView over a stub ----------

    private sealed class Host : IDisposable
    {
        private readonly Window _window;
        public WizardView View { get; }

        private Host(Window window, WizardView view)
        {
            _window = window;
            View = view;
        }

        public static Host Show(object dataContext, double width)
        {
            var view = new WizardView { DataContext = dataContext };
            var window = new Window
            {
                Content = view,
                Width = width,
                Height = 900,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -10000,
                Top = -10000,
            };
            window.Show();
            window.UpdateLayout();
            WpfRenderHarness.Pump(DispatcherPriority.Loaded);
            WpfRenderHarness.Pump(DispatcherPriority.Render);
            window.UpdateLayout();
            WpfRenderHarness.Pump();
            return new Host(window, view);
        }

        public IEnumerable<T> Descendants<T>() where T : DependencyObject => Descendants<T>(View);

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

        public void Dispose()
        {
            _window.Close();
            WpfRenderHarness.Pump(DispatcherPriority.Background);
        }
    }
}
