using System.Text.Json.Serialization;
using Companion.Core.Json;

namespace Companion.Core.Smgp;

/// <summary>
/// The SMGP world's PREDETERMINED driver career stats, the history as it stood the moment the player
/// arrived (a whole number of prior seasons). Keyed by DRIVER id, loaded from
/// <c>data/rules/smgp/driver-stats.json</c>. DISPLAY-ONLY, never a fold input (like
/// <see cref="SmgpDriverProfiles"/>): shown on the Paddock tab and (as the player's own running total)
/// the rival readout. An absent file resolves to <see cref="Empty"/>, so a non-SMGP install is
/// unaffected. The player's LIVE season / career stats accrue on top of these baselines from actual
/// results (computed elsewhere from the career DB).
/// </summary>
public sealed class SmgpDriverStats
{
    private readonly IReadOnlyDictionary<string, SmgpDriverStatLine> _byDriver;

    private SmgpDriverStats(
        int loreSeasons, int roundsPerSeason,
        IReadOnlyList<SmgpSeasonChampion> champions,
        IReadOnlyDictionary<string, SmgpDriverStatLine> byDriver)
    {
        LoreSeasons = loreSeasons;
        RoundsPerSeason = roundsPerSeason;
        Champions = champions;
        _byDriver = byDriver;
    }

    /// <summary>How many seasons the SMGP world ran before the player arrived.</summary>
    public int LoreSeasons { get; }

    /// <summary>Rounds per lore season (for coherence display / totals).</summary>
    public int RoundsPerSeason { get; }

    /// <summary>The champion of each prior season (season number → driver id).</summary>
    public IReadOnlyList<SmgpSeasonChampion> Champions { get; }

    public static SmgpDriverStats Empty { get; } =
        new(0, 0, [], new Dictionary<string, SmgpDriverStatLine>(StringComparer.Ordinal));

    /// <summary>This driver's predetermined career stats, or null when none are authored.</summary>
    public SmgpDriverStatLine? ForDriver(string driverId) => _byDriver.GetValueOrDefault(driverId);

    /// <summary>The driver ids the catalog has stats for (drift-guard source).</summary>
    public IReadOnlyCollection<string> Drivers => _byDriver.Keys.ToArray();

    /// <summary>Loads <c>data/rules/smgp/driver-stats.json</c> from the rules directory, or
    /// <see cref="Empty"/> when the file is absent.</summary>
    public static SmgpDriverStats Load(string rulesDirectory)
    {
        string path = Path.Combine(rulesDirectory, "smgp", "driver-stats.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
    }

    public static SmgpDriverStats Parse(string json)
    {
        var dto = System.Text.Json.JsonSerializer.Deserialize<StatsDto>(json, CoreJson.Options)
            ?? new StatsDto();
        var byDriver = new Dictionary<string, SmgpDriverStatLine>(StringComparer.Ordinal);
        foreach (var line in dto.Drivers)
            if (line is { DriverId.Length: > 0 })
                byDriver[line.DriverId] = line;
        return new SmgpDriverStats(dto.LoreSeasons, dto.RoundsPerSeason, dto.Champions ?? [], byDriver);
    }

    private sealed record StatsDto
    {
        [JsonPropertyName("loreSeasons")] public int LoreSeasons { get; init; }
        [JsonPropertyName("roundsPerSeason")] public int RoundsPerSeason { get; init; }
        [JsonPropertyName("champions")] public IReadOnlyList<SmgpSeasonChampion>? Champions { get; init; }
        [JsonPropertyName("drivers")] public IReadOnlyList<SmgpDriverStatLine?> Drivers { get; init; } = [];
    }
}

/// <summary>One prior season's champion (the SMGP world's title history).</summary>
public sealed record SmgpSeasonChampion
{
    public int Season { get; init; }
    public string DriverId { get; init; } = "";
}

/// <summary>One driver's predetermined career totals (pre-player-era). All non-negative; by construction
/// <c>CareerStarts &gt;= CareerTop5s &gt;= CareerPodiums &gt;= CareerWins</c>.</summary>
public sealed record SmgpDriverStatLine
{
    public string DriverId { get; init; } = "";
    public int CareerStarts { get; init; }
    public int CareerWins { get; init; }
    public int CareerPodiums { get; init; }
    public int CareerPoles { get; init; }
    public int CareerTop5s { get; init; }
    public int CareerPoints { get; init; }
    public int Championships { get; init; }
}
