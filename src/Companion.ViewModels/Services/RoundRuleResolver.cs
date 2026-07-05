using System.Text;
using Companion.Core.Numerics;
using Companion.Core.Packs;
using Companion.Core.Scoring;

namespace Companion.ViewModels.Services;

/// <summary>The per-round scoring rules a pack round resolves to, ready for
/// <see cref="RoundResult"/> construction.</summary>
public sealed record ResolvedRoundRules
{
    public Rational PointsFactor { get; init; } = Rational.One;
    public bool CountsForConstructors { get; init; } = true;
    public string? AlternateRaceTableId { get; init; }
}

/// <summary>
/// Matches a pack round against the season's catalog <see cref="RoundOverride"/> list
/// (half-points races, Indy constructors exclusion, double-points finales). Catalog
/// overrides are keyed by f1db grand-prix id ("monaco", "abu-dhabi", "spain"); pack rounds
/// carry display names ("Monaco Grand Prix", "Spanish Grand Prix"), so the match is by
/// slugified round name: exact/prefix match first, then the event suffix ("… Grand Prix")
/// is stripped and the remaining country adjective resolved through an explicit alias table
/// ("spanish" → "spain", "belgian" → "belgium"). Every id used in the shipped rules catalog
/// resolves under this rule; packs with exotic naming should keep round names close to the
/// f1db grand-prix name (a future pack-format field can make the binding exact).
/// </summary>
public static class RoundRuleResolver
{
    public static ResolvedRoundRules Resolve(CatalogSeason season, PackRound round)
    {
        var rules = new ResolvedRoundRules();

        foreach (var roundOverride in season.RoundOverrides)
        {
            if (!Matches(roundOverride.GrandPrix, round.Name))
                continue;

            rules = rules with
            {
                PointsFactor = roundOverride.PointsFactor ?? rules.PointsFactor,
                CountsForConstructors = roundOverride.CountsForConstructors ?? rules.CountsForConstructors,
                AlternateRaceTableId = roundOverride.AlternateRaceTableId ?? rules.AlternateRaceTableId,
            };
        }

        return rules;
    }

    /// <summary>True when the catalog grand-prix id refers to the given round name.</summary>
    public static bool Matches(string grandPrixId, string roundName)
    {
        string id = grandPrixId.Trim().ToLowerInvariant();
        string slug = Slugify(roundName);
        if (id.Length == 0 || slug.Length == 0)
            return false;

        // "monaco" vs "monaco-grand-prix", "indianapolis" vs "indianapolis-500",
        // "abu-dhabi" vs "abu-dhabi-grand-prix".
        if (slug == id || slug.StartsWith(id + "-", StringComparison.Ordinal))
            return true;

        // "spanish-grand-prix" → "spanish" → "spain".
        string phrase = StripEventSuffix(slug);
        if (phrase == id)
            return true;
        return CountryAdjectives.TryGetValue(phrase, out string? country) &&
               string.Equals(country, id, StringComparison.Ordinal);
    }

    private static string StripEventSuffix(string slug)
    {
        foreach (string suffix in (string[])["-grand-prix", "-gp", "-500"])
        {
            if (slug.EndsWith(suffix, StringComparison.Ordinal))
                return slug[..^suffix.Length];
        }
        return slug;
    }

    /// <summary>Grand-prix naming adjectives → f1db country-style grand-prix ids. Covers
    /// every adjective form used by world-championship grand-prix names (the shipped rules
    /// catalog only needs spain/austria/australia/malaysia/belgium, the rest future-proof
    /// era packs).</summary>
    private static readonly Dictionary<string, string> CountryAdjectives = new(StringComparer.Ordinal)
    {
        ["spanish"] = "spain",
        ["belgian"] = "belgium",
        ["austrian"] = "austria",
        ["australian"] = "australia",
        ["malaysian"] = "malaysia",
        ["german"] = "germany",
        ["french"] = "france",
        ["british"] = "great-britain",
        ["italian"] = "italy",
        ["brazilian"] = "brazil",
        ["mexican"] = "mexico",
        ["mexico-city"] = "mexico-city",
        ["canadian"] = "canada",
        ["american"] = "united-states",
        ["united-states"] = "united-states",
        ["dutch"] = "netherlands",
        ["hungarian"] = "hungary",
        ["japanese"] = "japan",
        ["portuguese"] = "portugal",
        ["argentine"] = "argentina",
        ["argentinian"] = "argentina",
        ["south-african"] = "south-africa",
        ["russian"] = "russia",
        ["chinese"] = "china",
        ["turkish"] = "turkey",
        ["korean"] = "south-korea",
        ["swedish"] = "sweden",
        ["swiss"] = "switzerland",
        ["european"] = "europe",
        ["styrian"] = "styria",
        ["tuscan"] = "tuscany",
        ["saudi-arabian"] = "saudi-arabia",
        ["moroccan"] = "morocco",
        ["indian"] = "india",
        ["bahraini"] = "bahrain",
        ["monegasque"] = "monaco",
    };

    private static string Slugify(string name)
    {
        var builder = new StringBuilder(name.Length);
        bool lastWasHyphen = true;
        foreach (char c in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen)
            {
                builder.Append('-');
                lastWasHyphen = true;
            }
        }
        return builder.ToString().TrimEnd('-');
    }
}
