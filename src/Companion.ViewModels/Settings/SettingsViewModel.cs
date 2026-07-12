using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Core.Career;

namespace Companion.ViewModels.Settings;

/// <summary>One accent preset chip on the settings screen: a friendly label, the accent-dictionary key
/// (<see cref="AppSettings.AccentNames"/>), and a representative swatch color.</summary>
public sealed record AccentPreset(string Name, string PresetName, string Hex);

/// <summary>One selectable news-verbosity option in the Immersion section's picker.</summary>
public sealed record NewsDetailOption(NewsDetailLevel Level, string Label, string Description);

/// <summary>
/// The settings screen (ux-round contract section 3): four sections — Appearance, Racing,
/// Staging (NAMeS-first), Data — every control applying LIVE through
/// <see cref="ISettingsService"/> (no OK/Apply buttons, no restart), plus Reset-to-defaults.
/// WPF-free; the view adds folder pickers and open-in-Explorer buttons on top.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _service;
    private bool _loading;

    public SettingsViewModel(ISettingsService service, string? documentsDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;

        string documents = documentsDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        DefaultPacksFolder = Path.Combine(documents, "AMS2CareerCompanion", "Packs");
        CareersFolder = Path.Combine(documents, "AMS2CareerCompanion", "Careers");
        SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AMS2CareerCompanion");

        LoadFrom(service.Current);
    }

    /// <summary>Raised when the user closes the screen (Esc / the Done button); the shell
    /// navigates back to whatever was open.</summary>
    public event EventHandler? CloseRequested;

    // ---------- appearance ----------

    /// <summary>The 7 named accent presets (each with a contrast-tuned dark + light variant). Order +
    /// keys match <see cref="AppSettings.AccentNames"/>; the Hex is a representative swatch color.</summary>
    public IReadOnlyList<AccentPreset> AccentPresets { get; } =
    [
        new("SMGP red", "SmgpRed", "#E10600"),
        new("Royal blue", "RoyalBlue", "#4F8CFF"),
        new("Teal", "Teal", "#17B0A0"),
        new("Green", "Green", "#3E9B6E"),
        new("Gold", "Gold", "#D9A83C"),
        new("Orange", "Orange", "#FF8A3D"),
        new("Magenta", "Magenta", "#D14BB0"),
    ];

    /// <summary>The selected accent preset key (one of <see cref="AppSettings.AccentNames"/>).</summary>
    [ObservableProperty]
    private string _accentName = AppSettings.DefaultAccentName;

    partial void OnAccentNameChanged(string value) =>
        Apply(s => s with { AccentName = AppSettings.NormalizeAccentName(value) });

    [RelayCommand]
    private void SelectAccent(AccentPreset? preset)
    {
        if (preset is not null)
            AccentName = preset.PresetName;
    }

    /// <summary>True = the LIGHT base theme, false = DARK. Applies live (the appearance service swaps the
    /// base + accent ResourceDicts, and every view consumes them via DynamicResource, so it recolors
    /// instantly). Codex's theme contract keeps both bases contrast-correct.</summary>
    [ObservableProperty]
    private bool _isLightTheme;

    partial void OnIsLightThemeChanged(bool value) =>
        Apply(s => s with { Theme = value ? AppSettings.ThemeLight : AppSettings.ThemeDark });

    public int MinFontScalePercent => AppSettings.MinFontScalePercent;

    public int MaxFontScalePercent => AppSettings.MaxFontScalePercent;

    [ObservableProperty]
    private int _fontScalePercent = 100;

    partial void OnFontScalePercentChanged(int value) =>
        Apply(s => s with { FontScalePercent = value });

    // ---------- racing ----------

    public double MinDifficulty => DifficultyModel.MinSlider;

    public double MaxDifficulty => DifficultyModel.MaxSlider;

    [ObservableProperty]
    private double _defaultDifficulty = 100.0;

    partial void OnDefaultDifficultyChanged(double value) =>
        Apply(s => s with { DefaultDifficulty = value });

    [ObservableProperty]
    private bool _minimalNarrative;

    partial void OnMinimalNarrativeChanged(bool value) =>
        Apply(s => s with { MinimalNarrative = value });

    [ObservableProperty]
    private bool _autoOpenBriefing = true;

    partial void OnAutoOpenBriefingChanged(bool value) =>
        Apply(s => s with { AutoOpenBriefing = value });

    // ---------- immersion (career-hub-design.md §2.1: "settings to modify what you see") ----------

    /// <summary>Master switch for the era skin (hub era badge + gallery era labels).</summary>
    [ObservableProperty]
    private bool _eraThemingEnabled = true;

    partial void OnEraThemingEnabledChanged(bool value) =>
        Apply(s => s with { EraThemingEnabled = value });

    /// <summary>The selectable news-verbosity levels, in display order (combo/radio source).</summary>
    public IReadOnlyList<NewsDetailOption> NewsDetailOptions { get; } =
    [
        new(NewsDetailLevel.Articles, "Full articles", "Headlines expand into the full period article"),
        new(NewsDetailLevel.HeadlinesOnly, "Headlines only", "Show just the headline — no expanded article body"),
        new(NewsDetailLevel.Minimal, "Minimal", "The most stripped-back reading — headlines only"),
    ];

    [ObservableProperty]
    private NewsDetailLevel _newsDetail = NewsDetailLevel.Articles;

    partial void OnNewsDetailChanged(NewsDetailLevel value) =>
        Apply(s => s with { NewsDetail = value });

    // ---------- staging (NAMeS-first) ----------

    [ObservableProperty]
    private bool _preferInstalledBaseline = true;

    partial void OnPreferInstalledBaselineChanged(bool value) =>
        Apply(s => s with { PreferInstalledBaseline = value });

    [ObservableProperty]
    private bool _diffAwareStaging = true;

    partial void OnDiffAwareStagingChanged(bool value) =>
        Apply(s => s with { DiffAwareStaging = value });

    [ObservableProperty]
    private bool _restorePromptOnSeasonEnd = true;

    partial void OnRestorePromptOnSeasonEndChanged(bool value) =>
        Apply(s => s with { RestorePromptOnSeasonEnd = value });

    // ---------- data ----------

    public ObservableCollection<string> PackFolders { get; } = [];

    /// <summary>Default pack folder (informational; always searched, not removable).</summary>
    public string DefaultPacksFolder { get; }

    public string CareersFolder { get; }

    /// <summary>Where settings.json (and recent.json) live, for the open button.</summary>
    public string SettingsFolder { get; }

    /// <summary>Adds a custom pack search folder (the view supplies the picked path).</summary>
    public void AddPackFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        string trimmed = path.Trim();
        if (PackFolders.Any(f => string.Equals(f, trimmed, StringComparison.OrdinalIgnoreCase)))
            return;
        PackFolders.Add(trimmed);
        Apply(s => s with { PackFolders = PackFolders.ToList() });
    }

    [RelayCommand]
    private void RemovePackFolder(string? path)
    {
        if (path is null)
            return;
        var existing = PackFolders.FirstOrDefault(f =>
            string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
            return;
        PackFolders.Remove(existing);
        Apply(s => s with { PackFolders = PackFolders.ToList() });
    }

    // ---------- reset / close ----------

    [RelayCommand]
    private void Reset()
    {
        _service.Reset();
        LoadFrom(_service.Current);
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    // ---------- plumbing ----------

    private void Apply(Func<AppSettings, AppSettings> mutate)
    {
        if (_loading)
            return;
        _service.Update(mutate);
    }

    private void LoadFrom(AppSettings settings)
    {
        _loading = true;
        try
        {
            AccentName = AppSettings.NormalizeAccentName(settings.AccentName);
            IsLightTheme = string.Equals(settings.Theme, AppSettings.ThemeLight, StringComparison.OrdinalIgnoreCase);
            FontScalePercent = settings.FontScalePercent;
            DefaultDifficulty = settings.DefaultDifficulty;
            MinimalNarrative = settings.MinimalNarrative;
            AutoOpenBriefing = settings.AutoOpenBriefing;
            EraThemingEnabled = settings.EraThemingEnabled;
            NewsDetail = settings.NewsDetail;
            PreferInstalledBaseline = settings.PreferInstalledBaseline;
            DiffAwareStaging = settings.DiffAwareStaging;
            RestorePromptOnSeasonEnd = settings.RestorePromptOnSeasonEnd;
            PackFolders.Clear();
            foreach (string folder in settings.PackFolders)
                PackFolders.Add(folder);
        }
        finally
        {
            _loading = false;
        }
    }
}
