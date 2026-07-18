using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Companion.App.Views;
using Companion.ViewModels.Briefing;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;

namespace Companion.RenderHarness.Tests;

public sealed class SmgpRivalBannerScaleRenderTests
{
    [Theory]
    [InlineData("Dark", "RoyalBlue", 1.00)]
    [InlineData("Dark", "RoyalBlue", 1.25)]
    [InlineData("Dark", "RoyalBlue", 1.50)]
    [InlineData("Light", "Gold", 1.00)]
    [InlineData("Light", "Gold", 1.25)]
    [InlineData("Light", "Gold", 1.50)]
    public void RivalScreenView_HeadquartersBannerFitsSupportedThemesAndUiScales(
        string theme,
        string accent,
        double scale)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            Type? discoveredSessionType = typeof(BriefingSmgpRenderTests).GetNestedType(
                "SmgpSession",
                BindingFlags.NonPublic);
            Assert.NotNull(discoveredSessionType);
            Type sessionType = discoveredSessionType;
            var session = Assert.IsAssignableFrom<ICareerSession>(Activator.CreateInstance(sessionType));
            var briefing = new BriefingViewModel(session);
            briefing.SelectedSmgpRival = briefing.SmgpRivals[0];
            var view = new RivalScreenView { DataContext = new RivalScreenViewModel(briefing) };
            view.Resources.MergedDictionaries.Add(LoadThemeDictionary($"Theme.{theme}.xaml"));
            view.Resources.MergedDictionaries.Add(
                LoadThemeDictionary($"Accents/{theme}/Accent.{accent}.xaml"));
            view.LayoutTransform = new ScaleTransform(scale, scale);

            var root = new Border
            {
                Width = 1600,
                Height = 900,
                Child = view,
            };
            root.Measure(new Size(root.Width, root.Height));
            root.Arrange(new Rect(0, 0, root.Width, root.Height));
            root.UpdateLayout();

            ScrollViewer scrollViewer = Assert.Single(Descendants<ScrollViewer>(view));
            Assert.Equal(ScrollBarVisibility.Disabled, scrollViewer.HorizontalScrollBarVisibility);
            Assert.True(scrollViewer.ViewportWidth > 0);
            Assert.True(scrollViewer.ExtentWidth <= scrollViewer.ViewportWidth + 1,
                $"Rival content overflowed horizontally in {theme} at {scale:P0}.");

            Image banner = Assert.Single(Descendants<Image>(view), image =>
                image.Source is BitmapSource bitmap
                && bitmap.PixelWidth == 1040
                && bitmap.PixelHeight == 200);
            Rect bannerBounds = banner.TransformToAncestor(view)
                .TransformBounds(new Rect(new Point(), banner.RenderSize));
            Assert.True(bannerBounds.Width > 0);
            Assert.True(bannerBounds.Left >= -1);
            Assert.True(bannerBounds.Right <= view.ActualWidth + 1,
                $"Headquarters banner overflowed in {theme} at {scale:P0}.");
        });
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
}
