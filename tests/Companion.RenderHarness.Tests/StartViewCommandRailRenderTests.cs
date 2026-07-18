using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Companion.App.Views;
using Companion.ViewModels.Services;
using Companion.ViewModels.Start;

namespace Companion.RenderHarness.Tests;

/// <summary>The Pit Wall Command Rail acceptance pass (smgp-alpha-finish-status.md blocker 3):
/// INDEPENDENT renders of the main menu at all four required size/scale combinations — both
/// themes at the two extreme UI scales — proving the left command rail never clips, never
/// scrolls sideways, and keeps every command accessible. Each combo also writes a review frame
/// to scratchpad/review-frames/ for Mike's sign-off (run the tests, open the four PNGs).
/// </summary>
public sealed class StartViewCommandRailRenderTests
{
    [Theory]
    [InlineData("Dark", "RoyalBlue", 1.00)]
    [InlineData("Dark", "RoyalBlue", 1.50)]
    [InlineData("Light", "Gold", 1.00)]
    [InlineData("Light", "Gold", 1.50)]
    public void MainMenuCommandRail_FitsEveryRequiredThemeAndScale(string theme, string accent, double scale)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new StartViewModel(new EmptyRecentStore());
            var view = new StartView { DataContext = vm };
            view.Resources.MergedDictionaries.Add(LoadThemeDictionary($"Theme.{theme}.xaml"));
            view.Resources.MergedDictionaries.Add(
                LoadThemeDictionary($"Accents/{theme}/Accent.{accent}.xaml"));
            view.LayoutTransform = new ScaleTransform(scale, scale);

            var root = new Border { Width = 1920, Height = 1080, Child = view };
            root.Measure(new Size(root.Width, root.Height));
            root.Arrange(new Rect(0, 0, root.Width, root.Height));
            root.UpdateLayout();

            // The rail exists and never overflows the window horizontally.
            var rail = Assert.IsAssignableFrom<FrameworkElement>(view.FindName("MainMenuCommandRail"));
            Assert.True(rail.ActualWidth > 0 && rail.ActualHeight > 0);
            Rect railBounds = rail.TransformToAncestor(view)
                .TransformBounds(new Rect(new Point(), rail.RenderSize));
            Assert.True(railBounds.Left >= -1,
                $"Rail clipped on the left in {theme} at {scale:P0}.");
            Assert.True(railBounds.Right <= view.ActualWidth + 1,
                $"Rail overflowed the window in {theme} at {scale:P0} ({railBounds.Right:0.#} > {view.ActualWidth:0.#}).");

            // The rail scrolls VERTICALLY when crowded, never sideways.
            foreach (var scroller in Descendants<ScrollViewer>(view))
            {
                Assert.Equal(ScrollBarVisibility.Disabled, scroller.HorizontalScrollBarVisibility);
                Assert.True(
                    scroller.ExtentWidth <= scroller.ViewportWidth + 1,
                    $"A rail scroller overflowed horizontally in {theme} at {scale:P0} " +
                    $"({scroller.ExtentWidth:0.#} > {scroller.ViewportWidth:0.#}).");
            }

            // All three mode cards render with accessible names (IN DEVELOPMENT for Dynasty,
            // PLAYABLE NOW for SMGP and Racing Passport).
            Button[] modeButtons = Descendants<Button>(view)
                .Where(button => button.DataContext is CareerModeEntry)
                .ToArray();
            Assert.Equal(3, modeButtons.Length);
            Assert.All(modeButtons, button =>
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button))));
            Assert.Contains(modeButtons, button =>
                AutomationProperties.GetItemStatus(button).Contains("PLAYABLE NOW", StringComparison.Ordinal));

            SaveReviewFrame(root, theme, scale);
        });
    }

    /// <summary>Writes the combo's frame to scratchpad/review-frames/ for Mike's sign-off.</summary>
    private static void SaveReviewFrame(FrameworkElement element, string theme, double scale)
    {
        int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string directory = Path.Combine(RepositoryRoot(), "scratchpad", "review-frames");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(
            directory, $"command-rail-{theme.ToLowerInvariant()}-{scale:0.00}.png");
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Companion.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Companion.slnx not found above the test output.");
    }

    private static ResourceDictionary LoadThemeDictionary(string relativePath) =>
        Assert.IsType<ResourceDictionary>(Application.LoadComponent(
            new Uri($"/AMS2CareerCompanion;component/Themes/{relativePath}", UriKind.Relative)));

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

    private sealed class EmptyRecentStore : IRecentCareersStore
    {
        public IReadOnlyList<RecentCareer> Load() => [];
        public void Touch(string path, string careerName, int seasonYear = 0, string? careerStyle = null) { }
        public void Remove(string path) { }
    }
}
