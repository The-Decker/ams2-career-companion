using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Companion.RenderHarness.Tests;

/// <summary>
/// Guards the runtime-switchable theme seam. Base dictionaries must remain interchangeable,
/// accents may replace only their six documented resources, and switchable semantic brushes must
/// be consumed through DynamicResource so already-created controls respond to a dictionary swap.
/// </summary>
public sealed partial class ThemeContractRenderTests
{
    private const string ComponentRoot = "/AMS2CareerCompanion;component/Themes/";

    private static readonly IReadOnlyDictionary<string, Type> BaseContract =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["BgBrush"] = typeof(SolidColorBrush),
            ["SurfaceBrush"] = typeof(SolidColorBrush),
            ["SurfaceAltBrush"] = typeof(SolidColorBrush),
            ["FieldBrush"] = typeof(SolidColorBrush),
            ["EdgeBrush"] = typeof(SolidColorBrush),
            ["EdgeStrongBrush"] = typeof(SolidColorBrush),
            ["TextBrush"] = typeof(SolidColorBrush),
            ["TextSecondaryBrush"] = typeof(SolidColorBrush),
            ["TextMutedBrush"] = typeof(SolidColorBrush),
            ["TextFaintBrush"] = typeof(SolidColorBrush),
            ["AccentBrush"] = typeof(SolidColorBrush),
            ["AccentHoverBrush"] = typeof(SolidColorBrush),
            ["AccentTextBrush"] = typeof(SolidColorBrush),
            ["AccentDimBrush"] = typeof(SolidColorBrush),
            ["SelectionBrush"] = typeof(SolidColorBrush),
            ["OnAccentBrush"] = typeof(SolidColorBrush),
            ["SuccessBrush"] = typeof(SolidColorBrush),
            ["SuccessDimBrush"] = typeof(SolidColorBrush),
            ["WarningBrush"] = typeof(SolidColorBrush),
            ["WarningDimBrush"] = typeof(SolidColorBrush),
            ["ErrorBrush"] = typeof(SolidColorBrush),
            ["ErrorDimBrush"] = typeof(SolidColorBrush),
            ["ControlHoverOverlayBrush"] = typeof(SolidColorBrush),
            ["OverlaySurfaceBrush"] = typeof(SolidColorBrush),
            ["OverlaySurfaceAltBrush"] = typeof(SolidColorBrush),
            ["ScrimBrush"] = typeof(SolidColorBrush),
            ["ShadowBrush"] = typeof(SolidColorBrush),
            ["MediaCaptionGradientBrush"] = typeof(LinearGradientBrush),
            ["OnMediaBrush"] = typeof(SolidColorBrush),
            ["OnMediaMutedBrush"] = typeof(SolidColorBrush),
            ["TeamColorScrimBrush"] = typeof(SolidColorBrush),
            ["OnTeamColorBrush"] = typeof(SolidColorBrush),
        };

    private static readonly string[] AccentContract =
    [
        "AccentBrush",
        "AccentHoverBrush",
        "AccentTextBrush",
        "AccentDimBrush",
        "SelectionBrush",
        "OnAccentBrush",
    ];

    private static readonly string[] AccentNames =
    [
        "SmgpRed",
        "Gold",
        "Teal",
        "RoyalBlue",
        "Green",
        "Magenta",
        "Orange",
    ];

    private static readonly HashSet<string> InvariantArtResources =
    [
        "MediaCaptionGradientBrush",
        "OnMediaBrush",
        "OnMediaMutedBrush",
        "TeamColorScrimBrush",
        "OnTeamColorBrush",
    ];

    [Fact]
    public void DarkAndLightBaseThemes_HaveTheSameCompleteTypedContract()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var dark = LoadDictionary("Theme.Dark.xaml");
            var light = LoadDictionary("Theme.Light.xaml");

            AssertBaseContract(dark, "dark");
            AssertBaseContract(light, "light");

            Assert.Equal(
                dark.Keys.Cast<string>().OrderBy(static key => key, StringComparer.Ordinal),
                light.Keys.Cast<string>().OrderBy(static key => key, StringComparer.Ordinal));
        });
    }

    [Fact]
    public void EveryDarkAndLightAccent_OverridesExactlyTheSixAccentResources()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            foreach (var baseTheme in new[] { "Dark", "Light" })
            {
                foreach (var accentName in AccentNames)
                {
                    var dictionary = LoadDictionary($"Accents/{baseTheme}/Accent.{accentName}.xaml");
                    var actualKeys = dictionary.Keys.Cast<string>()
                        .OrderBy(static key => key, StringComparer.Ordinal)
                        .ToArray();

                    Assert.True(
                        AccentContract.OrderBy(static key => key, StringComparer.Ordinal)
                            .SequenceEqual(actualKeys, StringComparer.Ordinal),
                        $"{baseTheme}/Accent.{accentName}.xaml must override exactly: " +
                        string.Join(", ", AccentContract) + ". Actual: " + string.Join(", ", actualKeys));

                    foreach (var key in AccentContract)
                        Assert.IsType<SolidColorBrush>(dictionary[key]);
                }
            }
        });
    }

    [Fact]
    public void SwitchableSemanticBrushConsumers_UseDynamicResource()
    {
        var appDirectory = Path.Combine(FindRepositoryRoot(), "src", "Companion.App");
        var switchableKeys = BaseContract.Keys
            .Where(key => !InvariantArtResources.Contains(key))
            .ToHashSet(StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(appDirectory, "*.xaml", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            foreach (Match match in StaticResourceReference().Matches(source))
            {
                var key = match.Groups["key"].Value;
                if (!switchableKeys.Contains(key))
                    continue;

                var line = 1 + source.AsSpan(0, match.Index).Count('\n');
                violations.Add($"{Path.GetRelativePath(appDirectory, file)}:{line} uses StaticResource {key}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Runtime-switchable semantic brushes must use DynamicResource:\n" +
            string.Join("\n", violations));
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void EveryBaseAndAccentPair_MeetsSmallTextContrast(string baseTheme)
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var palette = LoadDictionary($"Theme.{baseTheme}.xaml");
            var surface = BrushColor(palette, "SurfaceBrush");

            foreach (var key in new[]
                     {
                         "TextBrush", "TextSecondaryBrush", "TextMutedBrush", "TextFaintBrush",
                         "SuccessBrush", "WarningBrush", "ErrorBrush",
                     })
            {
                AssertContrast(BrushColor(palette, key), surface, 4.5, $"{baseTheme} {key} on SurfaceBrush");
            }

            foreach (var accentName in AccentNames)
            {
                var accent = LoadDictionary($"Accents/{baseTheme}/Accent.{accentName}.xaml");
                AssertContrast(
                    BrushColor(accent, "AccentTextBrush"),
                    surface,
                    4.5,
                    $"{baseTheme}/{accentName} AccentTextBrush on SurfaceBrush");
                AssertContrast(
                    BrushColor(accent, "OnAccentBrush"),
                    BrushColor(accent, "AccentBrush"),
                    4.5,
                    $"{baseTheme}/{accentName} OnAccentBrush on AccentBrush");
            }
        });
    }

    [Fact]
    public void ReplacingTheBaseDictionary_UpdatesAnExistingDynamicResourceConsumer()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var dark = LoadDictionary("Theme.Dark.xaml");
            var light = LoadDictionary("Theme.Light.xaml");
            var host = new Border();
            host.Resources.MergedDictionaries.Add(dark);
            host.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");

            Assert.Equal(BrushColor(dark, "SurfaceBrush"), Assert.IsType<SolidColorBrush>(host.Background).Color);

            host.Resources.MergedDictionaries[0] = light;

            Assert.Equal(BrushColor(light, "SurfaceBrush"), Assert.IsType<SolidColorBrush>(host.Background).Color);
        });
    }

    [Fact]
    public void ViewsAndStableControlStyles_DoNotInlineHexPaint()
    {
        var appDirectory = Path.Combine(FindRepositoryRoot(), "src", "Companion.App");
        var files = Directory.EnumerateFiles(Path.Combine(appDirectory, "Views"), "*.xaml", SearchOption.AllDirectories)
            .Append(Path.Combine(appDirectory, "MainWindow.xaml"))
            .Append(Path.Combine(appDirectory, "Themes", "Theme.xaml"));
        var violations = new List<string>();

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            foreach (Match match in HexColorLiteral().Matches(source))
            {
                var line = 1 + source.AsSpan(0, match.Index).Count('\n');
                violations.Add($"{Path.GetRelativePath(appDirectory, file)}:{line} contains {match.Value}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Inline paint belongs in a base/accent or invariant-art dictionary:\n" + string.Join("\n", violations));
    }

    private static ResourceDictionary LoadDictionary(string relativePath) =>
        Assert.IsType<ResourceDictionary>(Application.LoadComponent(
            new Uri(ComponentRoot + relativePath, UriKind.Relative)));

    private static void AssertBaseContract(ResourceDictionary dictionary, string description)
    {
        var actualKeys = dictionary.Keys.Cast<object>().ToArray();
        Assert.All(actualKeys, key => Assert.IsType<string>(key));

        var sortedActual = actualKeys.Cast<string>().OrderBy(static key => key, StringComparer.Ordinal);
        var sortedExpected = BaseContract.Keys.OrderBy(static key => key, StringComparer.Ordinal);
        Assert.True(
            sortedExpected.SequenceEqual(sortedActual, StringComparer.Ordinal),
            $"The {description} base theme must define the complete semantic contract. " +
            $"Expected: {string.Join(", ", sortedExpected)}. Actual: {string.Join(", ", sortedActual)}");

        foreach (var (key, expectedType) in BaseContract)
            Assert.Equal(expectedType, dictionary[key].GetType());
    }

    private static Color BrushColor(ResourceDictionary dictionary, string key) =>
        Assert.IsType<SolidColorBrush>(dictionary[key]).Color;

    private static void AssertContrast(Color foreground, Color background, double minimum, string description)
    {
        var foregroundLuminance = RelativeLuminance(foreground);
        var backgroundLuminance = RelativeLuminance(background);
        var ratio = (Math.Max(foregroundLuminance, backgroundLuminance) + 0.05) /
                    (Math.Min(foregroundLuminance, backgroundLuminance) + 0.05);

        Assert.True(ratio >= minimum, $"{description} has {ratio:F2}:1 contrast; expected at least {minimum:F1}:1.");
    }

    private static double RelativeLuminance(Color color) =>
        (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

    private static double Linear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Companion.App")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Could not find the repository root above {AppContext.BaseDirectory}.");
    }

    [GeneratedRegex(@"\{StaticResource\s+(?<key>[A-Za-z][A-Za-z0-9]*)\s*\}", RegexOptions.CultureInvariant)]
    private static partial Regex StaticResourceReference();

    [GeneratedRegex(@"#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?\b", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorLiteral();
}
