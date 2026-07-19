using System.Text.RegularExpressions;
using Companion.Core.Determinism;
using Companion.Core.Json;

namespace Companion.Core.Smgp;

/// <summary>
/// The SMGP "living world" dispatch corpus (<c>data/rules/smgp/dispatches.json</c>): templated in-world news
/// BODIES keyed by a dispatch template id, interchangeable phrase POOLS referenced as <c>{pool:name}</c>, and
/// a rotating <c>rumor</c> pool for the Paddock. A body template is literal prose + fact tokens
/// (<c>{player}</c>, <c>{rival}</c>, <c>{venue}</c>, <c>{number}</c>, …) + pool references, so a compact
/// corpus composes into many distinct stories. Selection is DETERMINISTIC from a PCG32 stream (the caller
/// keys it off the master seed + the round), so the same career renders the same dispatches on every open and
/// on replay. DISPLAY-ONLY, never a fold input, exactly like <see cref="SmgpRivalQuotes"/> and the news
/// corpora. An absent file yields <see cref="Empty"/>, every render returns the caller's fallback, so the
/// feed still shows each milestone's own words.
/// </summary>
public sealed class SmgpDispatchCorpus
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _templates;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _pools;

    /// <summary>Guards runaway pool recursion (a pool referencing itself) so a malformed corpus fails loud
    /// in tests, never hangs the feed.</summary>
    private const int MaxExpansionDepth = 8;

    private static readonly Regex TokenPattern = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    private SmgpDispatchCorpus(
        IReadOnlyDictionary<string, IReadOnlyList<string>> templates,
        IReadOnlyDictionary<string, IReadOnlyList<string>> pools)
    {
        _templates = templates;
        _pools = pools;
    }

    /// <summary>An empty corpus (no file shipped): every <see cref="Render"/> returns its fallback and
    /// <see cref="Rumor"/> returns empty.</summary>
    public static SmgpDispatchCorpus Empty { get; } = new(
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

    /// <summary>The template ids the corpus declares (introspection for the corpus-consistency test).</summary>
    public IReadOnlyCollection<string> TemplateKeys => (IReadOnlyCollection<string>)_templates.Keys;

    /// <summary>The pool names the corpus declares (introspection for the corpus-consistency test).</summary>
    public IReadOnlyCollection<string> PoolNames => (IReadOnlyCollection<string>)_pools.Keys;

    /// <summary>Loads <c>data/rules/smgp/dispatches.json</c>, or <see cref="Empty"/> when the file is absent
    /// (back-compat: the feed keeps each milestone's own detail line as the story).</summary>
    public static SmgpDispatchCorpus Load(string rulesDirectory)
    {
        string path = Path.Combine(rulesDirectory, "smgp", "dispatches.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
    }

    public static SmgpDispatchCorpus Parse(string json)
    {
        var dto = System.Text.Json.JsonSerializer.Deserialize<CorpusDto>(json, CoreJson.Options)
            ?? new CorpusDto();
        return new SmgpDispatchCorpus(Freeze(dto.Templates), Freeze(dto.Pools));
    }

    /// <summary>Renders one dispatch body for <paramref name="templateKey"/> with the given
    /// <paramref name="tokens"/>, drawing the template pick and every pool expansion from
    /// <paramref name="stream"/> in a fixed left-to-right traversal order. Returns
    /// <paramref name="fallback"/> when the corpus declares no template for the key (so a dispatch always
    /// has a body). An unknown <c>{token}</c> resolves to empty (lenient, the feed never crashes; the
    /// corpus-consistency test proves every shipped template uses only known tokens/pools).</summary>
    public string Render(
        string templateKey, IReadOnlyDictionary<string, string> tokens, Pcg32 stream, string fallback)
    {
        if (!_templates.TryGetValue(templateKey, out var variants) || variants.Count == 0)
            return fallback;
        string template = variants[stream.NextInt(0, variants.Count)];
        return Expand(template, tokens, stream, depth: 0);
    }

    /// <summary>A rotating "paddock rumor" line (the <c>rumor</c> pool), chosen and expanded from
    /// <paramref name="stream"/> so it rotates across the career yet stays stable on a re-open (the caller
    /// keys the stream off a slow-moving value like the applied-round count). Empty when no rumor pool is
    /// authored.</summary>
    public string Rumor(IReadOnlyDictionary<string, string> tokens, Pcg32 stream)
    {
        if (!_pools.TryGetValue("rumor", out var lines) || lines.Count == 0)
            return "";
        string line = lines[stream.NextInt(0, lines.Count)];
        return Expand(line, tokens, stream, depth: 0);
    }

    /// <summary>Single-pass expansion: resolve each <c>{token}</c>, <c>{pool:name}</c> selects a fragment
    /// (recursively expanded, same stream); a plain token substitutes a fact value (missing → empty).
    /// Substituted values are not re-scanned, so a fact containing braces prints verbatim.</summary>
    private string Expand(string text, IReadOnlyDictionary<string, string> tokens, Pcg32 stream, int depth)
    {
        if (depth > MaxExpansionDepth)
            throw new InvalidOperationException(
                $"SMGP dispatch expansion exceeded depth {MaxExpansionDepth}, a pool likely references " +
                "itself. Break the cycle in the corpus.");

        return TokenPattern.Replace(text, match =>
        {
            string token = match.Groups[1].Value;
            if (token.StartsWith("pool:", StringComparison.Ordinal))
            {
                string poolName = token["pool:".Length..];
                if (!_pools.TryGetValue(poolName, out var fragments) || fragments.Count == 0)
                    throw new InvalidOperationException(
                        $"SMGP dispatch references undeclared pool '{poolName}'.");
                string fragment = fragments[stream.NextInt(0, fragments.Count)];
                return Expand(fragment, tokens, stream, depth + 1);
            }

            // Lenient: an unknown token collapses to nothing rather than crashing the live feed. Tests
            // guard the shipped corpus so this never actually fires in production.
            return tokens.TryGetValue(token, out string? value) ? value : "";
        });
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Freeze(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? map)
    {
        var frozen = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (map is not null)
            foreach (var (key, list) in map)
                frozen[key] = list ?? [];
        return frozen;
    }

    private sealed record CorpusDto
    {
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Templates { get; init; } =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, IReadOnlyList<string>> Pools { get; init; } =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
    }
}
