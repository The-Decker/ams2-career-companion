using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Companion.App.Views;
using Companion.ViewModels.Services;
using Companion.ViewModels.Start;

namespace Companion.RenderHarness.Tests;

/// <summary>
/// Off-screen coverage for the Alpha 1.0 main menu: the authored race photograph, the three
/// explicit career experiences, the unavailable Passport state, and the minimum-window layout.
/// </summary>
public sealed class StartViewRenderTests
{
    private sealed class FakeRecentStore : IRecentCareersStore
    {
        private readonly List<RecentCareer> _careers =
        [
            new()
            {
                Path = @"C:\c\a.ams2career",
                CareerName = "Formula One 1967",
                LastOpenedUtc = DateTimeOffset.UnixEpoch,
                SeasonYear = 1967,
                TerminalState = "deceased",
            },
            new()
            {
                Path = @"C:\c\b.ams2career",
                CareerName = "Super Monaco GP",
                LastOpenedUtc = DateTimeOffset.UnixEpoch,
                SeasonYear = 1988,
                CareerStyle = "smgp",
                TerminalState = "careerOver",
            },
        ];

        public IReadOnlyList<RecentCareer> Load() => _careers;
        public void Touch(string path, string careerName, int seasonYear = 0, string? careerStyle = null) { }
        public void Remove(string path) { }
    }

    [Fact]
    public void StartView_CareerGarageBadgesTerminalCareersWithoutDisablingTheirArchives()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new StartViewModel(new FakeRecentStore());
            var view = new StartView { DataContext = vm };
            Arrange(view, 1920, 1080);

            var garageButton = Assert.IsType<Button>(view.FindName("ModeCareerGarageButton"));
            garageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WpfRenderHarness.Pump();
            view.UpdateLayout();

            var gallery = Assert.IsType<ListBox>(view.FindName("CareerGalleryList"));
            Assert.Equal(2, gallery.Items.Count);
            Assert.Equal(Visibility.Visible,
                Assert.IsType<Border>(view.FindName("CareerGaragePanel")).Visibility);

            gallery.UpdateLayout();
            ListBoxItem[] cards = Enumerable.Range(0, gallery.Items.Count)
                .Select(index => Assert.IsType<ListBoxItem>(
                    gallery.ItemContainerGenerator.ContainerFromIndex(index)))
                .ToArray();
            Assert.All(cards, card => Assert.True(card.IsEnabled));

            Border[] badges = Descendants<Border>(gallery)
                .Where(border => border.Name == "TerminalCareerBadge")
                .ToArray();
            Assert.Equal(2, badges.Length);
            Assert.Contains(badges,
                badge => AutomationProperties.GetName(badge) == "IN MEMORIAM");
            Assert.Contains(badges,
                badge => AutomationProperties.GetName(badge) == "CAREER OVER");
            Assert.All(badges, badge => Assert.Equal(Visibility.Visible, badge.Visibility));
        });
    }

    [Fact]
    public void StartView_RendersExactlyThreeModeCards_WithAccessibleCommandWiring()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new StartViewModel(new FakeRecentStore());
            var view = new StartView { DataContext = vm };
            Border root = Arrange(view, 1920, 1080);

            var modeList = Assert.IsType<ItemsControl>(view.FindName("CareerModeList"));
            Assert.Equal(3, modeList.Items.Count);
            Assert.Equal(
                ["Grand Prix Dynasty", "Super Monaco GP", "Racing Passport"],
                vm.CareerModes.Select(static mode => mode.DisplayName));

            Button[] modeButtons = Descendants<Button>(view)
                .Where(static button => button.DataContext is CareerModeEntry)
                .ToArray();
            Assert.Equal(3, modeButtons.Length);

            for (int index = 0; index < modeButtons.Length; index++)
            {
                Button button = modeButtons[index];
                CareerModeEntry mode = Assert.IsType<CareerModeEntry>(button.DataContext);
                Assert.Same(vm.StartCareerModeCommand, button.Command);
                Assert.Same(mode, button.CommandParameter);
                Assert.Equal(mode.DisplayName, AutomationProperties.GetName(button));
                Assert.Equal(mode.Description, AutomationProperties.GetHelpText(button));
                Assert.Equal(mode.AvailabilityLabel, AutomationProperties.GetItemStatus(button));
                Assert.Equal(mode.PersistenceSummary, button.ToolTip?.ToString());
                Assert.Equal(mode.IsAvailable, button.IsEnabled);
            }

            Assert.True(modeButtons[0].IsEnabled);
            Assert.True(modeButtons[1].IsEnabled);
            Assert.False(modeButtons[2].IsEnabled);

            Button smgpButton = Assert.Single(modeButtons, static button =>
                button.DataContext is CareerModeEntry { Id: "smgp" });
            Image smgpArtwork = Assert.Single(Descendants<Image>(view), static image =>
                image.Name == "SmgpModeArtwork" &&
                image.DataContext is CareerModeEntry { Id: "smgp" });
            var smgpSource = Assert.IsAssignableFrom<BitmapSource>(smgpArtwork.Source);
            Assert.Equal(1800, smgpSource.PixelWidth);
            Assert.Equal(874, smgpSource.PixelHeight);
            Assert.Equal(Stretch.Uniform, smgpArtwork.Stretch);
            Assert.Equal(Visibility.Visible, smgpArtwork.Visibility);
            Assert.InRange(smgpButton.ActualWidth / smgpButton.ActualHeight, 2.03, 2.09);
            Assert.All(modeButtons.Where(static button =>
                    button.DataContext is CareerModeEntry { Id: not "smgp" }),
                button => Assert.True(button.ActualHeight < smgpButton.ActualHeight));
            Assert.DoesNotContain(
                view.InputBindings.OfType<System.Windows.Input.KeyBinding>(),
                static binding => binding.Key == System.Windows.Input.Key.N);

            var backdrop = Assert.IsType<Image>(view.FindName("MainMenuBackdrop"));
            var source = Assert.IsAssignableFrom<BitmapSource>(backdrop.Source);
            Assert.Equal(1672, source.PixelWidth);
            Assert.Equal(941, source.PixelHeight);

            ForceRender(root);
            SaveReviewFrameIfRequested(root);
        });
    }

    [Fact]
    public void StartView_MinimumWindowAt130Percent_HasNoHorizontalOverflow()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new StartViewModel(new FakeRecentStore());
            var view = new StartView
            {
                DataContext = vm,
                LayoutTransform = new ScaleTransform(1.30, 1.30),
            };
            Border root = Arrange(view, 920, 620);
            ForceRender(root);

            var scroller = Assert.IsType<ScrollViewer>(view.FindName("MainMenuModeStage"));
            var modeList = Assert.IsType<ItemsControl>(view.FindName("CareerModeList"));
            Assert.Equal(3, modeList.Items.Count);
            Assert.Equal(ScrollBarVisibility.Disabled, scroller.HorizontalScrollBarVisibility);
            Assert.True(scroller.ViewportWidth > 0);
            Assert.True(
                scroller.ExtentWidth <= scroller.ViewportWidth + 1,
                $"Main menu overflowed horizontally: extent {scroller.ExtentWidth:F1}, viewport {scroller.ViewportWidth:F1}.");
            Assert.True(modeList.ActualWidth > 0);
            Assert.True(modeList.ActualHeight > 0);
        });
    }

    [Fact]
    public void StartView_2560x1440At160Percent_KeepsEveryModeInTheLeftRail()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new StartViewModel(new FakeRecentStore());
            var view = new StartView
            {
                DataContext = vm,
                LayoutTransform = new ScaleTransform(1.60, 1.60),
            };
            Border root = Arrange(view, 2560, 1440);
            ForceRender(root);
            SaveReviewFrameIfRequested(root);

            var rail = Assert.IsType<Border>(view.FindName("MainMenuCommandRail"));
            var scroller = Assert.IsType<ScrollViewer>(view.FindName("MainMenuModeStage"));
            var modeList = Assert.IsType<ItemsControl>(view.FindName("CareerModeList"));
            Button[] modeButtons = Descendants<Button>(view)
                .Where(static button => button.DataContext is CareerModeEntry)
                .ToArray();

            Assert.Equal(3, modeButtons.Length);
            Assert.Equal(3, modeList.Items.Count);
            Assert.InRange(rail.ActualWidth, 430, 520);
            Assert.True(rail.ActualWidth < view.ActualWidth * .45,
                $"The {rail.ActualWidth:F1}px command rail occupied the center of a {view.ActualWidth:F1}px menu.");
            Assert.Equal(ScrollBarVisibility.Disabled, scroller.HorizontalScrollBarVisibility);
            Assert.True(scroller.ExtentWidth <= scroller.ViewportWidth + 1,
                $"Main menu overflowed horizontally: extent {scroller.ExtentWidth:F1}, viewport {scroller.ViewportWidth:F1}.");
            Assert.All(modeButtons, button =>
            {
                Assert.True(button.ActualWidth > 0);
                Assert.True(button.ActualHeight > 0);
                Assert.True(button.ActualWidth <= rail.ActualWidth);
            });
        });
    }

    [Fact]
    public void MainWindow_HeaderLeavesTheTopMiddleEmpty()
    {
        string path = Path.Combine(FindRepositoryRoot(), "src", "Companion.App", "MainWindow.xaml");
        XDocument document = XDocument.Load(path);
        XElement player = Assert.Single(
            document.Descendants(),
            static element => element.Name.LocalName == "MusicPlayerControl");

        Assert.Equal("Right", player.Attributes().Single(
            static attribute => attribute.Name.LocalName == "DockPanel.Dock").Value);
        Assert.Equal("Right", player.Attributes().Single(
            static attribute => attribute.Name.LocalName == "HorizontalAlignment").Value);
    }

    [Fact]
    public void MainMenuAsset_ReplacesGeneratedArtworkInProjectContract()
    {
        string root = FindRepositoryRoot();
        string appRoot = Path.Combine(root, "src", "Companion.App");
        string newAsset = Path.Combine(appRoot, "Assets", "MainMenu", "main-menu.png");
        string smgpModeAsset = Path.Combine(appRoot, "Assets", "MainMenu", "smgp-mode.jpg");
        string canonicalSmgpAsset = Path.Combine(root, "data", "ams2", "era-art", "smgp.jpg");
        string oldAsset = Path.Combine(appRoot, "Assets", "MainMenu", "grand-prix-blue-hour.png");
        string project = File.ReadAllText(Path.Combine(appRoot, "Companion.App.csproj"));
        string view = File.ReadAllText(Path.Combine(appRoot, "Views", "StartView.xaml"));

        Assert.True(File.Exists(newAsset));
        Assert.True(File.Exists(smgpModeAsset));
        Assert.Equal(File.ReadAllBytes(canonicalSmgpAsset), File.ReadAllBytes(smgpModeAsset));
        Assert.False(File.Exists(oldAsset));
        Assert.Contains(@"Assets\MainMenu\main-menu.png", project, StringComparison.Ordinal);
        Assert.Contains(@"Assets\MainMenu\smgp-mode.jpg", project, StringComparison.Ordinal);
        Assert.DoesNotContain("grand-prix-blue-hour.png", project, StringComparison.Ordinal);
        Assert.Contains("Assets/MainMenu/main-menu.png", view, StringComparison.Ordinal);
        Assert.Contains("Assets/MainMenu/smgp-mode.jpg", view, StringComparison.Ordinal);
        Assert.DoesNotContain("grand-prix-blue-hour.png", view, StringComparison.Ordinal);
        Assert.DoesNotContain("NewCareerCommand", view, StringComparison.Ordinal);
    }

    private static Border Arrange(StartView view, double width, double height)
    {
        var root = new Border
        {
            Width = width,
            Height = height,
            Child = view,
        };
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        return root;
    }

    private static void ForceRender(FrameworkElement element)
    {
        int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
    }

    private static void SaveReviewFrameIfRequested(FrameworkElement element)
    {
        string? path = Environment.GetEnvironmentVariable("AMS2_MAIN_MENU_REVIEW_PNG");
        if (string.IsNullOrWhiteSpace(path))
            return;

        int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
                yield return typed;
            foreach (T descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Companion.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Could not find Companion.slnx above '{AppContext.BaseDirectory}'.");
    }
}
