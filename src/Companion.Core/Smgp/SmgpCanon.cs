using System.Text.Json.Serialization;
using Companion.Core.Json;

namespace Companion.Core.Smgp;

/// <summary>
/// The SMGP canonical identity registry (mission SMGP-024, canon lock): the ONE authoritative
/// source for the 24 fictional SMGP teams, their 24 permanent cars, and their 17 permanent
/// engine specifications, loaded from <c>data/rules/smgp/canon.json</c>. Identity rule: team
/// name, car name, and engine name never change across the 17 SMGP seasons; only the
/// season/year changes. Derived systems (car-spec cards, dossiers, news, history, validation)
/// read identity through this catalog rather than maintaining their own lists. DISPLAY-ONLY
/// reference data, never a fold input. An absent file resolves to <see cref="Empty"/>, so a
/// non-SMGP install is simply unaffected.
/// </summary>
public sealed class SmgpCanon
{
    public const string ExpectedCanonVersion = "smgp-24-v1";
    public const int ExpectedTeamCount = 24;
    public const int ExpectedEngineCount = 17;
    public const int ExpectedSeasonCount = 17;

    private readonly IReadOnlyDictionary<string, SmgpCanonTeam> _teamsById;
    private readonly IReadOnlyDictionary<string, SmgpCanonEngine> _enginesById;
    private readonly IReadOnlyDictionary<string, string> _teamIdByAlias;

    private SmgpCanon(
        string canonVersion,
        string mode,
        int seasons,
        IReadOnlyDictionary<string, SmgpCanonTeam> teamsById,
        IReadOnlyDictionary<string, SmgpCanonEngine> enginesById,
        IReadOnlyDictionary<string, string> teamIdByAlias)
    {
        CanonVersion = canonVersion;
        Mode = mode;
        Seasons = seasons;
        _teamsById = teamsById;
        _enginesById = enginesById;
        _teamIdByAlias = teamIdByAlias;
    }

    public static SmgpCanon Empty { get; } = new(
        "",
        "",
        0,
        new Dictionary<string, SmgpCanonTeam>(StringComparer.Ordinal),
        new Dictionary<string, SmgpCanonEngine>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>The registry's format/canon version ("smgp-24-v1").</summary>
    public string CanonVersion { get; }

    /// <summary>Mode ownership of every identity in the registry ("smgp").</summary>
    public string Mode { get; }

    /// <summary>The season count the identity lock spans (17).</summary>
    public int Seasons { get; }

    /// <summary>All 24 canonical teams, keyed by pack team id ("team.madonna").</summary>
    public IReadOnlyCollection<SmgpCanonTeam> Teams => _teamsById.Values.ToArray();

    /// <summary>All 17 official engine specifications, keyed by engine id.</summary>
    public IReadOnlyCollection<SmgpCanonEngine> Engines => _enginesById.Values.ToArray();

    /// <summary>The canonical team for a pack team id, or null when unknown.</summary>
    public SmgpCanonTeam? ForTeam(string teamId) => _teamsById.GetValueOrDefault(teamId);

    /// <summary>The canonical engine for an engine id, or null when unknown.</summary>
    public SmgpCanonEngine? ForEngine(string engineId) => _enginesById.GetValueOrDefault(engineId);

    /// <summary>Resolves a user/import/save-facing team name (any casing, any registered alias)
    /// to its canonical team id: "AZELIA" -> "team.azalea". Returns null when the string is not
    /// a known team name or alias. Aliases normalize, they are never displayed.</summary>
    public string? NormalizeTeamName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        string trimmed = name.Trim();
        foreach (var (id, team) in _teamsById)
            if (string.Equals(trimmed, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, team.DisplayName, StringComparison.OrdinalIgnoreCase))
                return id;
        return _teamIdByAlias.GetValueOrDefault(trimmed);
    }

    /// <summary>The derived identity for one team in one season. The canon lock makes every
    /// season 1..17 return the SAME team/car/engine display strings; season metadata (ordinal,
    /// year) is the only thing that varies. Null when the team is unknown or the season is
    /// outside the registry's span.</summary>
    public SmgpSeasonIdentity? SeasonIdentity(string teamId, int season)
    {
        var team = ForTeam(teamId);
        if (team is null || season < team.FirstSeason || season > team.LastSeason)
            return null;
        return new SmgpSeasonIdentity(
            season, team.Id, team.DisplayName, team.CarId, team.CarDisplayName,
            team.EngineId, team.EngineDisplayName);
    }

    /// <summary>Structural validation, the build-time/test-time loud failure the mission
    /// requires. Returns every violation found; an empty list means the registry is sound:
    /// exactly 24 teams and 17 engines, ids unique, every team's engine resolvable, every
    /// team locked for seasons 1..17, no alias colliding with a canonical id or display
    /// name, and no identity string carrying a year/model suffix.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!string.Equals(CanonVersion, ExpectedCanonVersion, StringComparison.Ordinal))
            errors.Add($"canonVersion is '{CanonVersion}', expected '{ExpectedCanonVersion}'.");
        if (!string.Equals(Mode, "smgp", StringComparison.Ordinal))
            errors.Add($"mode is '{Mode}', expected 'smgp'.");
        if (Seasons != ExpectedSeasonCount)
            errors.Add($"seasons is {Seasons}, expected {ExpectedSeasonCount}.");
        if (_teamsById.Count != ExpectedTeamCount)
            errors.Add($"team count is {_teamsById.Count}, expected {ExpectedTeamCount}.");
        if (_enginesById.Count != ExpectedEngineCount)
            errors.Add($"engine count is {_enginesById.Count}, expected {ExpectedEngineCount}.");

        foreach (var team in _teamsById.Values)
        {
            if (!_enginesById.ContainsKey(team.EngineId))
                errors.Add($"{team.Id}: engine '{team.EngineId}' is not in the engine registry.");
            if (!team.IdentityLocked)
                errors.Add($"{team.Id}: identityLocked must be true.");
            if (team.FirstSeason != 1 || team.LastSeason != Seasons)
                errors.Add($"{team.Id}: season span {team.FirstSeason}-{team.LastSeason}, expected 1-{Seasons}.");
            foreach (string s in new[] { team.DisplayName, team.CarDisplayName, team.EngineDisplayName })
                if (HasForbiddenSuffix(s))
                    errors.Add($"{team.Id}: identity string '{s}' carries a year/model suffix.");
            // Car ids follow "<team>-<model>" by convention (team.madonna -> madonna-456); a
            // mismatch almost always means a typo in one of the two fields.
            if (team.CarDisplayName.StartsWith(team.DisplayName, StringComparison.Ordinal))
            {
                string expectedCarId =
                    $"{team.Id["team.".Length..]}-{team.CarDisplayName[team.DisplayName.Length..].Trim().ToLowerInvariant().Replace(' ', '-')}";
                if (!string.Equals(team.CarId, expectedCarId, StringComparison.Ordinal))
                    errors.Add($"{team.Id}: carId '{team.CarId}' does not match convention '{expectedCarId}'.");
            }
        }

        foreach (var (alias, teamId) in _teamIdByAlias)
        {
            if (!_teamsById.ContainsKey(teamId))
                errors.Add($"alias '{alias}' points at unknown team '{teamId}'.");
            if (_teamsById.Values.Any(t =>
                    string.Equals(alias, t.DisplayName, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"alias '{alias}' collides with a canonical display name.");
        }

        return errors;
    }

    /// <summary>A year or model-increment suffix on an identity string is the signature of the
    /// banned seasonal-rename pattern ("MADONNA 457", "IRIS 717B", "PRISM 91 V10"). Checks only
    /// the string's tail; interior digits are canon (MADONNA 456, LORRY 32 V8).</summary>
    private static bool HasForbiddenSuffix(string value)
    {
        string[] suffixes = [" EVO", " EVOLUTION", " MK II", " MK III", " SPEC 2", " GEN 2", " TURBO", " HYBRID"];
        foreach (string suffix in suffixes)
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        if (value.EndsWith("B", StringComparison.Ordinal) && value.Length > 2 &&
            char.IsDigit(value[^2]))
            return true; // "717B" style model letters
        return false;
    }

    /// <summary>Loads <c>data/rules/smgp/canon.json</c> from the rules directory, or
    /// <see cref="Empty"/> when the file is absent.</summary>
    public static SmgpCanon Load(string rulesDirectory)
    {
        string path = Path.Combine(rulesDirectory, "smgp", "canon.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
    }

    public static SmgpCanon Parse(string json)
    {
        var dto = System.Text.Json.JsonSerializer.Deserialize<CanonDto>(json, CoreJson.Options)
            ?? new CanonDto();
        var teams = new Dictionary<string, SmgpCanonTeam>(StringComparer.Ordinal);
        foreach (var team in dto.Teams)
            if (team is not null && !string.IsNullOrEmpty(team.Id))
                teams[team.Id] = team;
        var engines = new Dictionary<string, SmgpCanonEngine>(StringComparer.Ordinal);
        foreach (var engine in dto.Engines)
            if (engine is not null && !string.IsNullOrEmpty(engine.Id))
                engines[engine.Id] = engine;
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var team in teams.Values)
            foreach (string alias in team.Aliases)
                aliases[alias] = team.Id;
        return new SmgpCanon(
            dto.CanonVersion ?? "", dto.Mode ?? "", dto.Seasons, teams, engines, aliases);
    }

    private sealed record CanonDto
    {
        [JsonPropertyName("canonVersion")]
        public string? CanonVersion { get; init; }

        [JsonPropertyName("mode")]
        public string? Mode { get; init; }

        [JsonPropertyName("seasons")]
        public int Seasons { get; init; }

        [JsonPropertyName("engines")]
        public IReadOnlyList<SmgpCanonEngine?> Engines { get; init; } = [];

        [JsonPropertyName("teams")]
        public IReadOnlyList<SmgpCanonTeam?> Teams { get; init; } = [];
    }
}

/// <summary>One official engine specification. Shared engines stay one registry entry used by
/// several teams (LIZZIE 24 V8 powers TYRANT, BULLETS and COMET); the teams remain separate.</summary>
public sealed record SmgpCanonEngine
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = "";
}

/// <summary>One canonical SMGP team's permanent identity: team, car, and engine names locked
/// for every one of the 17 seasons. Aliases normalize legacy spellings on load/import and are
/// never displayed.</summary>
public sealed record SmgpCanonTeam
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = "";

    [JsonPropertyName("carId")]
    public string CarId { get; init; } = "";

    [JsonPropertyName("carDisplayName")]
    public string CarDisplayName { get; init; } = "";

    [JsonPropertyName("engineId")]
    public string EngineId { get; init; } = "";

    [JsonPropertyName("engineDisplayName")]
    public string EngineDisplayName { get; init; } = "";

    [JsonPropertyName("maxPowerHp")]
    public int MaxPowerHp { get; init; }

    [JsonPropertyName("firstSeason")]
    public int FirstSeason { get; init; } = 1;

    [JsonPropertyName("lastSeason")]
    public int LastSeason { get; init; } = 17;

    [JsonPropertyName("identityLocked")]
    public bool IdentityLocked { get; init; } = true;

    [JsonPropertyName("aliases")]
    public IReadOnlyList<string> Aliases { get; init; } = [];
}

/// <summary>One team-season identity row, the derived 408-combination view (24 teams x 17
/// seasons). The canon lock keeps the display strings identical in every season.</summary>
public sealed record SmgpSeasonIdentity(
    int Season,
    string TeamId,
    string TeamDisplayName,
    string CarId,
    string CarDisplayName,
    string EngineId,
    string EngineDisplayName);
