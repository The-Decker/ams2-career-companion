using System.Text.Json;
using Companion.ViewModels.Settings;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The settings.json store (ux-round contract section 3): full round-trip of every field,
/// corrupt/missing files degrading to defaults (never a crash), and normalization clamping
/// hand-edited values back into their legal ranges.
/// </summary>
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-settings-").FullName;

    private string FilePath => Path.Combine(_root, "sub", "settings.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Structural equality (records with list members compare by reference).</summary>
    private static void AssertSameSettings(AppSettings expected, AppSettings actual) =>
        Assert.Equal(
            JsonSerializer.Serialize(expected),
            JsonSerializer.Serialize(actual));

    [Fact]
    public void MissingFile_LoadsDefaults()
    {
        var store = new JsonSettingsStore(FilePath);
        var settings = store.Load();

        AssertSameSettings(new AppSettings().Normalized(), settings);
        Assert.Equal(AppSettings.CurrentVersion, settings.Version);
        Assert.Equal("dark", settings.Theme);
        Assert.Equal(AppSettings.DefaultAccentColor, settings.AccentColor);
        Assert.Equal(100, settings.FontScalePercent);
        Assert.True(settings.AutoOpenBriefing);
        Assert.True(settings.PreferInstalledBaseline);
        Assert.True(settings.DiffAwareStaging);
        Assert.True(settings.RestorePromptOnSeasonEnd);
        Assert.False(settings.MinimalNarrative);
        Assert.True(settings.StandingsColumns.ShowGross);
        Assert.False(settings.StandingsColumns.ShowPerRound);
    }

    [Fact]
    public void RoundTrip_PersistsEveryField()
    {
        var store = new JsonSettingsStore(FilePath);
        var settings = new AppSettings
        {
            AccentColor = "#3E9B6E",
            FontScalePercent = 120,
            DefaultDifficulty = 104.0,
            MinimalNarrative = true,
            AutoOpenBriefing = false,
            PreferInstalledBaseline = false,
            DiffAwareStaging = false,
            RestorePromptOnSeasonEnd = false,
            PackFolders = [@"D:\MyPacks", @"E:\MorePacks"],
            StandingsColumns = new StandingsColumnSettings
            {
                ShowCounted = true,
                ShowGross = false,
                ShowDropped = false,
                ShowPerRound = true,
            },
            StandingsTabIndex = 2,
        };

        store.Save(settings);
        var reloaded = new JsonSettingsStore(FilePath).Load();

        AssertSameSettings(settings.Normalized(), reloaded);
        Assert.Equal(settings.PackFolders, reloaded.PackFolders);
        Assert.Equal(settings.StandingsColumns, reloaded.StandingsColumns);
        Assert.Equal(2, reloaded.StandingsTabIndex);
    }

    [Fact]
    public void SavedFile_IsVersionedCamelCaseJson()
    {
        var store = new JsonSettingsStore(FilePath);
        store.Save(new AppSettings());

        string json = File.ReadAllText(FilePath);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(AppSettings.CurrentVersion, root.GetProperty("version").GetInt32());
        Assert.Equal("dark", root.GetProperty("theme").GetString());
        Assert.True(root.TryGetProperty("accentColor", out _));
        Assert.True(root.TryGetProperty("fontScalePercent", out _));
        Assert.True(root.TryGetProperty("preferInstalledBaseline", out _));
        Assert.True(root.TryGetProperty("standingsColumns", out _));
    }

    [Theory]
    [InlineData("{ not json at all")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    public void CorruptFile_LoadsDefaults_WithoutThrowing(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content);

        var settings = new JsonSettingsStore(FilePath).Load();

        Assert.Equal(AppSettings.DefaultAccentColor, settings.AccentColor);
        Assert.Equal(100, settings.FontScalePercent);
    }

    [Fact]
    public void HandEditedValues_AreClampedOnLoad()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """
            {
              "version": 1,
              "accentColor": "not-a-color",
              "fontScalePercent": 400,
              "defaultDifficulty": 9000,
              "standingsTabIndex": 99,
              "packFolders": ["  D:\\Packs  ", "", "d:\\packs"]
            }
            """);

        var settings = new JsonSettingsStore(FilePath).Load();

        Assert.Equal(AppSettings.DefaultAccentColor, settings.AccentColor); // invalid hex → default
        Assert.Equal(AppSettings.MaxFontScalePercent, settings.FontScalePercent);
        Assert.Equal(120.0, settings.DefaultDifficulty); // DifficultyModel.MaxSlider
        Assert.Equal(2, settings.StandingsTabIndex);
        Assert.Equal(new[] { @"D:\Packs" }, settings.PackFolders); // trimmed, deduped, no empties
    }

    [Theory]
    [InlineData("#4F8CFF", "#4F8CFF")]
    [InlineData("4f8cff", "#4F8CFF")]
    [InlineData(" #a1b2c3 ", "#A1B2C3")]
    public void AccentColor_NormalizesAcceptedSpellings(string given, string expected)
    {
        Assert.Equal(expected, AppSettings.NormalizeAccentColor(given));
        Assert.True(AppSettings.IsValidAccentColor(given));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    public void AccentColor_RejectsInvalidSpellings(string? given)
    {
        Assert.Null(AppSettings.NormalizeAccentColor(given));
        Assert.False(AppSettings.IsValidAccentColor(given));
    }

    [Fact]
    public void Save_CreatesTheDirectory()
    {
        var store = new JsonSettingsStore(FilePath);
        store.Save(new AppSettings());
        Assert.True(File.Exists(FilePath));
    }
}
