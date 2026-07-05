using Companion.ViewModels.Settings;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The live settings seam + the settings screen viewmodel: every property change persists
/// immediately AND raises the Changed event (live-apply, no restart), reset returns to
/// defaults, and invalid input (bad hex) is flagged without being applied.
/// </summary>
public sealed class SettingsServiceTests
{
    private static (SettingsService Service, InMemorySettingsStore Store) NewService(
        AppSettings? initial = null)
    {
        var store = new InMemorySettingsStore(initial);
        return (new SettingsService(store), store);
    }

    // ---------- the service ----------

    [Fact]
    public void Update_Persists_AndRaisesChanged_WithTheNewSnapshot()
    {
        var (service, store) = NewService();
        AppSettings? observed = null;
        service.Changed += (_, s) => observed = s;

        service.Update(s => s with { FontScalePercent = 120 });

        Assert.NotNull(observed);
        Assert.Equal(120, observed.FontScalePercent);
        Assert.Equal(120, service.Current.FontScalePercent);
        Assert.Equal(120, store.Load().FontScalePercent);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void Update_NormalizesBeforePersisting()
    {
        var (service, store) = NewService();

        service.Update(s => s with { FontScalePercent = 999, AccentColor = "a1b2c3" });

        Assert.Equal(AppSettings.MaxFontScalePercent, service.Current.FontScalePercent);
        Assert.Equal("#A1B2C3", service.Current.AccentColor);
        Assert.Equal(store.Load().AccentColor, service.Current.AccentColor);
    }

    [Fact]
    public void Reset_ReturnsToDefaults_AndPersists()
    {
        var (service, store) = NewService(new AppSettings
        {
            FontScalePercent = 130,
            MinimalNarrative = true,
            PackFolders = [@"D:\Packs"],
        });

        service.Reset();

        Assert.Equal(100, service.Current.FontScalePercent);
        Assert.False(service.Current.MinimalNarrative);
        Assert.Empty(service.Current.PackFolders);
        Assert.Empty(store.Load().PackFolders);
    }

    // ---------- the screen viewmodel: live-apply ----------

    [Fact]
    public void EveryControl_AppliesLive_ThroughTheService()
    {
        var (service, store) = NewService();
        var vm = new SettingsViewModel(service, documentsDirectory: Path.GetTempPath());
        int changes = 0;
        service.Changed += (_, _) => changes++;

        vm.FontScalePercent = 110;
        Assert.Equal(110, service.Current.FontScalePercent);

        vm.DefaultDifficulty = 95.0;
        Assert.Equal(95.0, service.Current.DefaultDifficulty);

        vm.MinimalNarrative = true;
        Assert.True(service.Current.MinimalNarrative);

        vm.AutoOpenBriefing = false;
        Assert.False(service.Current.AutoOpenBriefing);

        vm.PreferInstalledBaseline = false;
        Assert.False(service.Current.PreferInstalledBaseline);

        vm.DiffAwareStaging = false;
        Assert.False(service.Current.DiffAwareStaging);

        vm.RestorePromptOnSeasonEnd = false;
        Assert.False(service.Current.RestorePromptOnSeasonEnd);

        vm.EraThemingEnabled = false;
        Assert.False(service.Current.EraThemingEnabled);

        vm.NewsDetail = NewsDetailLevel.HeadlinesOnly;
        Assert.Equal(NewsDetailLevel.HeadlinesOnly, service.Current.NewsDetail);

        Assert.Equal(9, changes);          // one Changed per control touch — live-apply
        Assert.Equal(9, store.SaveCount);  // and every change hit the store immediately
    }

    [Fact]
    public void ConstructingTheScreen_DoesNotWriteAnything()
    {
        var (service, store) = NewService(new AppSettings { FontScalePercent = 120 });

        var vm = new SettingsViewModel(service, documentsDirectory: Path.GetTempPath());

        Assert.Equal(120, vm.FontScalePercent); // loaded from the service...
        Assert.Equal(0, store.SaveCount);       // ...without echoing anything back
    }

    [Fact]
    public void AccentPreset_AppliesItsHex()
    {
        var (service, _) = NewService();
        var vm = new SettingsViewModel(service, documentsDirectory: Path.GetTempPath());

        var preset = vm.AccentPresets.First(p => p.Hex != AppSettings.DefaultAccentColor);
        vm.SelectAccentCommand.Execute(preset);

        Assert.Equal(preset.Hex, vm.AccentHex);
        Assert.Equal(AppSettings.NormalizeAccentColor(preset.Hex), service.Current.AccentColor);
        Assert.False(vm.AccentHexInvalid);
    }

    [Fact]
    public void InvalidHex_IsFlagged_AndNotApplied()
    {
        var (service, _) = NewService();
        var vm = new SettingsViewModel(service, documentsDirectory: Path.GetTempPath());

        vm.AccentHex = "#12";

        Assert.True(vm.AccentHexInvalid);
        Assert.Equal(AppSettings.DefaultAccentColor, service.Current.AccentColor); // unchanged

        vm.AccentHex = "#3E9B6E";
        Assert.False(vm.AccentHexInvalid);
        Assert.Equal("#3E9B6E", service.Current.AccentColor);
    }

    [Fact]
    public void PackFolders_AddAndRemove_PersistDeduplicated()
    {
        var (service, _) = NewService();
        var vm = new SettingsViewModel(service, documentsDirectory: Path.GetTempPath());

        vm.AddPackFolder(@"D:\Packs");
        vm.AddPackFolder(@"d:\packs");   // case-insensitive duplicate — ignored
        vm.AddPackFolder("  ");          // blank — ignored
        vm.AddPackFolder(@"E:\More");

        Assert.Equal(new[] { @"D:\Packs", @"E:\More" }, vm.PackFolders);
        Assert.Equal(new[] { @"D:\Packs", @"E:\More" }, service.Current.PackFolders);

        vm.RemovePackFolderCommand.Execute(@"D:\Packs");

        Assert.Equal(new[] { @"E:\More" }, vm.PackFolders);
        Assert.Equal(new[] { @"E:\More" }, service.Current.PackFolders);
    }

    [Fact]
    public void ResetCommand_RestoresDefaults_OnTheScreenAndTheService()
    {
        var (service, _) = NewService();
        var vm = new SettingsViewModel(service, documentsDirectory: Path.GetTempPath());
        vm.FontScalePercent = 130;
        vm.MinimalNarrative = true;
        vm.AddPackFolder(@"D:\Packs");

        vm.ResetCommand.Execute(null);

        Assert.Equal(100, vm.FontScalePercent);
        Assert.False(vm.MinimalNarrative);
        Assert.Empty(vm.PackFolders);
        Assert.Equal(100, service.Current.FontScalePercent);
    }

    [Fact]
    public void CloseCommand_RaisesCloseRequested()
    {
        var (service, _) = NewService();
        var vm = new SettingsViewModel(service, documentsDirectory: Path.GetTempPath());
        bool closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        vm.CloseCommand.Execute(null);

        Assert.True(closed);
    }
}
