using System.Text.RegularExpressions;
using Companion.Core.News;

namespace Companion.Tests.News;

/// <summary>
/// Static guards over the SHIPPED news corpora (data/rules/news/*.json), so the grind of
/// deepening them decade by decade can never drift the contract:
/// era voice stays era-CAPPED (no anachronistic vocabulary — a 1960s dispatch must not mention
/// telemetry, a 1970s one DRS), every template's tokens resolve (fact token or declared pool —
/// a typo here otherwise surfaces as a render-time throw in whatever career happens to roll
/// that variant), and every era keeps a minimum variety floor per article cause.
/// </summary>
public sealed class NewsCorpusGuardTests
{
    private static string ShippedNewsDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "rules", "news");

    private static readonly Lazy<NewsArticleBank> Bank = new(() =>
        NewsArticleBank.LoadDirectory(ShippedNewsDirectory));

    /// <summary>The fact tokens the composer fills (NewsArticleBank.BuildBody's vocabulary —
    /// mirrored in every corpus file's $comment).</summary>
    private static readonly HashSet<string> FactTokens = new(StringComparer.Ordinal)
    {
        "player", "team", "race", "year", "round", "position", "expected", "winner",
        "fieldSize", "champPosition", "champMove", "champLeader",
    };

    /// <summary>Vocabulary that must not appear in an era's strings — technology, regulation or
    /// media culture that did not exist yet (word-boundary, case-insensitive: "backers" must
    /// never trip "KERS"). Kept deliberately unambiguous; era-legitimate borderline terms
    /// (1970s ground-effect, 1980s turbo, 1990s telemetry, early-2010s KERS) are NOT banned.</summary>
    public static IEnumerable<object[]> EraBans() =>
    [
        ["1960s", new[] { "turbo", "ground.effect", "telemetry", "DRS", "halo", "hybrid",
            "safety car", "power unit", "KERS", "undercut", "overcut", "simulator", "tweet",
            "social media", "slicks", "pit-to-car" }],
        ["1970s", new[] { "telemetry", "DRS", "halo", "hybrid", "power unit", "KERS",
            "undercut", "overcut", "simulator", "tweet", "social media" }],
        ["1980s", new[] { "DRS", "halo", "hybrid", "power unit", "KERS", "tweet", "social media" }],
        ["1990s", new[] { "DRS", "halo", "hybrid", "power unit", "KERS", "tweet", "social media" }],
        ["2000s", new[] { "DRS", "halo", "power unit", "tweet" }],
        // Tobacco livery money left the sport before the 2010s; the era runs 2010-2029.
        ["2010s", new[] { "cigarette", "tobacco" }],
    ];

    [Theory]
    [MemberData(nameof(EraBans))]
    public void EraStrings_CarryNoAnachronisticVocabulary(string eraKey, string[] bannedTerms)
    {
        var offenders = new List<string>();
        foreach (var (where, text) in EraStrings(eraKey))
        {
            foreach (string term in bannedTerms)
            {
                if (Regex.IsMatch(text, $@"\b{term}\b", RegexOptions.IgnoreCase))
                    offenders.Add($"{where}: '{text}' mentions banned-for-{eraKey} '{term}'");
            }
        }
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>The era-neutral "default" strings render in EVERY era, so they must satisfy the
    /// union of every era's bans.</summary>
    [Fact]
    public void DefaultStrings_AreFullyEraNeutral()
    {
        var union = EraBans().SelectMany(row => (string[])row[1]).Distinct().ToArray();
        var offenders = new List<string>();
        foreach (var (where, text) in EraStrings(NewsArticleBank.DefaultEra))
        {
            foreach (string term in union)
            {
                if (Regex.IsMatch(text, $@"\b{term}\b", RegexOptions.IgnoreCase))
                    offenders.Add($"{where}: '{text}' mentions era-bound '{term}'");
            }
        }
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Every {token} in every body template is a fact token or a declared {pool:...};
    /// pool strings may use fact tokens only. A typo would otherwise throw at render time in
    /// whichever career first rolls the broken variant.</summary>
    [Fact]
    public void EveryTemplateToken_Resolves()
    {
        var bank = Bank.Value;
        var offenders = new List<string>();

        foreach (var (key, byEra) in bank.Bodies)
        foreach (var (era, templates) in byEra)
        foreach (string template in templates)
        foreach (Match m in Regex.Matches(template, @"\{([^{}]+)\}"))
        {
            string token = m.Groups[1].Value;
            if (token.StartsWith("pool:", StringComparison.Ordinal))
            {
                if (!bank.Pools.ContainsKey(token["pool:".Length..]))
                    offenders.Add($"body {key} [{era}]: undeclared {{{token}}}");
            }
            else if (!FactTokens.Contains(token))
            {
                offenders.Add($"body {key} [{era}]: unknown token {{{token}}}");
            }
        }

        foreach (var (name, byEra) in bank.Pools)
        foreach (var (era, fragments) in byEra)
        foreach (string fragment in fragments)
        foreach (Match m in Regex.Matches(fragment, @"\{([^{}]+)\}"))
        {
            string token = m.Groups[1].Value;
            if (!FactTokens.Contains(token))
                offenders.Add($"pool {name} [{era}]: unknown or nested token {{{token}}}");
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Variety floor: each shipped era keeps at least six variants per body key, so a
    /// season's worth of articles doesn't repeat itself into wallpaper.</summary>
    [Fact]
    public void EveryEra_KeepsTheVarietyFloor()
    {
        var offenders = new List<string>();
        foreach (var (key, byEra) in Bank.Value.Bodies)
        foreach (var (era, templates) in byEra)
        {
            if (era != NewsArticleBank.DefaultEra && templates.Count < 6)
                offenders.Add($"body {key} [{era}]: only {templates.Count} variants (floor 6)");
        }
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>All (where, text) strings authored under <paramref name="eraKey"/>.</summary>
    private static IEnumerable<(string Where, string Text)> EraStrings(string eraKey)
    {
        var bank = Bank.Value;
        foreach (var (key, byEra) in bank.Bodies)
        {
            if (byEra.TryGetValue(eraKey, out var templates))
                foreach (string t in templates)
                    yield return ($"body {key}", t);
        }
        foreach (var (name, byEra) in bank.Pools)
        {
            if (byEra.TryGetValue(eraKey, out var fragments))
                foreach (string f in fragments)
                    yield return ($"pool {name}", f);
        }
    }
}
