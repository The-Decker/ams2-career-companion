using System.Text.Json.Serialization;
using Companion.Core.Json;

namespace Companion.Core.Smgp;

/// <summary>
/// The SMGP-universe team profiles: each SEGA-world team's own quotes and multi-paragraph history,
/// keyed by TEAM id (e.g. "team.madonna"). Loaded from <c>data/rules/smgp/team-profiles.json</c>.
/// DISPLAY-ONLY — never a fold input (exactly like the news corpora, <see cref="SmgpRivalQuotes"/> and
/// <see cref="SmgpWhatReallyHappened"/>): the promotion/demotion screen shows the new team's story when
/// you move up or down the ladder. An absent file (or an un-authored team) resolves to null, so a
/// non-SMGP install or an un-updated data folder is simply unaffected.
/// </summary>
public sealed class SmgpTeamProfiles
{
    private readonly IReadOnlyDictionary<string, SmgpTeamProfile> _byTeam;

    private SmgpTeamProfiles(IReadOnlyDictionary<string, SmgpTeamProfile> byTeam) => _byTeam = byTeam;

    /// <summary>An empty catalog (no file shipped): every lookup returns null and <see cref="Teams"/>
    /// is empty, so the promotion screen simply omits the team story.</summary>
    public static SmgpTeamProfiles Empty { get; } =
        new(new Dictionary<string, SmgpTeamProfile>(StringComparer.Ordinal));

    /// <summary>This team's SMGP-world profile, or null when none is authored for it.</summary>
    public SmgpTeamProfile? ForTeam(string teamId) => _byTeam.GetValueOrDefault(teamId);

    /// <summary>The team ids the catalog has an authored profile for (drift-guard source).</summary>
    public IReadOnlyCollection<string> Teams => _byTeam.Keys.ToArray();

    /// <summary>Loads <c>data/rules/smgp/team-profiles.json</c> from the rules directory, or
    /// <see cref="Empty"/> when the file is absent.</summary>
    public static SmgpTeamProfiles Load(string rulesDirectory)
    {
        string path = Path.Combine(rulesDirectory, "smgp", "team-profiles.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
    }

    public static SmgpTeamProfiles Parse(string json)
    {
        var dto = System.Text.Json.JsonSerializer.Deserialize<ProfilesDto>(json, CoreJson.Options)
            ?? new ProfilesDto();
        var byTeam = new Dictionary<string, SmgpTeamProfile>(StringComparer.Ordinal);
        foreach (var (teamId, profile) in dto.Teams)
            if (profile is not null)
                byTeam[teamId] = profile;
        return new SmgpTeamProfiles(byTeam);
    }

    private sealed record ProfilesDto
    {
        [JsonPropertyName("teams")]
        public IReadOnlyDictionary<string, SmgpTeamProfile?> Teams { get; init; } =
            new Dictionary<string, SmgpTeamProfile?>(StringComparer.Ordinal);
    }
}

/// <summary>One SMGP-world team's profile — display-only reference content shown when the player
/// joins the team (a promotion or demotion). Fully fictional (the SEGA universe, never real F1).</summary>
public sealed record SmgpTeamProfile
{
    /// <summary>The team's display name ("Madonna").</summary>
    public string Name { get; init; } = "";

    /// <summary>A one-line arcade motto / tagline for the team ("THE CROWN NEVER SLIPS").</summary>
    public string Motto { get; init; } = "";

    /// <summary>The team's SMGP-world history — a few paragraphs (Mike: ~5).</summary>
    public IReadOnlyList<string> History { get; init; } = [];

    /// <summary>A few in-character team quotes (the principal, the garage, a rival's dig).</summary>
    public IReadOnlyList<string> Quotes { get; init; } = [];
}
