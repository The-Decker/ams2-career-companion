using System.Text.Json.Serialization;
using Companion.Core.Json;

namespace Companion.Core.Smgp;

/// <summary>
/// The SMGP team-season history capsules (mission SMGP-024): the base universe's record of one
/// team in one season, 24 teams x 17 seasons = 408 capsules, loaded from
/// <c>data/rules/smgp/capsules/sNN.json</c>. This is the world's own arc, NOT the player career:
/// capsules never name a season's champion and never mention the player. DISPLAY-ONLY reference
/// content (the season pages and team dossiers read it), never a fold input. Absent-tolerant: a
/// missing directory or season file simply yields fewer entries.
/// </summary>
public sealed class SmgpSeasonCapsules
{
    private readonly IReadOnlyDictionary<(string TeamId, int Season), SmgpTeamSeasonCapsule> _byTeamSeason;

    private SmgpSeasonCapsules(
        IReadOnlyDictionary<(string TeamId, int Season), SmgpTeamSeasonCapsule> byTeamSeason) =>
        _byTeamSeason = byTeamSeason;

    /// <summary>An empty catalog (no capsule files shipped): every lookup returns null.</summary>
    public static SmgpSeasonCapsules Empty { get; } =
        new(new Dictionary<(string, int), SmgpTeamSeasonCapsule>());

    /// <summary>The total capsules loaded (408 on a full install).</summary>
    public int Count => _byTeamSeason.Count;

    /// <summary>The seasons with a capsule file (1..17 on a full install).</summary>
    public IReadOnlyCollection<int> Seasons =>
        _byTeamSeason.Keys.Select(k => k.Season).Distinct().OrderBy(s => s).ToArray();

    /// <summary>One team's capsule in one season, or null when unauthored.</summary>
    public SmgpTeamSeasonCapsule? ForTeamSeason(string teamId, int season) =>
        _byTeamSeason.GetValueOrDefault((teamId, season));

    /// <summary>Every team's capsule for one season (24 on a full install).</summary>
    public IReadOnlyDictionary<string, SmgpTeamSeasonCapsule> ForSeason(int season) =>
        _byTeamSeason.Where(kv => kv.Key.Season == season)
            .ToDictionary(kv => kv.Key.TeamId, kv => kv.Value, StringComparer.Ordinal);

    /// <summary>One team's full seventeen-season arc, in season order (17 on a full install).</summary>
    public IReadOnlyList<(int Season, SmgpTeamSeasonCapsule Capsule)> ForTeam(string teamId) =>
        _byTeamSeason.Where(kv => string.Equals(kv.Key.TeamId, teamId, StringComparison.Ordinal))
            .OrderBy(kv => kv.Key.Season)
            .Select(kv => (kv.Key.Season, kv.Value))
            .ToArray();

    /// <summary>Loads every <c>sNN.json</c> under <c>data/rules/smgp/capsules</c>, or
    /// <see cref="Empty"/> when the directory is absent.</summary>
    public static SmgpSeasonCapsules Load(string rulesDirectory)
    {
        string directory = Path.Combine(rulesDirectory, "smgp", "capsules");
        if (!Directory.Exists(directory))
            return Empty;

        var byTeamSeason = new Dictionary<(string, int), SmgpTeamSeasonCapsule>();
        foreach (string file in Directory.EnumerateFiles(directory, "s*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            var seasonFile = System.Text.Json.JsonSerializer.Deserialize<SeasonFileDto>(
                File.ReadAllText(file), CoreJson.Options);
            if (seasonFile is null || seasonFile.Season <= 0)
                continue;
            foreach (var (teamId, capsule) in seasonFile.Capsules)
                if (capsule is not null)
                    byTeamSeason[(teamId, seasonFile.Season)] = capsule;
        }

        return new SmgpSeasonCapsules(byTeamSeason);
    }

    private sealed record SeasonFileDto
    {
        [JsonPropertyName("season")]
        public int Season { get; init; }

        [JsonPropertyName("capsules")]
        public IReadOnlyDictionary<string, SmgpTeamSeasonCapsule?> Capsules { get; init; } =
            new Dictionary<string, SmgpTeamSeasonCapsule?>(StringComparer.Ordinal);
    }
}

/// <summary>One team-season capsule: the season as the team lived it, its objective, its
/// defining event, and the consequence carried into the next season.</summary>
public sealed record SmgpTeamSeasonCapsule
{
    [JsonPropertyName("summary")]
    public string Summary { get; init; } = "";

    [JsonPropertyName("objective")]
    public string Objective { get; init; } = "";

    [JsonPropertyName("definingEvent")]
    public string DefiningEvent { get; init; } = "";

    [JsonPropertyName("carryForward")]
    public string CarryForward { get; init; } = "";
}
