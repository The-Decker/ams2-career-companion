using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.App.Audio;
using Companion.App.Views;
using Companion.ViewModels.Hub;

namespace Companion.RenderHarness.Tests;

public sealed partial class HubRailRenderTests
{
    [Fact]
    public void HubRail_At130Percent_RemainsScrollableFocusableAndPersistentlySelected()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var host = new HubRailHost();
            var view = new HubView
            {
                DataContext = host,
                LayoutTransform = new ScaleTransform(1.3, 1.3),
            };
            view.Measure(new Size(1000, 520));
            view.Arrange(new Rect(0, 0, 1000, 520));
            view.UpdateLayout();

            var rail = Assert.IsType<Border>(view.FindName("HubRail"));
            var scroll = Assert.IsType<ScrollViewer>(view.FindName("HubRailScrollViewer"));
            var levelChip = Assert.IsType<Border>(view.FindName("DriverLevelChip"));
            var availabilityChip = Assert.IsType<Border>(view.FindName("DriverAvailabilityChip"));
            Button[] destinations = Descendants<Button>(view)
                .Where(button => AutomationProperties.GetHelpText(button) ==
                    "Select this career workspace")
                .ToArray();
            Path[] destinationIcons = Descendants<Path>(view)
                .Where(path => path.Name == "RailDestinationIcon")
                .ToArray();

            Assert.InRange(rail.ActualWidth, 176, 224);
            Assert.Equal(Visibility.Visible, levelChip.Visibility);
            Assert.Equal(Visibility.Visible, availabilityChip.Visibility);
            Assert.True(levelChip.ActualWidth > 0);
            Assert.True(availabilityChip.ActualWidth > 0);
            Assert.True(scroll.ActualHeight > 0);
            Assert.True(scroll.ScrollableHeight > 0,
                "The compact 130% viewport must scroll instead of clipping a rail destination.");
            Assert.Equal(host.Tabs.Count, destinations.Length);
            Assert.All(destinations, button =>
            {
                Assert.True(button.ActualWidth > 0);
                Assert.True(button.ActualHeight >= 38);
                Assert.True(button.Focusable);
                Assert.NotNull(button.Command);
                Assert.Equal(SoundEffectCue.Navigate, SoundAssist.GetCue(button));
            });
            Assert.Equal(host.Tabs.Count, destinationIcons.Length);
            Assert.Equal(host.Tabs.Count,
                destinationIcons.Select(icon => icon.Data.ToString())
                    .Distinct(StringComparer.Ordinal)
                    .Count());
            Assert.All(destinationIcons, icon =>
            {
                Assert.False(icon.Data.Bounds.IsEmpty);
                Assert.True(icon.ActualWidth > 0);
                Assert.True(icon.ActualHeight > 0);
            });

            Button first = destinations[0];
            Button second = destinations[1];
            Assert.True(SoundAssist.GetSuppressWhen(first));
            Assert.False(SoundAssist.GetSuppressWhen(second));
            string selectedBrush = first.Background.ToString();
            Assert.NotEqual(selectedBrush, second.Background.ToString());

            second.Command!.Execute(second.CommandParameter);
            WpfRenderHarness.Pump();
            view.UpdateLayout();

            Assert.False(SoundAssist.GetSuppressWhen(first));
            Assert.True(SoundAssist.GetSuppressWhen(second));
            Assert.Equal(selectedBrush, second.Background.ToString());
            Assert.Same(host.Tabs[1], host.SelectedTab);
        });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (T descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private sealed class DummyContent : ObservableObject;

    private sealed partial class HubRailHost : ObservableObject
    {
        public HubRailHost()
        {
            Tabs =
            [
                Tab(HubViewModel.RaceTabKey, "Upcoming Race", "\uE7C1"),
                Tab(HubViewModel.StandingsTabKey, "Standings", "\uE9D9"),
                Tab(HubViewModel.DriverTabKey, "Driver", "\uE77B"),
                Tab(HubViewModel.CalendarTabKey, "Calendar", "\uE787"),
                Tab(HubViewModel.SkinsTabKey, "Grid Preview", "\uE790"),
                Tab(HubViewModel.HistoryTabKey, "History", "\uE81C"),
                Tab(HubViewModel.PaddockTabKey, "Paddock", "\uE716"),
                Tab(HubViewModel.NewsTabKey, "News", "\uE789"),
            ];
            _selectedTab = Tabs[0];
            Tabs[0].IsSelected = true;
            SelectTabCommand = new RelayCommand<HubTabViewModel>(SelectTab);
        }

        public HomeHost Home { get; } = new();
        public PaddockHost Paddock { get; } = new();
        public EraHost Era { get; } = new();
        public NewsHost News { get; } = new();
        public bool EraThemingEnabled => true;
        public ObservableCollection<HubTabViewModel> Tabs { get; }
        public IRelayCommand<HubTabViewModel> SelectTabCommand { get; }

        [ObservableProperty]
        private HubTabViewModel? _selectedTab;

        private void SelectTab(HubTabViewModel? tab)
        {
            if (tab is null)
                return;
            foreach (HubTabViewModel candidate in Tabs)
                candidate.IsSelected = ReferenceEquals(candidate, tab);
            SelectedTab = tab;
        }

        private static HubTabViewModel Tab(string key, string title, string glyph) =>
            new(key, title, glyph, new DummyContent());
    }

    private sealed class HomeHost
    {
        public object? CareerOver => null;
        public BriefingHost Briefing { get; } = new();
        public object? Session => null;
        public string SeasonYearText => "1990";
        public string HeaderTitle => "SUPER MONACO GP";
        public string RoundText => "ROUND 1";
        public string StandingText => "P12";
        public string FormText => "";
        public bool HasForm => false;
        public string DriverLevelText => "LV 137";
        public string DriverAvailabilityLabel => "Injured — out 2 races";
    }

    private sealed class BriefingHost { public bool SmgpCareerOver => false; }
    private sealed class PaddockHost { public bool HasPaddock => false; }
    private sealed class EraHost { public string Label => "THE TURBO YEARS"; }
    private sealed class NewsHost { public IReadOnlyList<object> Items { get; } = []; }
}
