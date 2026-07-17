using System.Text.RegularExpressions;
using Companion.Core.Career;

namespace Companion.ViewModels.Settings;

/// <summary>How much of the immersive news article the News tab shows (career-hub-design.md
/// decision 17 — "immersion is user-configurable", the spectrum replacing the binary toggle):
/// full <see cref="Articles"/> bodies, <see cref="HeadlinesOnly"/> (the ticker headline with no
/// expanded body), or <see cref="Minimal"/> (headlines only, the most stripped-back reading).</summary>
public enum NewsDetailLevel
{
    /// <summary>Full period articles — the headline expands into the immersive body (default).</summary>
    Articles = 0,

    /// <summary>Headlines only — the ticker headline, no expanded article body.</summary>
    HeadlinesOnly = 1,

    /// <summary>The most stripped-back reading — headlines only, no body.</summary>
    Minimal = 2,
}

/// <summary>Which standings-table columns are visible (right-click a column header on the
/// standings screen to toggle). Persisted inside <see cref="AppSettings"/>.</summary>
public sealed record StandingsColumnSettings
{
    public bool ShowCounted { get; init; } = true;
    public bool ShowGross { get; init; } = true;
    public bool ShowDropped { get; init; } = true;
    public bool ShowPerRound { get; init; }
}

/// <summary>The main window's last size/position (ux-round contract section 4: "window
/// size/position remembered"). Saved on close, applied on the next launch after
/// <see cref="ClampTo"/> pulls it back onto whatever monitors exist now.</summary>
public sealed record WindowPlacementSettings
{
    public double Left { get; init; }
    public double Top { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public bool IsMaximized { get; init; }

    /// <summary>True when every stored number is a usable finite value with a positive size —
    /// anything else (hand-edited NaN, a serialized Infinity, zero size) is discarded whole.</summary>
    public bool IsUsable() =>
        double.IsFinite(Left) && double.IsFinite(Top) &&
        double.IsFinite(Width) && double.IsFinite(Height) &&
        Width > 0 && Height > 0;

    /// <summary>Fits this placement into the given virtual-screen rectangle (monitors change
    /// between runs — a window restored onto an unplugged screen would be unreachable):
    /// size shrinks to fit, then the origin shifts so the window sits fully inside.</summary>
    public WindowPlacementSettings ClampTo(
        double screenLeft, double screenTop, double screenWidth, double screenHeight)
    {
        double width = Math.Min(Width, screenWidth);
        double height = Math.Min(Height, screenHeight);
        double left = Math.Clamp(Left, screenLeft, screenLeft + screenWidth - width);
        double top = Math.Clamp(Top, screenTop, screenTop + screenHeight - height);
        return this with { Left = left, Top = top, Width = width, Height = height };
    }
}

/// <summary>
/// The app settings file (ux-round contract section 3): versioned camelCase JSON at
/// %APPDATA%\AMS2CareerCompanion\settings.json. Everything has a safe default so a missing
/// or corrupt file degrades to defaults, and <see cref="Normalized"/> clamps every value
/// into its legal range so a hand-edited file can never wedge the UI.
/// </summary>
public sealed record AppSettings
{
    public const int CurrentVersion = 2;

    public const string DefaultAccentColor = "#4F8CFF";
    public const int MinFontScalePercent = 90;
    public const int MaxFontScalePercent = 160;
    public const int MinVolumePercent = 0;
    public const int MaxVolumePercent = 100;

    /// <summary>The base theme names the appearance service can load (Theme.&lt;name&gt;.xaml).</summary>
    public const string ThemeDark = "dark";
    public const string ThemeLight = "light";

    /// <summary>The named accent presets (each has a contrast-tuned Dark + Light ResourceDict —
    /// <c>Themes/Accents/&lt;Dark|Light&gt;/Accent.&lt;name&gt;.xaml</c>). Order = the settings-screen swatch order.</summary>
    public static readonly IReadOnlyList<string> AccentNames =
        ["SmgpRed", "RoyalBlue", "Teal", "Green", "Gold", "Orange", "Magenta"];

    public const string DefaultAccentName = "RoyalBlue";

    public int Version { get; init; } = CurrentVersion;

    // ---------- developer (dynasty-passport-roadmap Piece 2 — the debug menu gate) ----------

    /// <summary>The app-wide developer debug menu gate. OFF by default and deliberately NOT
    /// surfaced in the normal Settings UI, so a shipped Release shows nothing and costs nothing
    /// until it is unlocked. Unlocked for the session via the hidden Ctrl+Shift+F12 chord (which
    /// persists it here) or the <c>AMS2_DEVMODE=1</c> environment variable read at startup. When
    /// true, Ctrl+Shift+D opens the debug overlay; when false that keybind is a no-op and the
    /// overlay never renders. Additive bool — an older settings file (no field) loads it false.</summary>
    public bool DeveloperMode { get; init; }

    // ---------- appearance ----------

    /// <summary>The base theme: <see cref="ThemeDark"/> or <see cref="ThemeLight"/>. Drives which
    /// <c>Theme.&lt;name&gt;.xaml</c> (and which accent variant) the appearance service loads.</summary>
    public string Theme { get; init; } = ThemeDark;

    /// <summary>The named accent preset (<see cref="AccentNames"/>) — the appearance service loads
    /// <c>Accents/&lt;base&gt;/Accent.&lt;name&gt;.xaml</c>. Replaces the old custom-hex accent.</summary>
    public string AccentName { get; init; } = DefaultAccentName;

    /// <summary>Legacy custom-hex accent (superseded by <see cref="AccentName"/>); kept so old settings
    /// files round-trip. No longer drives the runtime accent.</summary>
    public string AccentColor { get; init; } = DefaultAccentColor;

    /// <summary>UI font scale in percent (90–160), applied to the root layout scale.</summary>
    public int FontScalePercent { get; init; } = 100;

    // ---------- racing ----------

    /// <summary>Default in-game Opponent Skill slider (70–120) used when no pace-anchor
    /// recommendation exists yet (fresh careers, round 1).</summary>
    public double DefaultDifficulty { get; init; } = 100.0;

    /// <summary>Suppresses generated headlines except championship-critical ones.</summary>
    public bool MinimalNarrative { get; init; }

    /// <summary>Open the Race Day briefing automatically when a career loads.</summary>
    public bool AutoOpenBriefing { get; init; } = true;

    // ---------- immersion (career-hub-design.md §2.1 + decisions 7 & 17: "settings to
    //            modify what you see" — the era skin and the news verbosity) ----------

    /// <summary>Master switch for the era skin: when off, era-medium chrome (the hub's
    /// telegram/fax/email badge, the gallery's era labels) falls back to neutral. Default on
    /// (fully immersive); additive so existing settings files load unchanged.</summary>
    public bool EraThemingEnabled { get; init; } = true;

    /// <summary>How chatty the News tab is: full <see cref="NewsDetailLevel.Articles"/> by
    /// default, or headline-only readings that hide the expanded article body.</summary>
    public NewsDetailLevel NewsDetail { get; init; } = NewsDetailLevel.Articles;

    // ---------- audio ----------

    /// <summary>Master switch for all app-owned audio. Disabling it leaves the individual
    /// bus levels intact so the previous mix is restored when sound is enabled again.</summary>
    public bool SoundEnabled { get; init; } = true;

    /// <summary>Overall app-audio level (0–100), applied ahead of the individual buses.</summary>
    public int MasterVolumePercent { get; init; } = 80;

    /// <summary>One-shot UI and milestone effects level (0–100).</summary>
    public int EffectsVolumePercent { get; init; } = 70;

    /// <summary>Looping environmental soundscape level (0–100).</summary>
    public int AmbienceVolumePercent { get; init; } = 35;

    /// <summary>Background music level (0–100).</summary>
    public int MusicVolumePercent { get; init; } = 40;

    /// <summary>Mute app-owned interface effects whenever the companion window is not focused.</summary>
    public bool MuteWhenUnfocused { get; init; } = true;

    // ---------- staging (NAMeS-first, locked decision #7) ----------

    /// <summary>Default state of the wizard's "use your installed AI file as the season
    /// baseline" checkbox (only offered when the installed class XML parses).</summary>
    public bool PreferInstalledBaseline { get; init; } = true;

    /// <summary>Diff-aware staging: skip the write when the installed file already satisfies
    /// the round (on = default per the contract).</summary>
    public bool DiffAwareStaging { get; init; } = true;

    /// <summary>Offer the one-click restore of the pre-season AI file on season end.</summary>
    public bool RestorePromptOnSeasonEnd { get; init; } = true;

    // ---------- data ----------

    /// <summary>Extra pack folders searched by the new-career wizard, on top of the bundled
    /// packs folder and Documents\AMS2CareerCompanion\Packs.</summary>
    public IReadOnlyList<string> PackFolders { get; init; } = [];

    // ---------- standings customization ----------

    public StandingsColumnSettings StandingsColumns { get; init; } = new();

    /// <summary>Last selected standings tab (0 drivers, 1 constructors, 2 round matrix).</summary>
    public int StandingsTabIndex { get; init; }

    // ---------- discoverability (ux-round contract section 4) ----------

    /// <summary>Coach-mark ids the user dismissed — those callouts never show again.
    /// Unknown ids survive normalization so newer builds' flags round-trip.</summary>
    public IReadOnlyList<string> DismissedCoachMarks { get; init; } = [];

    /// <summary>Main-window size/position, saved on close; null until the first close
    /// (first launch centers on screen as before).</summary>
    public WindowPlacementSettings? WindowPlacement { get; init; }

    // ---------- validation ----------

    private static readonly Regex HexColor = new(
        "^#?(?<hex>[0-9a-fA-F]{6})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsValidAccentColor(string? value) =>
        value is not null && HexColor.IsMatch(value.Trim());

    /// <summary>"#RRGGBB" (uppercase) for any accepted spelling; null when invalid.</summary>
    public static string? NormalizeAccentColor(string? value)
    {
        if (value is null)
            return null;
        var match = HexColor.Match(value.Trim());
        return match.Success ? "#" + match.Groups["hex"].Value.ToUpperInvariant() : null;
    }

    /// <summary>The base theme clamped to a known value (<see cref="ThemeDark"/>/<see cref="ThemeLight"/>).</summary>
    public static string NormalizeTheme(string? value) =>
        string.Equals(value?.Trim(), ThemeLight, StringComparison.OrdinalIgnoreCase) ? ThemeLight : ThemeDark;

    /// <summary>The accent preset clamped to a known name (case-insensitive), else the default.</summary>
    public static string NormalizeAccentName(string? value)
    {
        foreach (var name in AccentNames)
            if (string.Equals(value?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                return name;
        return DefaultAccentName;
    }

    /// <summary>Every field clamped/sanitized into its legal range — what the store returns
    /// after any load and what the service persists after any update.</summary>
    public AppSettings Normalized() => this with
    {
        Version = CurrentVersion,
        Theme = NormalizeTheme(Theme),
        AccentName = NormalizeAccentName(AccentName),
        AccentColor = NormalizeAccentColor(AccentColor) ?? DefaultAccentColor,
        FontScalePercent = Math.Clamp(FontScalePercent, MinFontScalePercent, MaxFontScalePercent),
        MasterVolumePercent = Math.Clamp(MasterVolumePercent, MinVolumePercent, MaxVolumePercent),
        EffectsVolumePercent = Math.Clamp(EffectsVolumePercent, MinVolumePercent, MaxVolumePercent),
        AmbienceVolumePercent = Math.Clamp(AmbienceVolumePercent, MinVolumePercent, MaxVolumePercent),
        MusicVolumePercent = Math.Clamp(MusicVolumePercent, MinVolumePercent, MaxVolumePercent),
        DefaultDifficulty = double.IsFinite(DefaultDifficulty)
            ? Math.Clamp(DefaultDifficulty, DifficultyModel.MinSlider, DifficultyModel.MaxSlider)
            : 100.0,
        PackFolders = PackFolders
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList(),
        NewsDetail = Enum.IsDefined(NewsDetail) ? NewsDetail : NewsDetailLevel.Articles,
        StandingsColumns = StandingsColumns ?? new StandingsColumnSettings(),
        StandingsTabIndex = Math.Clamp(StandingsTabIndex, 0, 2),
        DismissedCoachMarks = (DismissedCoachMarks ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList(),
        WindowPlacement = WindowPlacement is { } placement && placement.IsUsable()
            ? placement
            : null,
    };
}
