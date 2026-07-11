using System.Text.Json;
using System.Text.RegularExpressions;
using Companion.Core.Career;
using Companion.Core.Determinism;
using Companion.Core.Json;

namespace Companion.Core.News;

/// <summary>
/// The generative news-article grammar (<c>data/rules/news/*.json</c>): era-flavored article
/// BODIES keyed by journal <c>phase|cause</c>, filled from a round's <see cref="NewsFacts"/>.
///
/// An article body is a template made of literal prose, direct fact tokens (<c>{player}</c>,
/// <c>{race}</c>, <c>{position}</c>, …) AND phrase-pool references (<c>{pool:lede}</c>) that
/// expand to one interchangeable fragment drawn from a named pool. A pool fragment may itself
/// reference tokens and further pools, so one template plus a few pools composes into
/// combinatorially many distinct bodies from a compact corpus.
///
/// Selection is DETERMINISTIC: the template pick and every pool expansion draw, in a fixed
/// traversal order, from ONE seeded <c>headlines</c> PCG32 stream (the same stream the shipped
/// <see cref="HeadlineBank"/> consumes, constructed via <see cref="StreamFactory"/> from
/// <c>(masterSeed, year, round)</c>). So the same career renders byte-identical articles on
/// replay — the body is DERIVED, never a stored input. New eras/events are pure JSON adds.
/// </summary>
public sealed class NewsArticleBank
{
    /// <summary>Era key + year range; the "default" era is the fallback and needs no range.</summary>
    public required IReadOnlyList<HeadlineEra> Eras { get; init; }

    /// <summary>"phase|cause" -> era key -> body templates.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> Bodies { get; init; }

    /// <summary>Named phrase pools: pool name -> era key -> interchangeable fragments,
    /// referenced from body templates as <c>{pool:name}</c>.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> Pools { get; init; }

    public const string DefaultEra = HeadlineBank.DefaultEra;

    /// <summary>Guards runaway pool recursion (a pool referencing itself through the corpus)
    /// so a malformed corpus fails loud in tests, never hangs the news feed.</summary>
    private const int MaxExpansionDepth = 8;

    private static readonly Regex TokenPattern = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    /// <summary>Loads and MERGES every <c>*.json</c> corpus in a directory into one bank, so
    /// each era (or event family) can live in its own file and community packs drop new files
    /// beside the shipped ones — "add more content easily later" by construction. Files are
    /// read in ordinal filename order (deterministic); merge is additive — each file
    /// contributes its eras, and appends its body/pool variants to any shared key. A missing
    /// or empty directory yields an empty bank (the feed then keeps headlines as the story).</summary>
    public static NewsArticleBank LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return Empty();

        var files = Directory.GetFiles(directory, "*.json")
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0)
            return Empty();

        var eras = new List<HeadlineEra>();
        var eraKeys = new HashSet<string>(StringComparer.Ordinal);
        var bodies = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
        var pools = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);

        foreach (string file in files)
        {
            var bank = Parse(File.ReadAllText(file));
            foreach (var era in bank.Eras)
            {
                if (eraKeys.Add(era.Key))
                    eras.Add(era);
            }
            Merge(bodies, bank.Bodies);
            Merge(pools, bank.Pools);
        }

        return new NewsArticleBank
        {
            Eras = eras,
            Bodies = Freeze(bodies),
            Pools = Freeze(pools),
        };
    }

    private static NewsArticleBank Empty() => new()
    {
        Eras = [],
        Bodies = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.Ordinal),
        Pools = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.Ordinal),
    };

    private static void Merge(
        Dictionary<string, Dictionary<string, List<string>>> into,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> from)
    {
        foreach (var (key, byEra) in from)
        {
            var target = into.TryGetValue(key, out var existing)
                ? existing
                : into[key] = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var (era, variants) in byEra)
            {
                var list = target.TryGetValue(era, out var existingList)
                    ? existingList
                    : target[era] = [];
                list.AddRange(variants);
            }
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> Freeze(
        Dictionary<string, Dictionary<string, List<string>>> map) =>
        map.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyDictionary<string, IReadOnlyList<string>>)kvp.Value.ToDictionary(
                e => e.Key,
                e => (IReadOnlyList<string>)e.Value,
                StringComparer.Ordinal),
            StringComparer.Ordinal);

    public static NewsArticleBank Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<BankDto>(json, CoreJson.Options)
                  ?? throw new JsonException("News article corpus is empty.");
        if (dto.Bodies is not { Count: > 0 })
            throw new JsonException("News article corpus declares no bodies.");

        var eraKeys = new HashSet<string>(dto.Eras.Select(e => e.Key), StringComparer.Ordinal)
        {
            DefaultEra,
        };

        foreach (var (key, byEra) in dto.Bodies)
        {
            if (!key.Contains('|'))
                throw new JsonException($"Article body key '{key}' is not of the form 'phase|cause'.");
            ValidateEraMap(key, byEra, eraKeys, "body");
        }

        foreach (var (name, byEra) in dto.Pools)
            ValidateEraMap($"pool:{name}", byEra, eraKeys, "pool");

        return new NewsArticleBank { Eras = dto.Eras, Bodies = dto.Bodies, Pools = dto.Pools };
    }

    private static void ValidateEraMap(
        string key,
        IReadOnlyDictionary<string, IReadOnlyList<string>> byEra,
        HashSet<string> eraKeys,
        string kind)
    {
        if (byEra.Count == 0)
            throw new JsonException($"News {kind} '{key}' declares no era variants.");
        foreach (var (era, variants) in byEra)
        {
            if (!eraKeys.Contains(era))
                throw new JsonException($"News {kind} '{key}' references undeclared era '{era}'.");
            if (variants is not { Count: > 0 })
                throw new JsonException($"News {kind} '{key}' era '{era}' has no variants.");
        }
    }

    /// <summary>The era key for a year: the first declared era whose range contains it, else
    /// "default" — the same resolution rule as <see cref="HeadlineBank"/>.</summary>
    public string ResolveEra(int year)
    {
        foreach (var era in Eras)
        {
            if (era.FromYear <= year && year <= era.ToYear)
                return era.Key;
        }
        return DefaultEra;
    }

    /// <summary>Body templates for a phase+cause in the year's era, falling back to "default".
    /// Empty when the corpus has no entry for the key at all.</summary>
    public IReadOnlyList<string> Templates(string phase, string cause, int year) =>
        VariantsFor(Bodies, phase + "|" + cause, ResolveEra(year));

    private static IReadOnlyList<string> VariantsFor(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> map,
        string key,
        string era)
    {
        if (!map.TryGetValue(key, out var byEra))
            return [];
        if (byEra.TryGetValue(era, out var variants))
            return variants;
        return byEra.TryGetValue(DefaultEra, out var fallback) ? fallback : [];
    }

    /// <summary>Builds the deterministic article body for <paramref name="facts"/>, drawing the
    /// template pick and every pool expansion from <paramref name="stream"/> in a fixed
    /// left-to-right traversal order. Returns null when the corpus has no body for the key —
    /// the caller then keeps the headline as the whole story. Never mutates the corpus.</summary>
    /// <exception cref="InvalidOperationException">A selected template or fragment references an
    /// unknown token or an undeclared pool — so corpus bugs surface in tests, not journals.</exception>
    public string? BuildBody(NewsFacts facts, Pcg32 stream)
    {
        // PreferredEra (the SMGP fictional-world corpus) overrides the year→era resolution; a
        // key the preferred era lacks still falls back per-key to "default" inside VariantsFor.
        string era = facts.PreferredEra is { Length: > 0 } preferred ? preferred : ResolveEra(facts.Year);
        var templates = VariantsFor(Bodies, facts.Phase + "|" + facts.Cause, era);
        if (templates.Count == 0)
            return null;

        var tokens = TokenValues(facts);

        string template = templates[stream.NextInt(0, templates.Count)];
        return Expand(template, era, tokens, stream, depth: 0);
    }

    /// <summary>Single-pass expansion: scan the text once, resolving each <c>{token}</c> — a
    /// <c>{pool:name}</c> selects a fragment (recursively expanded, same stream) and a plain
    /// token substitutes a fact value. Substituted values are NOT re-scanned, so a fact that
    /// happens to contain braces prints verbatim.</summary>
    private string Expand(
        string text,
        string era,
        IReadOnlyDictionary<string, string> tokens,
        Pcg32 stream,
        int depth)
    {
        if (depth > MaxExpansionDepth)
            throw new InvalidOperationException(
                $"News article expansion exceeded depth {MaxExpansionDepth} — a pool likely " +
                "references itself. Break the cycle in the corpus.");

        return TokenPattern.Replace(text, match =>
        {
            string token = match.Groups[1].Value;
            if (token.StartsWith("pool:", StringComparison.Ordinal))
            {
                string poolName = token["pool:".Length..];
                var fragments = VariantsFor(Pools, poolName, era);
                if (fragments.Count == 0)
                    throw new InvalidOperationException(
                        $"News body references undeclared pool '{poolName}'.");
                string fragment = fragments[stream.NextInt(0, fragments.Count)];
                return Expand(fragment, era, tokens, stream, depth + 1);
            }

            return tokens.TryGetValue(token, out string? value)
                ? value
                : throw new InvalidOperationException(
                    $"News body uses unknown token '{{{token}}}': {text}");
        });
    }

    /// <summary>Maps facts onto the token vocabulary. Missing facts resolve to neutral phrases
    /// so a thin source row still fills every named slot (the article never prints a raw
    /// "{token}"). Ordinals reuse the shipped <see cref="HeadlineSelector.Ordinal"/>.</summary>
    private static IReadOnlyDictionary<string, string> TokenValues(NewsFacts facts)
    {
        string position = facts.PlayerFinish is { } p ? HeadlineSelector.Ordinal(p) : "out";
        string expected = facts.ExpectedFinish is { } e ? HeadlineSelector.Ordinal(e) : "the expected slot";
        string champPos = facts.ChampionshipPosition is { } c ? HeadlineSelector.Ordinal(c) : "the order";

        string champMove = facts.ChampionshipDelta switch
        {
            > 0 => facts.ChampionshipDelta == 1 ? "up a place" : $"up {facts.ChampionshipDelta} places",
            < 0 => facts.ChampionshipDelta == -1 ? "down a place" : $"down {-facts.ChampionshipDelta} places",
            0 => "holding station",
            _ => "settling",
        };

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["player"] = Or(facts.PlayerName, "our man"),
            ["team"] = Or(facts.TeamName, "the team"),
            ["race"] = Or(facts.RaceName, "the race"),
            ["year"] = facts.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["round"] = facts.Round.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["position"] = position,
            ["expected"] = expected,
            ["winner"] = Or(facts.WinnerName, "the winner"),
            ["fieldSize"] = facts.FieldSize?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "the field",
            ["champPosition"] = champPos,
            ["champMove"] = champMove,
            ["champLeader"] = Or(facts.ChampionshipLeaderName, "the championship leader"),
        };
    }

    private static string Or(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private sealed record BankDto
    {
        public IReadOnlyList<HeadlineEra> Eras { get; init; } = [];
        public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> Bodies { get; init; }
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> Pools { get; init; } =
            new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.Ordinal);
    }
}
