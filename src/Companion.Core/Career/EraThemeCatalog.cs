using System.Text.Json;
using System.Text.RegularExpressions;

namespace Companion.Core.Career;

/// <summary>
/// The community/pack era-skin override table (<c>data/rules/era-themes.json</c>), the
/// documented schema from career-hub-design.md §6: a top-level object mapping a DECADE string
/// (<c>"1960"</c>, <c>"1970"</c>, …) to <c>{medium, accent, fontStack, paperTexture,
/// datelineFormat}</c>. Reconciled with the built <see cref="EraTheme"/> record like so:
/// <list type="bullet">
/// <item><c>medium</c> selects which built-in theme the entry starts from (absent/unknown medium
/// → the built-in medium for that decade; an unrecognized medium string skips the entry).</item>
/// <item><c>accent</c> → <see cref="EraTheme.AccentHex"/> (must be <c>#RRGGBB</c>, else ignored),
/// <c>fontStack</c> → <see cref="EraTheme.DocumentFontStack"/>, <c>paperTexture</c> →
/// <see cref="EraTheme.PaperTextureKey"/>, and <c>datelineFormat</c> →
/// <see cref="EraTheme.DatelineFlourish"/> (the flourish is the record's only dateline knob).</item>
/// <item>Any field the entry omits is INHERITED from the built-in theme, and
/// <see cref="EraTheme.Label"/> always derives from the medium (uppercased), so a sparse entry
/// still resolves to a complete, well-formed skin.</item>
/// </list>
/// Resolution is MEDIUM-MATCHED, not raw decade-bucketed (<see cref="ForYear"/>): the built-in
/// <see cref="EraThemes"/> table always owns the era BOUNDARIES (telegram ≤1979, fax 1980–1993,
/// email ≥1994, including the mid-decade 1994 email seam), while the file only restyles a
/// medium within its built-in span. That keeps a shipped file mirroring the built-ins a true
/// no-op, and still lets a community pack give the same medium a different look per decade.
/// Pure and deterministic: the same file always resolves the same skins, so presentation
/// introduces no un-seeded state. This is display data only, never a fold input, never
/// persisted into a career. The hard-coded <see cref="EraThemes"/> table stays the fallback:
/// callers compose <c>catalog.ForYear(year) ?? EraThemes.ForYear(year)</c>, so a missing file,
/// a missing entry, or an invalid one simply yields today's built-in skin.
/// </summary>
public sealed record EraThemeCatalog
{
    /// <summary>The file name under the rules directory (<c>data/rules/era-themes.json</c>).</summary>
    public const string FileName = "era-themes.json";

    private static readonly Regex AccentShape = new("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);

    private readonly IReadOnlyDictionary<int, EraTheme> _byDecade;

    private EraThemeCatalog(IReadOnlyDictionary<int, EraTheme> byDecade) => _byDecade = byDecade;

    /// <summary>The empty catalog, every <see cref="ForYear"/> lookup misses, so callers fall
    /// back to the built-in table. Also the parse result of a wholly unusable file.</summary>
    public static EraThemeCatalog Empty { get; } = new(new Dictionary<int, EraTheme>());

    public bool IsEmpty => _byDecade.Count == 0;

    /// <summary>The decade keys this catalog overrides (e.g. 1960, 1970), ascending.</summary>
    public IReadOnlyList<int> Decades => _byDecade.Keys.Order().ToArray();

    /// <summary>The override skin for a season year, or <c>null</c> when no entry applies (the
    /// caller then falls back to the built-in table). Medium-matched resolution: the year's
    /// built-in medium (<see cref="EraThemes.ForYear"/>) is computed first, then the LATEST
    /// decade entry at or before the year carrying that SAME medium wins. The built-in table
    /// therefore keeps owning the era boundaries, a <c>"1990"</c> fax entry restyles 1990–1993
    /// but never 1994+ (built-in email years, which look for email entries instead), while a
    /// pack can still give one medium a different accent/font/texture per decade.</summary>
    public EraTheme? ForYear(int year)
    {
        EraMedium medium = EraThemes.ForYear(year).Medium;
        EraTheme? best = null;
        int bestDecade = int.MinValue;
        foreach (var (decade, theme) in _byDecade)
        {
            if (theme.Medium == medium && decade <= year && decade > bestDecade)
            {
                best = theme;
                bestDecade = decade;
            }
        }

        return best;
    }

    /// <summary>Loads <c>data/rules/era-themes.json</c> from the rules directory, or
    /// <see cref="Empty"/> when absent, a stale-data install then resolves every era from the
    /// built-in table (the absent-tolerant pattern of the other display-only rules data).</summary>
    public static EraThemeCatalog Load(string rulesDirectory)
    {
        string path = Path.Combine(rulesDirectory, FileName);
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
    }

    /// <summary>Parses the override table. Tolerant by contract: a non-object document, a
    /// non-numeric key, a non-object entry, an unrecognized <c>medium</c>, or a malformed
    /// <c>accent</c> never throws, the offending entry is skipped (or the offending field
    /// ignored) so one bad decade cannot take down the rest of the table.</summary>
    public static EraThemeCatalog Parse(string json)
    {
        var byDecade = new Dictionary<int, EraTheme>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return Empty;

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!int.TryParse(property.Name, out int decadeKey) || property.Value.ValueKind != JsonValueKind.Object)
                    continue;

                int decade = decadeKey / 10 * 10;
                EraTheme baseTheme = ResolveBaseTheme(property.Value, decade);
                if (GetString(property.Value, "medium") is { Length: > 0 } mediumText &&
                    !TryParseMedium(mediumText, out _))
                    continue; // an explicit but unrecognized medium invalidates the entry

                byDecade[decade] = new EraTheme
                {
                    Medium = baseTheme.Medium,
                    Label = baseTheme.Label,
                    AccentHex = GetString(property.Value, "accent") is { } accent &&
                                AccentShape.IsMatch(accent)
                        ? accent
                        : baseTheme.AccentHex,
                    DocumentFontStack = GetString(property.Value, "fontStack") is { Length: > 0 } fontStack
                        ? fontStack
                        : baseTheme.DocumentFontStack,
                    PaperTextureKey = GetString(property.Value, "paperTexture") ?? baseTheme.PaperTextureKey,
                    DatelineFlourish = GetString(property.Value, "datelineFormat") ?? baseTheme.DatelineFlourish,
                };
            }
        }
        catch (JsonException)
        {
            return Empty; // a syntactically broken file = no overrides, never a load failure
        }

        return new EraThemeCatalog(byDecade);
    }

    /// <summary>The built-in theme an entry starts from: the built-in for its declared
    /// <c>medium</c> when present and recognized, else the built-in for the decade itself.</summary>
    private static EraTheme ResolveBaseTheme(JsonElement entry, int decade) =>
        GetString(entry, "medium") is { Length: > 0 } mediumText && TryParseMedium(mediumText, out EraMedium medium)
            ? BuiltInFor(medium)
            : EraThemes.ForYear(decade);

    private static EraTheme BuiltInFor(EraMedium medium) => medium switch
    {
        EraMedium.Telegram => EraThemes.Telegram,
        EraMedium.Fax => EraThemes.Fax,
        _ => EraThemes.Email,
    };

    private static bool TryParseMedium(string text, out EraMedium medium) =>
        Enum.TryParse(text, ignoreCase: true, out medium) && Enum.IsDefined(medium);

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
