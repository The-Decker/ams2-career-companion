using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Companion.Ams2.CustomAi;

/// <summary>
/// A leniently parsed installed custom-AI class file (typically a community NAMeS file).
/// Base entries (no <c>tracks</c> attribute) carry the file's season-wide driver data; track
/// entries are the author's per-venue overrides and stay separate — the baseline import uses
/// base entries only, while track entries remain the file's own round-level refinement.
/// </summary>
public sealed record CommunityAiFile
{
    /// <summary>Entries without a <c>tracks</c> attribute, in file order.</summary>
    public required IReadOnlyList<CustomAiDriver> BaseEntries { get; init; }

    /// <summary>Track-scoped override entries (non-empty <c>tracks</c> attribute), in file order.</summary>
    public required IReadOnlyList<CustomAiDriver> TrackEntries { get; init; }

    /// <summary>Non-fatal oddities found while parsing (skipped entries, unreadable stats).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Base entries keyed by exact livery name. When a file repeats a livery's base
    /// entry, the LAST one wins (later file content overrides earlier, mirroring how authors
    /// append corrections).</summary>
    public IReadOnlyDictionary<string, CustomAiDriver> BaseEntriesByLivery()
    {
        var byLivery = new Dictionary<string, CustomAiDriver>(StringComparer.Ordinal);
        foreach (var entry in BaseEntries)
            byLivery[entry.LiveryName] = entry;
        return byLivery;
    }
}

/// <summary>
/// Lenient reader for INSTALLED custom-AI XML files. Community files (jusk et al.) are not
/// well-formed XML: header comments contain '--' runs (calendar tables drawn with dashes),
/// attribute spacing is nonstandard (<c>tracks ="..."</c>), and livery names carry raw
/// ampersands ("Bang &amp; Olufsen" written as "Bang &amp;"). The game reads them anyway — so
/// must we: comments are stripped and bare ampersands escaped BEFORE the XML parse, and
/// per-entry oddities degrade to warnings, never exceptions. Only hopelessly broken markup
/// throws (as <see cref="InvalidOperationException"/>).
/// </summary>
public static class CommunityAiReader
{
    public static CommunityAiFile ReadFile(string path) => Parse(File.ReadAllText(path));

    /// <summary>File-reading convenience that swallows every parse/IO failure into null —
    /// for probing callers (diff-aware staging) where "unreadable" simply means "not a match".</summary>
    public static CommunityAiFile? TryReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? ReadFile(path) : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static CommunityAiFile Parse(string xmlText)
    {
        var warnings = new List<string>();
        string cleaned = RepairBareAmpersands(StripComments(xmlText));

        XDocument document;
        try
        {
            document = XDocument.Parse(cleaned);
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException(
                $"The installed custom-AI file is not readable even with lenient parsing: {ex.Message}", ex);
        }

        var baseEntries = new List<CustomAiDriver>();
        var trackEntries = new List<CustomAiDriver>();

        int position = 0;
        foreach (var element in document.Descendants()
                     .Where(e => e.Name.LocalName.Equals("driver", StringComparison.OrdinalIgnoreCase)))
        {
            position++;
            string? liveryName = AttributeValue(element, "livery_name");
            if (string.IsNullOrWhiteSpace(liveryName))
            {
                warnings.Add($"Driver entry #{position} has no livery_name attribute — skipped.");
                continue;
            }

            var tracks = (AttributeValue(element, "tracks") ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var driver = new CustomAiDriver
            {
                LiveryName = liveryName,
                Tracks = tracks,
                Name = TextValue(element, "name"),
                Country = TextValue(element, "country"),

                RaceSkill = Stat(element, "race_skill", liveryName, warnings),
                QualifyingSkill = Stat(element, "qualifying_skill", liveryName, warnings),
                Aggression = Stat(element, "aggression", liveryName, warnings),
                Defending = Stat(element, "defending", liveryName, warnings),
                Stamina = Stat(element, "stamina", liveryName, warnings),
                Consistency = Stat(element, "consistency", liveryName, warnings),
                StartReactions = Stat(element, "start_reactions", liveryName, warnings),
                WetSkill = Stat(element, "wet_skill", liveryName, warnings),
                TyreManagement = Stat(element, "tyre_management", liveryName, warnings),
                FuelManagement = Stat(element, "fuel_management", liveryName, warnings),
                BlueFlagConceding = Stat(element, "blue_flag_conceding", liveryName, warnings),
                WeatherTyreChanges = Stat(element, "weather_tyre_changes", liveryName, warnings),
                AvoidanceOfMistakes = Stat(element, "avoidance_of_mistakes", liveryName, warnings),
                AvoidanceOfForcedMistakes = Stat(element, "avoidance_of_forced_mistakes", liveryName, warnings),
                VehicleReliability = Stat(element, "vehicle_reliability", liveryName, warnings),

                WeightScalar = Stat(element, "weight_scalar", liveryName, warnings),
                PowerScalar = Stat(element, "power_scalar", liveryName, warnings),
                DragScalar = Stat(element, "drag_scalar", liveryName, warnings),
                SetupDownforce = Stat(element, "setup_downforce", liveryName, warnings),
                SetupDownforceRandomness = Stat(element, "setup_downforce_randomness", liveryName, warnings),
            };

            (tracks.Length > 0 ? trackEntries : baseEntries).Add(driver);
        }

        return new CommunityAiFile
        {
            BaseEntries = baseEntries,
            TrackEntries = trackEntries,
            Warnings = warnings,
        };
    }

    // ---------- lenient pre-processing ----------

    /// <summary>Removes every <c>&lt;!-- ... --&gt;</c> block by literal scan. Community
    /// headers draw tables with '-' runs, which is illegal inside XML comments — the whole
    /// comment goes, so the parser never sees it. An unterminated comment swallows the rest
    /// of the file (matching how browsers treat it).</summary>
    private static string StripComments(string text)
    {
        var result = new StringBuilder(text.Length);
        int position = 0;
        while (true)
        {
            int start = text.IndexOf("<!--", position, StringComparison.Ordinal);
            if (start < 0)
            {
                result.Append(text, position, text.Length - position);
                break;
            }

            result.Append(text, position, start - position);
            int end = text.IndexOf("-->", start + 4, StringComparison.Ordinal);
            if (end < 0)
                break;
            position = end + 3;
        }
        return result.ToString();
    }

    /// <summary>Escapes '&amp;' characters that do not start a character/entity reference —
    /// community livery names like "Bang &amp; Olufsen" are written with a raw ampersand.</summary>
    private static string RepairBareAmpersands(string text) =>
        BareAmpersand.Replace(text, "&amp;");

    private static readonly Regex BareAmpersand =
        new(@"&(?!(?:[A-Za-z][A-Za-z0-9]*|#[0-9]+|#x[0-9A-Fa-f]+);)", RegexOptions.Compiled);

    // ---------- element access (case-lenient) ----------

    private static string? AttributeValue(XElement element, string name) =>
        element.Attributes()
            .FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    private static string? TextValue(XElement element, string name)
    {
        string? value = element.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static double? Stat(XElement element, string name, string liveryName, List<string> warnings)
    {
        string? raw = TextValue(element, name);
        if (raw is null)
            return null;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return value;

        warnings.Add($"'{liveryName}': {name} value '{raw}' is not a number — ignored.");
        return null;
    }
}
