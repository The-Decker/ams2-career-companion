using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Companion.Core.Packs;

/// <summary>
/// The entry "rounds" expression: which 1-based calendar rounds an entry participates in,
/// written as "1-11", "4", or "1,3,5-8". Parses to a distinct, sorted set of round numbers;
/// overlapping segments merge. Bounds against the season's round count are the validator's job.
/// </summary>
public sealed class RoundsRange
{
    /// <summary>Sanity cap: no season segment spans this many rounds.</summary>
    private const int MaxSpan = 500;

    private readonly HashSet<int> _lookup;

    private RoundsRange(SortedSet<int> rounds)
    {
        Rounds = rounds.ToArray();
        _lookup = [.. rounds];
    }

    /// <summary>The distinct round numbers, ascending.</summary>
    public IReadOnlyList<int> Rounds { get; }

    public int Min => Rounds[0];

    public int Max => Rounds[^1];

    public bool Contains(int round) => _lookup.Contains(round);

    public static RoundsRange Parse(string? text) =>
        TryParse(text, out var range, out var error) ? range : throw new FormatException(error);

    public static bool TryParse(string? text, [NotNullWhen(true)] out RoundsRange? range) =>
        TryParse(text, out range, out _);

    public static bool TryParse(
        string? text,
        [NotNullWhen(true)] out RoundsRange? range,
        [NotNullWhen(false)] out string? error)
    {
        range = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Rounds range is empty.";
            return false;
        }

        var rounds = new SortedSet<int>();
        foreach (var segment in text.Split(','))
        {
            var token = segment.Trim();
            if (token.Length == 0)
            {
                error = $"Rounds range '{text}' has an empty segment.";
                return false;
            }

            int dash = token.IndexOf('-');
            if (dash < 0)
            {
                if (!TryParseRound(token, out int round))
                {
                    error = $"Rounds range segment '{token}' is not a round number (1 or greater).";
                    return false;
                }
                rounds.Add(round);
                continue;
            }

            if (!TryParseRound(token[..dash].Trim(), out int from) ||
                !TryParseRound(token[(dash + 1)..].Trim(), out int to))
            {
                error = $"Rounds range segment '{token}' is not of the form 'from-to' with round numbers 1 or greater.";
                return false;
            }
            if (to < from)
            {
                error = $"Rounds range segment '{token}' runs backwards.";
                return false;
            }
            if (to - from >= MaxSpan)
            {
                error = $"Rounds range segment '{token}' spans more than {MaxSpan} rounds.";
                return false;
            }

            for (int round = from; round <= to; round++)
                rounds.Add(round);
        }

        range = new RoundsRange(rounds);
        return true;
    }

    /// <summary>Positive integer only: no sign, no whitespace, no decimals.</summary>
    private static bool TryParseRound(string token, out int round) =>
        int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out round) && round >= 1;

    /// <summary>Canonical compact form: consecutive runs collapse to "from-to" ("1,3,5-8").</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        int i = 0;
        while (i < Rounds.Count)
        {
            int j = i;
            while (j + 1 < Rounds.Count && Rounds[j + 1] == Rounds[j] + 1)
                j++;
            parts.Add(j == i
                ? Rounds[i].ToString(CultureInfo.InvariantCulture)
                : $"{Rounds[i]}-{Rounds[j]}");
            i = j + 1;
        }
        return string.Join(",", parts);
    }
}
