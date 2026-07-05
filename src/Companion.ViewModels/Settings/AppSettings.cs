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
    public const int CurrentVersion = 1;

    public const string DefaultAccentColor = "#4F8CFF";
    public const int MinFontScalePercent = 90;
    public const int MaxFontScalePercent = 130;

    public int Version { get; init; } = CurrentVersion;

    // ---------- appearance ----------

    /// <summary>"dark" today; the key exists so a light theme can arrive without a
    /// settings-file migration.</summary>
    public string Theme { get; init; } = "dark";

    /// <summary>Accent color as "#RRGGBB" (presets or custom hex).</summary>
    public string AccentColor { get; init; } = DefaultAccentColor;

    /// <summary>UI font scale in percent (90–130), applied to the root FontSize resource.</summary>
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

    /// <summary>Every field clamped/sanitized into its legal range — what the store returns
    /// after any load and what the service persists after any update.</summary>
    public AppSettings Normalized() => this with
    {
        Version = CurrentVersion,
        Theme = string.IsNullOrWhiteSpace(Theme) ? "dark" : Theme.Trim(),
        AccentColor = NormalizeAccentColor(AccentColor) ?? DefaultAccentColor,
        FontScalePercent = Math.Clamp(FontScalePercent, MinFontScalePercent, MaxFontScalePercent),
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
