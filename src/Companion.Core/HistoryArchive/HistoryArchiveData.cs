using System.Text.Json;
using System.Text.Json.Serialization;

namespace Companion.Core.HistoryArchive;

/// <summary>A data-driven era definition (data/history/eras.json). Authored, sourced,
/// verified-historical; boundaries tile the documented year span exactly (validated).</summary>
public sealed record HistoricalEra
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required int FromYear { get; init; }
    public required int ToYear { get; init; }
    public string Overview { get; init; } = "";
    public IReadOnlyList<string> DefiningCharacteristics { get; init; } = [];
    public string EngineTrends { get; init; } = "";
    public string SafetyContext { get; init; } = "";
    public IReadOnlyList<string> RegulationChanges { get; init; } = [];
    public string Legacy { get; init; } = "";
    public string Provenance { get; init; } = "";
    public IReadOnlyList<string> Sources { get; init; } = [];
}

/// <summary>A first-class technology/regulation/safety subject (data/history/subjects.json).
/// Incomplete records say so honestly (<see cref="IsComplete"/>) rather than inventing facts.</summary>
public sealed record HistorySubject
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    /// <summary>"technology" | "regulation" | "safety".</summary>
    public required string Category { get; init; }
    public string Summary { get; init; } = "";
    public IReadOnlyList<string> Body { get; init; } = [];
    public int FromYear { get; init; }
    public int? ToYear { get; init; }
    public IReadOnlyList<int> RelatedYears { get; init; } = [];
    public string Provenance { get; init; } = "";
    public bool IsComplete { get; init; } = true;
    public IReadOnlyList<string> Sources { get; init; } = [];
}

/// <summary>A documented team-lineage relationship, never a silent merge: two historically
/// connected teams stay distinct entities with the connection on record.</summary>
public sealed record TeamLineageLink
{
    public required string RelatedTo { get; init; }
    /// <summary>"renamed" | "succeeded" | "merged" | "purchased".</summary>
    public required string Relationship { get; init; }
    public string Note { get; init; } = "";
}

/// <summary>One team identity (data/history/aliases.json): the canonical display name, the
/// exact data-file strings it groups (engine-suffix variants), and documented lineage.</summary>
public sealed record TeamIdentity
{
    public required string Canonical { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public int FirstYear { get; init; }
    public int LastYear { get; init; }
    public IReadOnlyList<TeamLineageLink> Lineage { get; init; } = [];
    public bool IsComplete { get; init; } = true;
    public string Provenance { get; init; } = "";
    public IReadOnlyList<string> Sources { get; init; } = [];
}

/// <summary>
/// The authored history-archive reference data: eras, subjects, team identities. Loaded from
/// the app-shipped data/history folder beside the 60 season files; read-only, display-only,
/// never a fold input. Missing files degrade to empty (the archive sections simply hide).
/// </summary>
public sealed class HistoryArchiveData
{
    public required IReadOnlyList<HistoricalEra> Eras { get; init; }
    public required IReadOnlyList<HistorySubject> Subjects { get; init; }
    public required IReadOnlyList<TeamIdentity> Teams { get; init; }
    public string ErasSource { get; init; } = "";
    public string SubjectsSource { get; init; } = "";
    public string TeamsSource { get; init; } = "";

    public static HistoryArchiveData Empty { get; } = new() { Eras = [], Subjects = [], Teams = [] };

    public bool IsEmpty => Eras.Count == 0 && Subjects.Count == 0 && Teams.Count == 0;

    public HistoricalEra? EraForYear(int year) =>
        Eras.FirstOrDefault(e => year >= e.FromYear && year <= e.ToYear);

    /// <summary>Canonical team identity for a raw data-file team string (exact alias match),
    /// or null when the string is unknown (callers keep the raw string, never guess).</summary>
    public TeamIdentity? TeamForAlias(string teamString) =>
        _aliasIndex.Value.GetValueOrDefault(teamString);

    private readonly Lazy<Dictionary<string, TeamIdentity>> _aliasIndex;

    public HistoryArchiveData()
    {
        _aliasIndex = new Lazy<Dictionary<string, TeamIdentity>>(() =>
        {
            var index = new Dictionary<string, TeamIdentity>(StringComparer.Ordinal);
            foreach (var team in Teams)
            {
                foreach (var alias in team.Aliases)
                {
                    index.TryAdd(alias, team);
                }
            }
            return index;
        });
    }

    private static readonly JsonSerializerOptions LoadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private sealed record ErasDto(string? Source, IReadOnlyList<HistoricalEra>? Eras);
    private sealed record SubjectsDto(string? Source, IReadOnlyList<HistorySubject>? Subjects);
    private sealed record TeamsDto(string? Source, IReadOnlyList<TeamIdentity>? Teams);

    public static HistoryArchiveData Load(string? historyDirectory)
    {
        if (historyDirectory is null || !Directory.Exists(historyDirectory))
        {
            return Empty;
        }

        var eras = TryRead<ErasDto>(Path.Combine(historyDirectory, "eras.json"));
        var subjects = TryRead<SubjectsDto>(Path.Combine(historyDirectory, "subjects.json"));
        var teams = TryRead<TeamsDto>(Path.Combine(historyDirectory, "aliases.json"));

        return new HistoryArchiveData
        {
            Eras = eras?.Eras ?? [],
            Subjects = subjects?.Subjects ?? [],
            Teams = teams?.Teams ?? [],
            ErasSource = eras?.Source ?? "",
            SubjectsSource = subjects?.Source ?? "",
            TeamsSource = teams?.Source ?? "",
        };
    }

    private static T? TryRead<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), LoadOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null; // a malformed reference file hides its section, never crashes the app
        }
    }

    /// <summary>Authoring validation, run by the content tests over the shipped files:
    /// era tiling (no gaps/overlaps across the documented span), unique keys/ids, required
    /// prose present, subject categories from the fixed vocabulary, lineage references
    /// resolving to real canonical names, no alias claimed by two identities.</summary>
    public void Validate()
    {
        var eraKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var era in Eras)
        {
            if (!eraKeys.Add(era.Key))
            {
                throw new InvalidOperationException($"Duplicate era key '{era.Key}'.");
            }
            if (era.Overview.Length == 0 || era.Name.Length == 0)
            {
                throw new InvalidOperationException($"Era '{era.Key}' is missing prose.");
            }
            if (era.Sources.Count == 0)
            {
                throw new InvalidOperationException($"Era '{era.Key}' carries no sources.");
            }
        }
        var ordered = Eras.OrderBy(e => e.FromYear).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].FromYear != ordered[i - 1].ToYear + 1)
            {
                throw new InvalidOperationException(
                    $"Era '{ordered[i].Key}' does not start where '{ordered[i - 1].Key}' ends.");
            }
        }

        var subjectIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var subject in Subjects)
        {
            if (!subjectIds.Add(subject.Id))
            {
                throw new InvalidOperationException($"Duplicate subject id '{subject.Id}'.");
            }
            if (subject.Category is not ("technology" or "regulation" or "safety"))
            {
                throw new InvalidOperationException(
                    $"Subject '{subject.Id}' has unknown category '{subject.Category}'.");
            }
            if (subject.Body.Count == 0 || subject.Body.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException($"Subject '{subject.Id}' has an empty body.");
            }
            if (subject.Sources.Count == 0)
            {
                throw new InvalidOperationException($"Subject '{subject.Id}' carries no sources.");
            }
        }

        var canonicals = new HashSet<string>(StringComparer.Ordinal);
        var claimedAliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var team in Teams)
        {
            if (!canonicals.Add(team.Canonical))
            {
                throw new InvalidOperationException($"Duplicate canonical team '{team.Canonical}'.");
            }
            foreach (var alias in team.Aliases)
            {
                if (!claimedAliases.Add(alias))
                {
                    throw new InvalidOperationException(
                        $"Alias '{alias}' is claimed by more than one team identity.");
                }
            }
        }
        foreach (var team in Teams)
        {
            foreach (var link in team.Lineage)
            {
                if (!canonicals.Contains(link.RelatedTo))
                {
                    throw new InvalidOperationException(
                        $"Team '{team.Canonical}' lineage references unknown '{link.RelatedTo}'.");
                }
                if (link.Relationship is not ("renamed" or "succeeded" or "merged" or "purchased"))
                {
                    throw new InvalidOperationException(
                        $"Team '{team.Canonical}' has unknown relationship '{link.Relationship}'.");
                }
                if (string.Equals(link.RelatedTo, team.Canonical, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Team '{team.Canonical}' lineage references itself.");
                }
            }
        }
    }
}
