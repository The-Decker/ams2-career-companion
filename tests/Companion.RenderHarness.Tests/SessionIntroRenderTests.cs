using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Companion.App.Views;
using Companion.ViewModels.Shell;

namespace Companion.RenderHarness.Tests;

/// <summary>Binding, asset, theme and compact-layout guard for both cinematic session gates.</summary>
public sealed class SessionIntroRenderTests
{
    [Theory]
    [InlineData(SessionIntroKind.Qualifying, "qualifying", "QUALIFYING", "Begin qualifying",
        "AccentBrush", "AccentBrush")]
    [InlineData(SessionIntroKind.Race, "race", "RACE", "Start the race",
        "ErrorBrush", "WarningBrush")]
    public void SessionIntroView_RendersBoundVariantAndContinuesOnce(
        SessionIntroKind kind,
        string artworkKey,
        string expectedTitle,
        string expectedAction,
        string railBrushKey,
        string actionBrushKey)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            int continues = 0;
            const string subtitle = "Round 4 of 16  ·  Circuit de Monaco  ·  Clear";
            var vm = new SessionIntroViewModel(kind, subtitle, () => continues++);
            var view = new SessionIntroView { DataContext = vm };

            view.Measure(new Size(1200, 680));
            view.Arrange(new Rect(0, 0, 1200, 680));
            view.UpdateLayout();

            Assert.Equal(1200, view.ActualWidth);
            Assert.Equal(680, view.ActualHeight);

            var artwork = Assert.IsType<Image>(view.FindName("SessionArtwork"));
            Assert.NotNull(artwork.Source);
            Assert.Contains($"SessionIntros/{artworkKey}.png", artwork.Source.ToString(),
                StringComparison.OrdinalIgnoreCase);

            var title = Assert.IsType<TextBlock>(view.FindName("SessionTitle"));
            var subtitleText = Assert.IsType<TextBlock>(view.FindName("SessionSubtitle"));
            var rail = Assert.IsType<Border>(view.FindName("ModeRail"));
            var signal = Assert.IsType<Border>(view.FindName("ModeSignal"));
            var glyph = Assert.IsType<TextBlock>(view.FindName("ModeGlyph"));
            var button = Assert.IsType<Button>(view.FindName("ContinueButton"));
            var root = Assert.IsType<Grid>(view.FindName("SessionIntroRoot"));

            Assert.Equal(expectedTitle, title.Text);
            Assert.Equal(subtitle, subtitleText.Text);
            Assert.Equal(kind == SessionIntroKind.Race ? "R" : "Q", glyph.Text);
            Assert.Equal(expectedAction, AutomationProperties.GetName(button));
            Assert.Equal("Continue to the session entry screen", AutomationProperties.GetHelpText(button));
            Assert.True(button.IsDefault);
            Assert.Same(button, FocusManager.GetFocusedElement(root));

            AssertBrushMatches(rail.Background, railBrushKey);
            AssertBrushMatches(button.Background, actionBrushKey);
            AssertBrushMatches(signal.BorderBrush,
                kind == SessionIntroKind.Race ? "WarningBrush" : "AccentBrush");

            Assert.True(button.Command.CanExecute(button.CommandParameter));
            button.Command.Execute(button.CommandParameter);
            Assert.False(button.Command.CanExecute(button.CommandParameter));
            button.Command.Execute(button.CommandParameter);
            Assert.Equal(1, continues);
        });
    }

    [Theory]
    [InlineData(SessionIntroKind.Qualifying)]
    [InlineData(SessionIntroKind.Race)]
    public void SessionIntroView_KeepsTheActionVisibleAtCompactRaceLoopViewport(SessionIntroKind kind)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new SessionIntroViewModel(
                kind,
                "Round 16 of 16  ·  Monaco Grand Prix  ·  Championship finale",
                () => { });
            var view = new SessionIntroView { DataContext = vm };

            view.Measure(new Size(560, 360));
            view.Arrange(new Rect(0, 0, 560, 360));
            view.UpdateLayout();

            var button = Assert.IsType<Button>(view.FindName("ContinueButton"));
            Point topLeft = button.TranslatePoint(new Point(), view);

            Assert.Equal(560, view.ActualWidth);
            Assert.Equal(360, view.ActualHeight);
            Assert.True(button.ActualWidth >= 226);
            Assert.True(button.ActualHeight >= 48);
            Assert.InRange(topLeft.X, 0, view.ActualWidth - button.ActualWidth);
            Assert.InRange(topLeft.Y, 0, view.ActualHeight - button.ActualHeight);
        });
    }

    [Theory]
    [InlineData(SessionIntroKind.Qualifying)]
    [InlineData(SessionIntroKind.Race)]
    public void SessionIntroView_ContinueRemainsInsideViewportAtMaximumUiScale(SessionIntroKind kind)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            const double scale = 1.30;
            var view = new SessionIntroView
            {
                DataContext = new SessionIntroViewModel(
                    kind,
                    "Round 16 of 16  ·  Monaco Grand Prix  ·  Championship finale",
                    () => { }),
            };
            var scaledShell = new Border
            {
                LayoutTransform = new ScaleTransform(scale, scale),
                Child = view,
            };
            var viewport = new Grid();
            viewport.Children.Add(scaledShell);

            // This is the physical equivalent of the compact 560x360 race-loop content area at
            // the app's maximum 130% root LayoutTransform.
            viewport.Measure(new Size(728, 468));
            viewport.Arrange(new Rect(0, 0, 728, 468));
            viewport.UpdateLayout();

            var button = Assert.IsType<Button>(view.FindName("ContinueButton"));
            Point topLeft = button.TranslatePoint(new Point(), viewport);
            Point bottomRight = button.TranslatePoint(
                new Point(button.ActualWidth, button.ActualHeight), viewport);

            Assert.Equal(728, viewport.ActualWidth);
            Assert.Equal(468, viewport.ActualHeight);
            Assert.InRange(topLeft.X, 0, viewport.ActualWidth);
            Assert.InRange(topLeft.Y, 0, viewport.ActualHeight);
            Assert.InRange(bottomRight.X, 0, viewport.ActualWidth);
            Assert.InRange(bottomRight.Y, 0, viewport.ActualHeight);
        });
    }

    [Fact]
    public void SessionIntroAssets_ArePackagedOnceAndAppTemplateIsRegistered()
    {
        string root = FindRepositoryRoot();
        string appRoot = Path.Combine(root, "src", "Companion.App");
        string project = File.ReadAllText(Path.Combine(appRoot, "Companion.App.csproj"));
        string appXaml = File.ReadAllText(Path.Combine(appRoot, "App.xaml"));

        Assert.Contains("<Resource Include=\"Assets\\SessionIntros\\*.png\" />", project,
            StringComparison.Ordinal);
        Assert.DoesNotContain("<None Include=\"Assets\\SessionIntros", project,
            StringComparison.Ordinal);
        Assert.Contains("DataType=\"{x:Type shell:SessionIntroViewModel}\"", appXaml,
            StringComparison.Ordinal);
        Assert.Contains("<views:SessionIntroView />", appXaml, StringComparison.Ordinal);

        foreach (string name in new[] { "qualifying.png", "race.png" })
        {
            string path = Path.Combine(appRoot, "Assets", "SessionIntros", name);
            Assert.True(File.Exists(path), $"Missing generated session-intro art: {path}");
            Assert.True(new FileInfo(path).Length > 100_000,
                $"Expected production-resolution session-intro art, but {name} is unexpectedly small.");
        }
    }

    private static void AssertBrushMatches(Brush actual, string resourceKey)
    {
        var actualSolid = Assert.IsType<SolidColorBrush>(actual);
        var expected = Assert.IsType<SolidColorBrush>(Application.Current.FindResource(resourceKey));
        Assert.Equal(expected.Color, actualSolid.Color);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Companion.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output.");
    }
}
