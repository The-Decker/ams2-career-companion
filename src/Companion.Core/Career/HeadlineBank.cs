using System.Text.Json;
using System.Text.RegularExpressions;
using Companion.Core.Determinism;
using Companion.Core.Json;

namespace Companion.Core.Career;

/// <summary>
/// The headline template bank (data/rules/career-headline-templates.json): variants keyed by
/// journal <c>phase|cause</c>, with era-flavored variant lists and a "default" fallback.
/// Selection consumes the `headlines` stream, so the news feed replays byte-identically.
/// </summary>
public sealed class HeadlineBank
{
    /// <summary>Era key + year range; the "default" era has no range and is the fallback.</summary>
    public required IReadOnlyList<HeadlineEra> Eras { get; init; }

    /// <summary>"phase|cause" -> era key -> variants.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> Templates { get; init; }

    public const string DefaultEra = "default";

    public static HeadlineBank Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<BankDto>(json, CoreJson.Options)
                  ?? throw new JsonException("Headline template file is empty.");
        if (dto.Templates is not { Count: > 0 })
            throw new JsonException("Headline template file declares no templates.");

        var eraKeys = new HashSet<string>(dto.Eras.Select(e => e.Key), StringComparer.Ordinal)
        {
            DefaultEra,
        };

        foreach (var (key, byEra) in dto.Templates)
        {
            if (!key.Contains('|'))
                throw new JsonException($"Template key '{key}' is not of the form 'phase|cause'.");
            foreach (var (era, variants) in byEra)
            {
                if (!eraKeys.Contains(era))
                    throw new JsonException($"Template '{key}' references undeclared era '{era}'.");
                if (variants is not { Count: > 0 })
                    throw new JsonException($"Template '{key}' era '{era}' has no variants.");
            }
        }

        return new HeadlineBank { Eras = dto.Eras, Templates = dto.Templates };
    }

    /// <summary>The era key for a year: the first declared era whose range contains it,
    /// else "default".</summary>
    public string ResolveEra(int year)
    {
        foreach (var era in Eras)
        {
            if (era.FromYear <= year && year <= era.ToYear)
                return era.Key;
        }
        return DefaultEra;
    }

    /// <summary>Variants for a phase+cause in the year's era, falling back to "default".
    /// Empty when the bank has no entry for the key at all.</summary>
    public IReadOnlyList<string> Variants(string phase, string cause, int year)
    {
        if (!Templates.TryGetValue(phase + "|" + cause, out var byEra))
            return [];
        if (byEra.TryGetValue(ResolveEra(year), out var variants))
            return variants;
        return byEra.TryGetValue(DefaultEra, out var fallback) ? fallback : [];
    }

    private sealed record BankDto
    {
        public IReadOnlyList<HeadlineEra> Eras { get; init; } = [];
        public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> Templates { get; init; }
    }
}

public sealed record HeadlineEra
{
    public required string Key { get; init; }
    public required int FromYear { get; init; }
    public required int ToYear { get; init; }
}

/// <summary>Deterministic headline selection: one variant picked with the `headlines`
/// stream, then a SINGLE-PASS {token} substitution, the template is scanned once with a
/// regex, each token resolves by ordinal lookup, and substituted values are never re-scanned
/// (a player named "{team} Kid" stays "{team} Kid" in print). Unknown tokens throw at
/// selection time so template bugs surface in tests, not journals.</summary>
public static class HeadlineSelector
{
    private static readonly Regex TokenPattern = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    /// <summary>Selects a headline, or null when the bank has no variants for the key
    /// (missing keys must not break the sim, the journal row still stands on its own).</summary>
    /// <exception cref="InvalidOperationException">A selected template uses a token the
    /// caller did not supply.</exception>
    public static string? Select(
        HeadlineBank bank,
        string phase,
        string cause,
        int year,
        IReadOnlyDictionary<string, string> tokens,
        Pcg32 headlineStream)
    {
        var variants = bank.Variants(phase, cause, year);
        if (variants.Count == 0)
            return null;

        // Ordinal lookup regardless of the caller dictionary's comparer.
        var lookup = new Dictionary<string, string>(tokens, StringComparer.Ordinal);

        string template = variants[headlineStream.NextInt(0, variants.Count)];
        return TokenPattern.Replace(template, match =>
            lookup.TryGetValue(match.Groups[1].Value, out string? value)
                ? value
                : throw new InvalidOperationException(
                    $"Headline template for '{phase}|{cause}' uses unknown token " +
                    $"'{{{match.Groups[1].Value}}}': {template}"));
    }

    /// <summary>English ordinal ("1st", "2nd", "3rd", "11th", …) for {position} tokens.</summary>
    public static string Ordinal(int position)
    {
        int mod100 = position % 100;
        string suffix = mod100 is 11 or 12 or 13
            ? "th"
            : (position % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th",
            };
        return position + suffix;
    }
}
