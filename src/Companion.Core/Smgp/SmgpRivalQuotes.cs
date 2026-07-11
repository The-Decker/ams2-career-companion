using System.Text.Json.Serialization;
using Companion.Core.Json;

namespace Companion.Core.Smgp;

/// <summary>The rival's mood when the briefing offers him — drives which line he says. Derived from
/// the two-wins tally: fresh (never engaged / streaks reset), the player one win up (the seat is one
/// win away), or the rival one win up (he is one win from taking the player's seat).</summary>
public enum SmgpRivalMood
{
    /// <summary>First challenge — neither has a live streak on the other.</summary>
    First,

    /// <summary>The player has beaten him once without losing — one more win takes his seat.</summary>
    PlayerLeads,

    /// <summary>He has beaten the player once — one more loss and he takes the player's seat.</summary>
    RivalLeads,
}

/// <summary>
/// The SMGP rivals' trash-talk: each driver's own lines for each <see cref="SmgpRivalMood"/>, so the
/// dossier says something DIFFERENT per character AND per situation (Mike: "every character will say
/// something different to you ... depending on if you first challenged them or beat them once").
/// Loaded from <c>data/rules/smgp/rival-quotes.json</c>. DISPLAY-ONLY — the briefing quote is never a
/// fold input (like the news corpora), so the line is chosen from a deterministic per-round seed only
/// so a re-open shows the same line. A driver with no authored lines (or an absent file) falls back
/// to the shared pool, and finally to a single deadpan constant, so the panel always has a line.
/// </summary>
public sealed class SmgpRivalQuotes
{
    private readonly IReadOnlyDictionary<string, MoodLines> _byDriver;
    private readonly MoodLines _fallback;

    private SmgpRivalQuotes(IReadOnlyDictionary<string, MoodLines> byDriver, MoodLines fallback)
    {
        _byDriver = byDriver;
        _fallback = fallback;
    }

    /// <summary>The last-resort line when nothing is authored — the arcade's own deadpan default.</summary>
    public const string Default = "IT'S INTERESTING.";

    /// <summary>An empty bank (no file shipped): every lookup returns <see cref="Default"/>.</summary>
    public static SmgpRivalQuotes Empty { get; } = new(
        new Dictionary<string, MoodLines>(StringComparer.Ordinal), new MoodLines());

    /// <summary>This driver's line for the mood, chosen deterministically from
    /// <paramref name="seed"/> (a per-round hash) so a re-opened briefing shows the same line. Falls
    /// back to the shared pool, then the deadpan default.</summary>
    public string Line(string driverId, SmgpRivalMood mood, uint seed)
    {
        var pool = Pool(_byDriver.GetValueOrDefault(driverId), mood);
        if (pool.Count == 0)
            pool = Pool(_fallback, mood);
        if (pool.Count == 0)
            return Default;
        return pool[(int)(seed % (uint)pool.Count)];
    }

    private static IReadOnlyList<string> Pool(MoodLines? lines, SmgpRivalMood mood) => mood switch
    {
        SmgpRivalMood.PlayerLeads => lines?.PlayerLeads ?? [],
        SmgpRivalMood.RivalLeads => lines?.RivalLeads ?? [],
        _ => lines?.First ?? [],
    };

    /// <summary>Loads <c>data/rules/smgp/rival-quotes.json</c> from the rules directory, or
    /// <see cref="Empty"/> when the file is absent (back-compat: the panel keeps the deadpan default).</summary>
    public static SmgpRivalQuotes Load(string rulesDirectory)
    {
        string path = Path.Combine(rulesDirectory, "smgp", "rival-quotes.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
    }

    public static SmgpRivalQuotes Parse(string json)
    {
        var dto = System.Text.Json.JsonSerializer.Deserialize<QuotesDto>(json, CoreJson.Options)
            ?? new QuotesDto();
        var byDriver = new Dictionary<string, MoodLines>(StringComparer.Ordinal);
        foreach (var (id, lines) in dto.Drivers)
            byDriver[id] = lines ?? new MoodLines();
        return new SmgpRivalQuotes(byDriver, dto.Fallback ?? new MoodLines());
    }

    /// <summary>One driver's (or the shared fallback's) lines per mood. Absent lists default to
    /// empty so a partially-authored driver still resolves (and back-fills from the fallback).</summary>
    public sealed record MoodLines
    {
        public IReadOnlyList<string> First { get; init; } = [];
        public IReadOnlyList<string> PlayerLeads { get; init; } = [];
        public IReadOnlyList<string> RivalLeads { get; init; } = [];
    }

    private sealed record QuotesDto
    {
        public MoodLines? Fallback { get; init; }

        [JsonPropertyName("drivers")]
        public IReadOnlyDictionary<string, MoodLines?> Drivers { get; init; } =
            new Dictionary<string, MoodLines?>(StringComparer.Ordinal);
    }
}
