using System.Text;
using System.Text.RegularExpressions;
using Companion.Core.Determinism;

namespace Companion.Core.Newsroom;

/// <summary>
/// The newsroom's template grammar, the proven <c>{token}</c>/<c>{pool:name}</c> engine grown
/// for editorial prose (docs/dev/newsroom-history-overhaul.md D5):
/// <list type="bullet">
/// <item><c>{token}</c>, substitute a fact value; UNKNOWN tokens throw (validation-tested).</item>
/// <item><c>{a:token}</c>, indefinite article + value ("a win" / "an eighth place").</item>
/// <item><c>{token's}</c>, possessive ("Senna's", "Brabhams'").</item>
/// <item><c>{ord:token}</c>, ordinal for a numeric value ("4" → "4th").</item>
/// <item><c>{pool:name}</c>, one fragment from a named pool, drawn from the supplied stream,
/// recursively expanded (depth-capped).</item>
/// <item><c>[[?token: text]]</c>, optional segment: rendered (and inner-expanded) only when
/// the token has a non-empty value, missing optional facts drop cleanly, never "null".</item>
/// </list>
/// Substituted values are never re-scanned. A finishing pass tidies whitespace and duplicate
/// punctuation so degraded segments still read like copy a sub-editor passed.
/// </summary>
public static class NewsroomGrammar
{
    public const int MaxExpansionDepth = 8;

    private static readonly Regex OptionalSegment = new(@"\[\[\?([a-zA-Z][a-zA-Z0-9]*):(.*?)\]\]",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Token = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    /// <summary>Expands a template against fact tokens + pools. <paramref name="poolLookup"/>
    /// resolves a pool name to its fragment list (era-resolved by the caller) or null.</summary>
    public static string Expand(
        string template,
        IReadOnlyDictionary<string, string> tokens,
        Func<string, IReadOnlyList<string>?> poolLookup,
        Pcg32 stream)
    {
        var expanded = ExpandCore(template, tokens, poolLookup, stream, depth: 0);
        return Tidy(expanded);
    }

    private static string ExpandCore(
        string template,
        IReadOnlyDictionary<string, string> tokens,
        Func<string, IReadOnlyList<string>?> poolLookup,
        Pcg32 stream,
        int depth)
    {
        if (depth > MaxExpansionDepth)
        {
            throw new InvalidOperationException(
                $"Template expansion exceeded depth {MaxExpansionDepth}, a pool probably references itself.");
        }

        // Optional segments first, so their inner tokens are only demanded when present.
        var text = OptionalSegment.Replace(template, m =>
        {
            var guard = m.Groups[1].Value;
            return tokens.TryGetValue(guard, out var value) && value.Length > 0
                ? ExpandCore(m.Groups[2].Value, tokens, poolLookup, stream, depth + 1)
                : "";
        });

        return Token.Replace(text, m =>
        {
            var body = m.Groups[1].Value;

            if (body.StartsWith("pool:", StringComparison.Ordinal))
            {
                var poolName = body["pool:".Length..];
                var fragments = poolLookup(poolName)
                    ?? throw new InvalidOperationException($"Undeclared pool '{poolName}'.");
                if (fragments.Count == 0)
                {
                    throw new InvalidOperationException($"Pool '{poolName}' is empty.");
                }
                var fragment = fragments[stream.NextInt(0, fragments.Count)];
                return ExpandCore(fragment, tokens, poolLookup, stream, depth + 1);
            }

            return ResolveToken(body, tokens);
        });
    }

    /// <summary>Resolves one token body, allowing prefixes to STACK left-to-right:
    /// <c>{a:ord:position}</c> = indefinite article over the ordinal of the position.</summary>
    private static string ResolveToken(string body, IReadOnlyDictionary<string, string> tokens)
    {
        if (body.StartsWith("a:", StringComparison.Ordinal))
        {
            var value = ResolveToken(body["a:".Length..], tokens);
            return value.Length == 0 ? "" : IndefiniteArticle(value) + " " + value;
        }

        if (body.StartsWith("ord:", StringComparison.Ordinal))
        {
            var value = ResolveToken(body["ord:".Length..], tokens);
            return int.TryParse(value, out var n) ? Ordinal(n) : value;
        }

        if (body.EndsWith("'s", StringComparison.Ordinal))
        {
            return Possessive(ResolveToken(body[..^2], tokens));
        }

        return Lookup(tokens, body);
    }

    private static string Lookup(IReadOnlyDictionary<string, string> tokens, string name) =>
        tokens.TryGetValue(name, out var value)
            ? value
            : throw new InvalidOperationException($"Unknown template token '{name}'.");

    /// <summary>"an" before a vowel SOUND approximation: vowel letters, minus common
    /// consonant-sounding exceptions ("one", "european", "unique"-style u-words).</summary>
    public static string IndefiniteArticle(string value)
    {
        if (value.Length == 0)
        {
            return "a";
        }

        var lower = value.ToLowerInvariant();
        if (lower.StartsWith("one", StringComparison.Ordinal)
            || lower.StartsWith("uni", StringComparison.Ordinal)
            || lower.StartsWith("eu", StringComparison.Ordinal)
            || lower.StartsWith("use", StringComparison.Ordinal))
        {
            return "a";
        }

        // "8th"/"11th"/"18th" read as vowel sounds ("eighth", "eleventh", "eighteenth").
        if (char.IsDigit(lower[0]))
        {
            return lower[0] == '8' || lower.StartsWith("11", StringComparison.Ordinal)
                || lower.StartsWith("18", StringComparison.Ordinal)
                ? "an"
                : "a";
        }

        return "aeiou".Contains(lower[0]) ? "an" : "a";
    }

    public static string Possessive(string value) =>
        value.Length == 0 ? "" :
        value.EndsWith('s') ? value + "'" : value + "'s";

    public static string Ordinal(int n)
    {
        var tens = Math.Abs(n) % 100;
        var units = Math.Abs(n) % 10;
        var suffix = tens is 11 or 12 or 13 ? "th" : units switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{n}{suffix}");
    }

    /// <summary>Post-expansion copy-editing: dropped optional segments must leave no seams —
    /// no doubled spaces, no space before punctuation, no doubled sentence punctuation, no
    /// dangling separators, first letter of each sentence upper-cased.</summary>
    public static string Tidy(string text)
    {
        var sb = new StringBuilder(text.Length);
        var previousNonSpace = '\0';
        var pendingSpace = false;

        foreach (var raw in text)
        {
            var c = raw;
            if (c is ' ' or '\t')
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (c is '.' or ',' or ';' or ':' or '!' or '?')
            {
                // No space before punctuation; collapse doubled punctuation (".." → ".",
                // ",." → ".", " , " → ", ").
                if (previousNonSpace is '.' or ',' or ';' or ':' or '!' or '?')
                {
                    if (previousNonSpace != c && (c is '.' or '!' or '?'))
                    {
                        sb[^1] = c; // terminal punctuation wins over a comma/semicolon
                        previousNonSpace = c;
                    }
                    pendingSpace = false;
                    continue;
                }
                pendingSpace = false;
                sb.Append(c);
                previousNonSpace = c;
                pendingSpace = false;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
            previousNonSpace = c;
        }

        // Sentence-initial capitals (a dropped leading segment can leave "the rest…").
        var result = sb.ToString().Trim();
        if (result.Length == 0)
        {
            return result;
        }

        var chars = result.ToCharArray();
        var capitalize = true;
        for (var i = 0; i < chars.Length; i++)
        {
            if (capitalize && char.IsLetter(chars[i]))
            {
                chars[i] = char.ToUpperInvariant(chars[i]);
                capitalize = false;
            }
            else if (chars[i] is '.' or '!' or '?')
            {
                capitalize = true;
            }
        }

        return new string(chars);
    }
}
