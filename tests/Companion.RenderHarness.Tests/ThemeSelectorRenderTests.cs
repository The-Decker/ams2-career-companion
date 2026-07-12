using System.Windows;
using Companion.ViewModels.Settings;

namespace Companion.RenderHarness.Tests;

/// <summary>Guards the App↔theme-file contract: every base theme (dark/light) and every named accent the
/// settings/appearance service can select must resolve to a real ResourceDictionary at the exact pack URI
/// App.ApplyTheme builds. A renamed/removed accent or a new AppSettings.AccentNames entry without a file
/// fails here rather than at runtime. Self-skips off Windows.</summary>
public sealed class ThemeSelectorRenderTests
{
    private const string Root = "/AMS2CareerCompanion;component/Themes/";

    private static ResourceDictionary Load(string relativePath) =>
        (ResourceDictionary)Application.LoadComponent(new Uri(Root + relativePath, UriKind.Relative));

    [Fact]
    public void EverySelectableThemeAndAccent_ResolvesToARealDictionary()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            foreach (var baseName in new[] { "Dark", "Light" })
            {
                Assert.NotNull(Load($"Theme.{baseName}.xaml"));
                foreach (var accent in AppSettings.AccentNames)
                    Assert.NotNull(Load($"Accents/{baseName}/Accent.{accent}.xaml"));
            }
        });
    }

    [Fact]
    public void DefaultThemeAndAccent_AreSelectable()
    {
        // The out-of-box defaults must be valid selections (dark + a named accent that has a dict).
        Assert.Equal(AppSettings.ThemeDark, new AppSettings().Normalized().Theme);
        Assert.Contains(AppSettings.DefaultAccentName, AppSettings.AccentNames);
    }
}
