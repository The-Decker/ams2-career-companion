using Companion.Core.HistoryArchive;

namespace Companion.ViewModels.Services;

/// <summary>A real driver's verified record, AGGREGATED from the shipped season files
/// (f1db-derived, CC BY 4.0) — computed, never authored, never mixed with career results
/// (docs/dev/newsroom-history-overhaul.md D10). Name strings are the identity the data gives us.</summary>
public sealed record DriverHistoryProfile
{
    public required string Name { get; init; }
    public required int FirstYear { get; init; }
    public required int LastYear { get; init; }
    public required int SeasonsEntered { get; init; }
    /// <summary>Race starts: classified finishes plus on-track retirements/DSQ; DNQ/DNPQ/DNS
    /// rows are entries that never started and are excluded.</summary>
    public required int Starts { get; init; }
    public required int Wins { get; init; }
    public required int Podiums { get; init; }
    public required int FastestLaps { get; init; }
    public IReadOnlyList<int> ChampionshipYears { get; init; } = [];
    public IReadOnlyList<int> RunnerUpYears { get; init; } = [];
    /// <summary>Teams driven for, with the years the data shows (raw data-file strings).</summary>
    public IReadOnlyList<DriverTeamStint> Stints { get; init; } = [];
    public IReadOnlyList<RaceRef> WinList { get; init; } = [];
}

public sealed record DriverTeamStint(string Team, int FirstYear, int LastYear);

public sealed record RaceRef(int Year, int Round, string RaceName);

/// <summary>A team identity's verified record aggregated across its documented alias strings.
/// Historically connected teams stay separate; the lineage links carry the connection.</summary>
public sealed record TeamHistoryProfile
{
    public required string Canonical { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public required int FirstYear { get; init; }
    public required int LastYear { get; init; }
    public required int Wins { get; init; }
    public IReadOnlyList<int> ConstructorsChampionshipYears { get; init; } = [];
    public IReadOnlyList<int> DriversChampionshipYears { get; init; } = [];
    public required int DriversFielded { get; init; }
    public IReadOnlyList<TeamLineageLink> Lineage { get; init; } = [];
    public bool IsComplete { get; init; } = true;
}

/// <summary>A circuit's verified record keyed by f1db layout id, with every documented edition.</summary>
public sealed record CircuitHistoryProfile
{
    public required string LayoutId { get; init; }
    public required string Name { get; init; }
    public string Place { get; init; } = "";
    public string LengthKm { get; init; } = "";
    public int? Turns { get; init; }
    public string History { get; init; } = "";
    public IReadOnlyList<string> Facts { get; init; } = [];
    public IReadOnlyList<CircuitEdition> Editions { get; init; } = [];
}

public sealed record CircuitEdition(int Year, int Round, string RaceName, string Winner, string WinnerTeam);

/// <summary>One verified-history timeline entry (year precision — the season files carry no
/// dates). Career-universe entries are merged by the ViewModel layer with their own provenance.</summary>
public sealed record HistoryTimelineEntry
{
    public required int Year { get; init; }
    public required string Category { get; init; }
    public required string Title { get; init; }
    public string Summary { get; init; } = "";
    public string EraKey { get; init; } = "";
    public required string Provenance { get; init; }
    /// <summary>Cross-navigation key: "season:{year}" | "subject:{id}" | "era:{key}".</summary>
    public string RelatedKey { get; init; } = "";
}

/// <summary>The complete computed archive: entity profiles + reference data + timeline.</summary>
public sealed record HistoryArchiveIndex
{
    public required IReadOnlyList<DriverHistoryProfile> Drivers { get; init; }
    public required IReadOnlyList<TeamHistoryProfile> Teams { get; init; }
    public required IReadOnlyList<CircuitHistoryProfile> Circuits { get; init; }
    public required IReadOnlyList<HistoryTimelineEntry> Timeline { get; init; }
    public required HistoryArchiveData Reference { get; init; }
    public required IReadOnlyList<int> YearsCovered { get; init; }

    public static HistoryArchiveIndex Empty { get; } = new()
    {
        Drivers = [],
        Teams = [],
        Circuits = [],
        Timeline = [],
        Reference = HistoryArchiveData.Empty,
        YearsCovered = [],
    };
}

/// <summary>
/// Builds the archive index by one pass over every shipped season file. Pure aggregation of
/// verified data — zero invented facts; unknown team strings stay their own entities marked
/// incomplete. Built once per session and cached (the reference data never changes at runtime).
/// </summary>
public static class HistoryEntityIndex
{
    public const int ProbeFromYear = 1950;
    public const int ProbeToYear = 2035;

    private static readonly string[] NonStartPositions = ["DNQ", "DNPQ", "DNS"];

    public static HistoryArchiveIndex Build(
        Func<int, HistoricalSeason?> seasonForYear,
        HistoryArchiveData reference)
    {
        var years = new List<int>();
        var seasons = new List<HistoricalSeason>();
        for (var year = ProbeFromYear; year <= ProbeToYear; year++)
        {
            if (seasonForYear(year) is { } season)
            {
                years.Add(year);
                seasons.Add(season);
            }
        }

        if (seasons.Count == 0)
        {
            return HistoryArchiveIndex.Empty with { Reference = reference };
        }

        return new HistoryArchiveIndex
        {
            Drivers = BuildDrivers(seasons),
            Teams = BuildTeams(seasons, reference),
            Circuits = BuildCircuits(seasons),
            Timeline = BuildTimeline(seasons, reference),
            Reference = reference,
            YearsCovered = years,
        };
    }

    private sealed class DriverAccumulator
    {
        public int First = int.MaxValue, Last = int.MinValue, Starts, Wins, Podiums, FastestLaps;
        public readonly HashSet<int> Seasons = [];
        public readonly List<int> Titles = [];
        public readonly List<int> RunnerUps = [];
        public readonly Dictionary<string, (int First, int Last)> Stints = new(StringComparer.Ordinal);
        public readonly List<RaceRef> WinList = [];
    }

    private static IReadOnlyList<DriverHistoryProfile> BuildDrivers(IReadOnlyList<HistoricalSeason> seasons)
    {
        var drivers = new Dictionary<string, DriverAccumulator>(StringComparer.Ordinal);

        DriverAccumulator For(string name)
        {
            if (!drivers.TryGetValue(name, out var acc))
            {
                drivers[name] = acc = new DriverAccumulator();
            }
            return acc;
        }

        foreach (var season in seasons)
        {
            foreach (var round in season.Rounds)
            {
                foreach (var result in round.Results)
                {
                    var acc = For(result.Driver);
                    acc.Seasons.Add(season.Year);
                    acc.First = Math.Min(acc.First, season.Year);
                    acc.Last = Math.Max(acc.Last, season.Year);
                    if (!NonStartPositions.Contains(result.Pos))
                    {
                        acc.Starts++;
                    }
                    if (result.Pos == "1")
                    {
                        acc.Wins++;
                        acc.WinList.Add(new RaceRef(season.Year, round.Round, round.Name));
                    }
                    if (result.Pos is "1" or "2" or "3")
                    {
                        acc.Podiums++;
                    }
                    if (acc.Stints.TryGetValue(result.Team, out var stint))
                    {
                        acc.Stints[result.Team] = (Math.Min(stint.First, season.Year), Math.Max(stint.Last, season.Year));
                    }
                    else
                    {
                        acc.Stints[result.Team] = (season.Year, season.Year);
                    }
                }

                if (round.FastestLap is { Length: > 0 } fl)
                {
                    For(fl).FastestLaps++;
                }
            }

            if (season.DriversChampion?.Driver is { Length: > 0 } champion)
            {
                For(champion).Titles.Add(season.Year);
            }
            if (season.RunnerUp?.Driver is { Length: > 0 } runnerUp)
            {
                For(runnerUp).RunnerUps.Add(season.Year);
            }
        }

        return drivers
            .Where(kv => kv.Value.Seasons.Count > 0)
            .Select(kv => new DriverHistoryProfile
            {
                Name = kv.Key,
                FirstYear = kv.Value.First == int.MaxValue ? 0 : kv.Value.First,
                LastYear = kv.Value.Last == int.MinValue ? 0 : kv.Value.Last,
                SeasonsEntered = kv.Value.Seasons.Count,
                Starts = kv.Value.Starts,
                Wins = kv.Value.Wins,
                Podiums = kv.Value.Podiums,
                FastestLaps = kv.Value.FastestLaps,
                ChampionshipYears = kv.Value.Titles,
                RunnerUpYears = kv.Value.RunnerUps,
                Stints = kv.Value.Stints
                    .Select(s => new DriverTeamStint(s.Key, s.Value.First, s.Value.Last))
                    .OrderBy(s => s.FirstYear).ThenBy(s => s.Team, StringComparer.Ordinal)
                    .ToList(),
                WinList = kv.Value.WinList,
            })
            .OrderByDescending(d => d.Wins)
            .ThenByDescending(d => d.Podiums)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToList();
    }

    private sealed class TeamAccumulator
    {
        public int First = int.MaxValue, Last = int.MinValue, Wins;
        public readonly HashSet<string> Drivers = new(StringComparer.Ordinal);
        public readonly List<int> ConstructorsTitles = [];
        public readonly List<int> DriversTitles = [];
    }

    private static IReadOnlyList<TeamHistoryProfile> BuildTeams(
        IReadOnlyList<HistoricalSeason> seasons, HistoryArchiveData reference)
    {
        // Aggregate by CANONICAL identity when the alias table knows the string; unknown
        // strings become their own incomplete entity (never silently merged).
        var teams = new Dictionary<string, TeamAccumulator>(StringComparer.Ordinal);
        var unknown = new HashSet<string>(StringComparer.Ordinal);

        string CanonicalOf(string raw)
        {
            var identity = reference.TeamForAlias(raw);
            if (identity is null)
            {
                unknown.Add(raw);
                return raw;
            }
            return identity.Canonical;
        }

        TeamAccumulator For(string canonical)
        {
            if (!teams.TryGetValue(canonical, out var acc))
            {
                teams[canonical] = acc = new TeamAccumulator();
            }
            return acc;
        }

        foreach (var season in seasons)
        {
            foreach (var round in season.Rounds)
            {
                foreach (var result in round.Results)
                {
                    var acc = For(CanonicalOf(result.Team));
                    acc.First = Math.Min(acc.First, season.Year);
                    acc.Last = Math.Max(acc.Last, season.Year);
                    acc.Drivers.Add(result.Driver);
                    if (result.Pos == "1")
                    {
                        acc.Wins++;
                    }
                }
            }

            if (season.ConstructorsChampion?.Team is { Length: > 0 } constructors)
            {
                For(CanonicalOf(constructors)).ConstructorsTitles.Add(season.Year);
            }
            if (season.DriversChampion?.Team is { Length: > 0 } driversTeam)
            {
                For(CanonicalOf(driversTeam)).DriversTitles.Add(season.Year);
            }
        }

        return teams
            .Select(kv =>
            {
                var identity = reference.Teams.FirstOrDefault(t =>
                    string.Equals(t.Canonical, kv.Key, StringComparison.Ordinal));
                return new TeamHistoryProfile
                {
                    Canonical = kv.Key,
                    Aliases = identity?.Aliases ?? [kv.Key],
                    FirstYear = kv.Value.First == int.MaxValue ? 0 : kv.Value.First,
                    LastYear = kv.Value.Last == int.MinValue ? 0 : kv.Value.Last,
                    Wins = kv.Value.Wins,
                    ConstructorsChampionshipYears = kv.Value.ConstructorsTitles,
                    DriversChampionshipYears = kv.Value.DriversTitles,
                    DriversFielded = kv.Value.Drivers.Count,
                    Lineage = identity?.Lineage ?? [],
                    IsComplete = identity is not null && identity.IsComplete && !unknown.Contains(kv.Key),
                };
            })
            .OrderByDescending(t => t.Wins)
            .ThenBy(t => t.Canonical, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<CircuitHistoryProfile> BuildCircuits(IReadOnlyList<HistoricalSeason> seasons)
    {
        var circuits = new Dictionary<string, (HistoricalCircuit Latest, int LatestYear, List<CircuitEdition> Editions)>(StringComparer.Ordinal);

        foreach (var season in seasons)
        {
            foreach (var round in season.Rounds)
            {
                if (round.Circuit?.LayoutId is not { Length: > 0 } layoutId)
                {
                    continue;
                }

                if (!circuits.TryGetValue(layoutId, out var entry))
                {
                    entry = (round.Circuit, season.Year, []);
                }
                else if (season.Year >= entry.LatestYear)
                {
                    entry = (round.Circuit, season.Year, entry.Editions);
                }
                entry.Editions.Add(new CircuitEdition(
                    season.Year, round.Round, round.Name, round.Winner ?? "", round.WinnerTeam ?? ""));
                circuits[layoutId] = entry;
            }
        }

        return circuits
            .Select(kv => new CircuitHistoryProfile
            {
                LayoutId = kv.Key,
                Name = kv.Value.Latest.Name ?? kv.Key,
                Place = kv.Value.Latest.Place ?? "",
                LengthKm = kv.Value.Latest.LengthKm ?? "",
                Turns = kv.Value.Latest.Turns,
                History = kv.Value.Latest.History ?? "",
                Facts = kv.Value.Latest.Facts,
                Editions = kv.Value.Editions,
            })
            .OrderByDescending(c => c.Editions.Count)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<HistoryTimelineEntry> BuildTimeline(
        IReadOnlyList<HistoricalSeason> seasons, HistoryArchiveData reference)
    {
        var timeline = new List<HistoryTimelineEntry>();

        foreach (var era in reference.Eras)
        {
            timeline.Add(new HistoryTimelineEntry
            {
                Year = era.FromYear,
                Category = "era",
                Title = $"{era.Name} begins",
                Summary = era.Overview,
                EraKey = era.Key,
                Provenance = "verifiedHistorical",
                RelatedKey = $"era:{era.Key}",
            });
        }

        foreach (var subject in reference.Subjects)
        {
            timeline.Add(new HistoryTimelineEntry
            {
                Year = subject.FromYear,
                Category = subject.Category,
                Title = subject.Title,
                Summary = subject.Summary,
                EraKey = reference.EraForYear(subject.FromYear)?.Key ?? "",
                Provenance = subject.Provenance.Length > 0 ? subject.Provenance : "verifiedHistorical",
                RelatedKey = $"subject:{subject.Id}",
            });
        }

        foreach (var season in seasons)
        {
            if (season.DriversChampion is not { Driver.Length: > 0 } champion)
            {
                continue; // an in-progress reference year has no champion yet — say nothing
            }

            var summary = champion.Team is { Length: > 0 } team
                ? $"{champion.Driver} ({team}) takes the drivers' championship"
                : $"{champion.Driver} takes the drivers' championship";
            if (season.ConstructorsChampion?.Team is { Length: > 0 } constructors)
            {
                summary += $"; {constructors} wins the constructors' title";
            }

            timeline.Add(new HistoryTimelineEntry
            {
                Year = season.Year,
                Category = "championship",
                Title = $"{season.Year}: {champion.Driver} champion",
                Summary = summary + ".",
                EraKey = reference.EraForYear(season.Year)?.Key ?? "",
                Provenance = "verifiedHistorical",
                RelatedKey = $"season:{season.Year}",
            });
        }

        return timeline
            .OrderBy(t => t.Year)
            .ThenBy(t => t.Category, StringComparer.Ordinal)
            .ThenBy(t => t.Title, StringComparer.Ordinal)
            .ToList();
    }
}
