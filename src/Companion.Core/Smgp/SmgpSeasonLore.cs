using System.Text.Json.Serialization;
using Companion.Core.Json;

namespace Companion.Core.Smgp;

/// <summary>
/// The authored 17-season SMGP campaign lore (<c>data/rules/smgp/seasons.json</c>): every season's
/// unique identity — title, era, overview, preseason/technical/safety context, themes, canon
/// timeline beats, story arcs, newsroom hooks, contenders, and milestone opportunities. SMGP CANON
/// (fiction), authored so no two seasons read alike while the closed 34-driver universe stays
/// coherent across the campaign arc. DISPLAY-ONLY — never a fold input (exactly like
/// <see cref="SmgpWhatReallyHappened"/>): season outcomes remain the sim's alone, so the lore
/// never asserts a campaign result. An absent file resolves to <see cref="Empty"/>, so an
/// un-updated data folder simply shows the plain "SEASON n / 17" header.
/// </summary>
public sealed class SmgpSeasonLore
{
    private readonly IReadOnlyDictionary<int, SmgpSeasonLoreEntry> _byOrdinal;

    private SmgpSeasonLore(IReadOnlyDictionary<int, SmgpSeasonLoreEntry> byOrdinal) => _byOrdinal = byOrdinal;

    /// <summary>An empty lore book (no file shipped): every lookup returns null.</summary>
    public static SmgpSeasonLore Empty { get; } = new(new Dictionary<int, SmgpSeasonLoreEntry>());

    /// <summary>The authored lore for one campaign season ordinal (1-17), or null when none.</summary>
    public SmgpSeasonLoreEntry? ForOrdinal(int ordinal) => _byOrdinal.GetValueOrDefault(ordinal);

    /// <summary>Every authored season, ordinal order.</summary>
    public IReadOnlyList<SmgpSeasonLoreEntry> Seasons =>
        _byOrdinal.Values.OrderBy(s => s.Ordinal).ToArray();

    public bool IsEmpty => _byOrdinal.Count == 0;

    /// <summary>Loads <c>data/rules/smgp/seasons.json</c>, or <see cref="Empty"/> when absent.</summary>
    public static SmgpSeasonLore Load(string rulesDirectory)
    {
        string path = Path.Combine(rulesDirectory, "smgp", "seasons.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
    }

    public static SmgpSeasonLore Parse(string json)
    {
        var dto = System.Text.Json.JsonSerializer.Deserialize<LoreDto>(json, CoreJson.Options)
            ?? new LoreDto();
        var byOrdinal = new Dictionary<int, SmgpSeasonLoreEntry>();
        foreach (var season in dto.Seasons)
        {
            if (season is null)
            {
                continue;
            }
            if (!byOrdinal.TryAdd(season.Ordinal, season))
            {
                throw new System.Text.Json.JsonException(
                    $"Duplicate SMGP season lore ordinal {season.Ordinal}.");
            }
        }
        return new SmgpSeasonLore(byOrdinal);
    }

    private sealed record LoreDto
    {
        [JsonPropertyName("version")]
        public int Version { get; init; } = 1;

        [JsonPropertyName("seasons")]
        public IReadOnlyList<SmgpSeasonLoreEntry?> Seasons { get; init; } = [];
    }
}

/// <summary>One campaign season's authored identity. All prose is SMGP canon (fiction) written to
/// be outcome-agnostic: the sim decides every result; the lore supplies the world around it.</summary>
public sealed record SmgpSeasonLoreEntry
{
    /// <summary>1-based campaign season ordinal (1-17).</summary>
    public required int Ordinal { get; init; }

    /// <summary>The season's unique evocative title ("The Tenth Summer").</summary>
    public required string Title { get; init; }

    /// <summary>One theme line under the title.</summary>
    public string Subtitle { get; init; } = "";

    /// <summary>The era block this season belongs to ("The Iron Circus", "The Horsepower War",
    /// "The Safety Reckoning", "The Golden Circus").</summary>
    public string Era { get; init; } = "";

    /// <summary>The substantial season overview.</summary>
    public string Overview { get; init; } = "";

    /// <summary>Preseason context entry.</summary>
    public string Preseason { get; init; } = "";

    /// <summary>Technical/regulation context entry.</summary>
    public string Technical { get; init; } = "";

    /// <summary>Safety/medical-era context entry.</summary>
    public string Safety { get; init; } = "";

    /// <summary>The season's major themes (4+).</summary>
    public IReadOnlyList<string> Themes { get; init; } = [];

    /// <summary>Baseline canon timeline beats (6+), outcome-agnostic.</summary>
    public IReadOnlyList<string> Timeline { get; init; } = [];

    /// <summary>Driver/team story arcs (4+), roster names only.</summary>
    public IReadOnlyList<string> Arcs { get; init; } = [];

    /// <summary>Short article/event hook lines the newsroom can echo (8+).</summary>
    public IReadOnlyList<string> Hooks { get; init; } = [];

    /// <summary>Key title contenders by exact roster driver name (3+).</summary>
    public IReadOnlyList<string> Contenders { get; init; } = [];

    /// <summary>Record/milestone opportunities plausible this season (2+).</summary>
    public IReadOnlyList<string> Milestones { get; init; } = [];
}
