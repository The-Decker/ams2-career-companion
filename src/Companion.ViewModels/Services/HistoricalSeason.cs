using System.Text.Json;

namespace Companion.ViewModels.Services;

/// <summary>The real historical results of one F1 season — "what really happened" reference content
/// the History tab shows ALONGSIDE the player's own (diverged) career. f1db-derived (CC BY 4.0), baked
/// into <c>data/history/&lt;year&gt;.json</c> by <c>scratchpad/derive_history.cs</c>. Read-only: the
/// sim/fold never scores it, so it can never affect determinism.</summary>
public sealed record HistoricalSeason
{
    public required int Year { get; init; }

    /// <summary>Attribution line (CC BY 4.0) — shown in the History panel.</summary>
    public string? Source { get; init; }

    public HistoricalChampion? DriversChampion { get; init; }

    /// <summary>The championship runner-up (driver + points; no team) — used to compose the season's
    /// title-margin summary. Null when unknown.</summary>
    public HistoricalChampion? RunnerUp { get; init; }

    public HistoricalTeamChampion? ConstructorsChampion { get; init; }

    public IReadOnlyList<HistoricalRound> Rounds { get; init; } = [];
}

/// <summary>The drivers' champion of a historical season.</summary>
public sealed record HistoricalChampion
{
    public required string Driver { get; init; }
    public string? Team { get; init; }
    /// <summary>Championship points as text (e.g. "51", "51.5") — display only.</summary>
    public string? Points { get; init; }
}

/// <summary>The constructors' champion of a historical season.</summary>
public sealed record HistoricalTeamChampion
{
    public required string Team { get; init; }
    public string? Points { get; init; }
}

/// <summary>One historical race: the winner, fastest lap, and the full classified order.</summary>
public sealed record HistoricalRound
{
    public required int Round { get; init; }
    /// <summary>The Grand Prix name ("South African Grand Prix").</summary>
    public required string Name { get; init; }
    public string? Winner { get; init; }
    public string? WinnerTeam { get; init; }
    public string? FastestLap { get; init; }
    /// <summary>The full classified result, in finishing order (retirements last, in their
    /// retirement order — exactly as f1db lists them).</summary>
    public IReadOnlyList<HistoricalResult> Results { get; init; } = [];
}

/// <summary>One driver's line in a historical race result.</summary>
public sealed record HistoricalResult
{
    /// <summary>Finishing position text — "1".."26", or "DNF"/"NC"/"DSQ"/"DNQ".</summary>
    public required string Pos { get; init; }
    public required string Driver { get; init; }
    public required string Team { get; init; }
    /// <summary>Retirement reason for a non-finisher ("Engine", "Accident"), else null.</summary>
    public string? Status { get; init; }
}

/// <summary>Loads the shipped historical-season reference files on demand, keyed by year.</summary>
public interface IHistoricalSeasonStore
{
    /// <summary>The real results for <paramref name="year"/>, or null when none is shipped (a year
    /// outside the baked range, or no history directory). Never throws.</summary>
    HistoricalSeason? ForYear(int year);
}

/// <summary>
/// Reads <c>&lt;historyDirectory&gt;/&lt;year&gt;.json</c> on first request and caches the result
/// (including a null "not present" so a missing year is probed only once). A missing directory,
/// missing file, or corrupt JSON resolves to null — the History tab must never crash on reference
/// data, it simply omits the "what really happened" panel for that year.
/// </summary>
public sealed class HistoricalSeasonStore : IHistoricalSeasonStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string? _directory;
    private readonly Dictionary<int, HistoricalSeason?> _cache = [];

    public HistoricalSeasonStore(string? historyDirectory) => _directory = historyDirectory;

    public HistoricalSeason? ForYear(int year)
    {
        if (_cache.TryGetValue(year, out var cached))
            return cached;
        var season = Load(year);
        _cache[year] = season;
        return season;
    }

    private HistoricalSeason? Load(int year)
    {
        if (string.IsNullOrEmpty(_directory))
            return null;
        string path = Path.Combine(_directory, year.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".json");
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<HistoricalSeason>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
