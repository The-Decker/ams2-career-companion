using System.Text.RegularExpressions;

namespace Companion.Core.Career;

/// <summary>The period medium a career's news + documents are skinned as (career-hub-design.md
/// §6). Selected from the active pack's decade; drives typography, accent and news chrome.</summary>
public enum EraMedium
{
    /// <summary>1960s–70s: uppercase monospace wire reports, "STOP"-punctuated, ochre paper.</summary>
    Telegram,

    /// <summary>1980s–early 90s: sender/date band, thermal-paper grain.</summary>
    Fax,

    /// <summary>Mid-90s onward: inbox rows, subject/sender, clean sans accent.</summary>
    Email,
}

/// <summary>
/// The resolved period skin for a season year: which medium, the accent color, the document
/// font stack, and the dateline flourish. Pure data — the App maps it to a resource-dictionary
/// swap ("immersive docs, legible tools": documents + chrome age, dense tables stay legible).
/// v1 uses the built-in table below; a <c>data/rules/era-themes.json</c> override is a later
/// slice so community packs can declare their own era feel.
/// </summary>
public sealed record EraTheme
{
    public required EraMedium Medium { get; init; }

    /// <summary>Short uppercase medium label ("TELEGRAM" / "FAX" / "EMAIL").</summary>
    public required string Label { get; init; }

    /// <summary>Era accent color as a "#RRGGBB" hex string (the App resolves it to a brush).</summary>
    public required string AccentHex { get; init; }

    /// <summary>Comma-separated font stack for period documents (news articles, offer letters).
    /// Functional/data surfaces keep the app's legible base face regardless of this.</summary>
    public required string DocumentFontStack { get; init; }

    /// <summary>The dateline flourish ("STOP" for the telegram era, "" otherwise).</summary>
    public required string DatelineFlourish { get; init; }
}

/// <summary>
/// Resolves a season year to its <see cref="EraTheme"/>. Pure and deterministic — the same year
/// always maps to the same skin, so presentation introduces no un-seeded state. Boundaries follow
/// the design's decade framing (telegram 60s–70s, fax 80s–early 90s, email mid-90s+); they are the
/// one taste knob here and are easy to retune (or move to JSON later).
/// </summary>
public static class EraThemes
{
    public static readonly EraTheme Telegram = new()
    {
        Medium = EraMedium.Telegram,
        Label = "TELEGRAM",
        AccentHex = "#C8922E", // ochre wire paper
        DocumentFontStack = "Consolas, Courier New, monospace",
        DatelineFlourish = "STOP",
    };

    public static readonly EraTheme Fax = new()
    {
        Medium = EraMedium.Fax,
        Label = "FAX",
        AccentHex = "#5E7A8C", // slate thermal
        DocumentFontStack = "Cascadia Mono, Consolas, monospace",
        DatelineFlourish = "",
    };

    public static readonly EraTheme Email = new()
    {
        Medium = EraMedium.Email,
        Label = "EMAIL",
        AccentHex = "#3B7DD8", // clean inbox blue
        DocumentFontStack = "Segoe UI, Arial, sans-serif",
        DatelineFlourish = "",
    };

    /// <summary>The era skin for a season year. Telegram ≤1979, Fax 1980–1993, Email ≥1994.</summary>
    public static EraTheme ForYear(int year) => year switch
    {
        <= 1979 => Telegram,
        <= 1993 => Fax,
        _ => Email,
    };

    /// <summary>The era skin implied by any text containing a 4-digit year (e.g. a career name
    /// like "Formula One 1967" — the wizard defaults names to "&lt;series&gt; &lt;year&gt;"); null
    /// when no plausible 19xx/20xx year is present. Lets the career gallery colour a card by era
    /// without opening the career file.</summary>
    public static EraTheme? FromText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        var match = Regex.Match(text, @"\b(19|20)\d{2}\b");
        return match.Success && int.TryParse(match.Value, out int year) ? ForYear(year) : null;
    }
}
