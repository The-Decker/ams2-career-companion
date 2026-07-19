using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Companion.App.Views;
using Companion.ViewModels.Hub;

namespace Companion.RenderHarness.Tests;

/// <summary>
/// Off-screen render tests for the generic tear-off <see cref="TabWindow"/> (the hub-rail ⧉ pop-out
/// for Standings / History / Driver / Skins). These pin the load-bearing behaviour that makes the
/// pop-out safe for a lens whose view-model is REBUILT each round (Standings): the window binds the
/// stable TAB and hosts its <c>Content</c>, so swapping the tab's content, exactly what the hub
/// does on Apply, updates the pop-out live. Self-skips on a non-Windows / non-STA host.
/// </summary>
public sealed class TabWindowRenderTests
{
    /// <summary>A throwaway lens view-model stand-in, no DataTemplate is needed, the test asserts on
    /// the ContentControl's <c>Content</c> property (the binding target), not a rendered visual.</summary>
    private sealed class DummyVm : ObservableObject;

    [Fact]
    public void TabWindow_BindsTheTabTitle_HostsItsContent_AndFollowsAContentSwapLive()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var first = new DummyVm();
            var rebuilt = new DummyVm();
            var tab = new HubTabViewModel(HubViewModel.StandingsTabKey, "Standings", "", first);

            var window = new TabWindow
            {
                DataContext = tab,
                ShowActivated = false,
                Left = -10000,
                Top = -10000,
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                WpfRenderHarness.Pump(DispatcherPriority.Loaded);

                Assert.Equal("Standings", window.Title); // title binds to the tab

                var content = Descendants<ContentControl>(window).First();
                Assert.Same(first, content.Content); // hosts the tab's current content

                // The hub rebuilds the Standings view-model on every applied round; because the pop-out
                // binds the stable tab, that swap must flow through live.
                tab.Content = rebuilt;
                window.UpdateLayout();
                WpfRenderHarness.Pump();
                Assert.Same(rebuilt, content.Content);
            }
            finally
            {
                window.Close();
                WpfRenderHarness.Pump(DispatcherPriority.Background);
            }
        });
    }

    [Fact]
    public void RaceAndNews_DoNotOfferTheRailPopOut_ButTheLensesDo()
    {
        // Pure view-model gate (no render needed): the Race tab is the loop and News keeps its own
        // in-view pop-out, so neither shows the rail ⧉; the read-only lenses do.
        Assert.False(new HubTabViewModel(HubViewModel.RaceTabKey, "Race", "", new DummyVm()).CanPopOut);
        Assert.False(new HubTabViewModel(HubViewModel.NewsTabKey, "News", "", new DummyVm()).CanPopOut);
        Assert.True(new HubTabViewModel(HubViewModel.StandingsTabKey, "Standings", "", new DummyVm()).CanPopOut);
        Assert.True(new HubTabViewModel(HubViewModel.HistoryTabKey, "History", "", new DummyVm()).CanPopOut);
        Assert.True(new HubTabViewModel(HubViewModel.DriverTabKey, "Driver", "", new DummyVm()).CanPopOut);
        Assert.True(new HubTabViewModel(HubViewModel.SkinsTabKey, "Skins", "", new DummyVm()).CanPopOut);
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
