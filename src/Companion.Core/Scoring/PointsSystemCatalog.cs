using System.Text.Json;
using System.Text.Json.Serialization;
using Companion.Core.Json;
using Companion.Core.Numerics;

namespace Companion.Core.Scoring;

/// <summary>Per-round rule tweak keyed by f1db grand-prix id: half-points races, the 2014
/// double-points finale, Indianapolis 500 constructors exclusion, shortened-race tables.</summary>
public sealed record RoundOverride
{
    public required string GrandPrix { get; init; }
    public Rational? PointsFactor { get; init; }
    public bool? CountsForConstructors { get; init; }
    public string? AlternateRaceTableId { get; init; }
}

/// <summary>
/// The season-rules catalog (data/rules/f1-points-systems.json) parsed but not yet bound to a
/// season shape. Best-N rules are stored season-shape-independently ("best 11", "best 5 of the
/// first 6 rounds plus best 4 of the rest") and materialize into <see cref="PointsSystem"/>
/// segments once the season's round count is known.
/// </summary>
public sealed class PointsSystemCatalog
{
    public required IReadOnlyDictionary<int, CatalogSeason> Seasons { get; init; }

    public static PointsSystemCatalog Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<CatalogDto>(json, CoreJson.Options)
                  ?? throw new JsonException("Points-system catalog is empty.");

        var seasons = new Dictionary<int, CatalogSeason>();
        foreach (var (yearText, season) in dto.Seasons)
        {
            if (!int.TryParse(yearText, out int year))
                throw new JsonException($"Season key '{yearText}' is not a year.");
            seasons[year] = season;
        }

        return new PointsSystemCatalog { Seasons = seasons };
    }

    public CatalogSeason GetSeason(int year) =>
        Seasons.TryGetValue(year, out var season)
            ? season
            : throw new KeyNotFoundException($"No points system catalogued for {year}.");

    private sealed record CatalogDto
    {
        public required Dictionary<string, CatalogSeason> Seasons { get; init; }
    }
}

public sealed record CatalogSeason
{
    public required IReadOnlyList<Rational> RacePoints { get; init; }
    public IReadOnlyList<Rational>? SprintPoints { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<Rational>>? AlternateRaceTables { get; init; }
    public FastestLapRule? FastestLap { get; init; }
    public SharedDrivePolicy SharedDrivePolicy { get; init; } = SharedDrivePolicy.Zero;
    public CatalogBestN? DriversBestN { get; init; }
    public CatalogConstructors? Constructors { get; init; }
    public IReadOnlyList<RoundOverride> RoundOverrides { get; init; } = [];
    public IReadOnlyList<string> ExcludedDrivers { get; init; } = [];
    public IReadOnlyList<string> ExcludedConstructors { get; init; } = [];

    /// <summary>Flat end-of-season points corrections that raw results cannot express
    /// (constructors-only penalties, stewards' shared-drive reallocations). Every entry
    /// must document its reason.</summary>
    public IReadOnlyList<CatalogPointsAdjustment> PointsAdjustments { get; init; } = [];

    /// <summary>Mid-season constructor entity changes (2018 Force India administration).
    /// Consumed by the fixture generator / pack tooling, not by the engine.</summary>
    public IReadOnlyList<CatalogEntitySplit> ConstructorEntitySplits { get; init; } = [];

    public PointsSystem ResolvePointsSystem(int roundCount)
    {
        var driversBestN = DriversBestN?.Resolve(roundCount);

        return new PointsSystem
        {
            RacePoints = RacePoints,
            SprintPoints = SprintPoints,
            AlternateRaceTables = AlternateRaceTables,
            FastestLap = FastestLap,
            SharedDrivePolicy = SharedDrivePolicy,
            DriversBestN = driversBestN,
            Constructors = Constructors is null
                ? null
                : new ConstructorsRule
                {
                    BestCarOnly = Constructors.BestCarOnly,
                    RacePoints = Constructors.RacePoints,
                    BestN = Constructors.BestN switch
                    {
                        null => null,
                        "sameAsDrivers" => driversBestN,
                        _ => throw new JsonException(
                            $"Unsupported constructors bestN value '{Constructors.BestN}'."),
                    },
                },
        };
    }

    public SeasonScoringDefinition ResolveScoringDefinition(int roundCount) => new()
    {
        PointsSystem = ResolvePointsSystem(roundCount),
        RoundCount = roundCount,
        ExcludedDrivers = ExcludedDrivers.ToHashSet(StringComparer.Ordinal),
        ExcludedConstructors = ExcludedConstructors.ToHashSet(StringComparer.Ordinal),
        DriverPointsAdjustments = PointsAdjustments
            .Where(a => a.Driver is not null)
            .ToDictionary(a => a.Driver!, a => a.Delta, StringComparer.Ordinal),
        ConstructorPointsAdjustments = PointsAdjustments
            .Where(a => a.Constructor is not null)
            .ToDictionary(a => a.Constructor!, a => a.Delta, StringComparer.Ordinal),
    };
}

/// <summary>One documented points correction. Exactly one of Driver/Constructor is set.</summary>
public sealed record CatalogPointsAdjustment
{
    public string? Driver { get; init; }
    public string? Constructor { get; init; }
    public required Rational Delta { get; init; }
    public required string Reason { get; init; }
}

/// <summary>A constructor championship entity that changed identity mid-season.</summary>
public sealed record CatalogEntitySplit
{
    public required string Constructor { get; init; }
    public required string Engine { get; init; }

    /// <summary>First round (1-based) scored under the new entity.</summary>
    public required int FromRound { get; init; }

    /// <summary>Composite id ("constructor+engine" form) of the successor entity.</summary>
    public required string NewId { get; init; }
}

public sealed record CatalogBestN
{
    /// <summary>"Best N results of the whole season" form.</summary>
    public int? WholeSeason { get; init; }

    /// <summary>The 1967–1980 split-season form.</summary>
    public CatalogSplitSeason? Split { get; init; }

    public BestNRule Resolve(int roundCount)
    {
        if (WholeSeason is { } wholeCount)
            return new BestNRule
            {
                Segments = [new BestNSegment { FromRound = 1, ToRound = roundCount, Count = wholeCount }],
            };

        if (Split is { } split)
        {
            if (split.FirstRounds + split.SecondRounds != roundCount)
                throw new InvalidOperationException(
                    $"Split-season rule covers {split.FirstRounds}+{split.SecondRounds} rounds " +
                    $"but the season has {roundCount}.");
            return new BestNRule
            {
                Segments =
                [
                    new BestNSegment { FromRound = 1, ToRound = split.FirstRounds, Count = split.FirstCount },
                    new BestNSegment
                    {
                        FromRound = split.FirstRounds + 1,
                        ToRound = roundCount,
                        Count = split.SecondCount,
                    },
                ],
            };
        }

        throw new JsonException("A bestN rule needs either 'wholeSeason' or 'split'.");
    }
}

public sealed record CatalogSplitSeason
{
    public required int FirstRounds { get; init; }
    public required int FirstCount { get; init; }
    public required int SecondRounds { get; init; }
    public required int SecondCount { get; init; }
}

public sealed record CatalogConstructors
{
    public bool BestCarOnly { get; init; }

    /// <summary>Null (no drops) or the string "sameAsDrivers".</summary>
    public string? BestN { get; init; }

    /// <summary>Constructors-specific race points table when it differed from the drivers
    /// table (1961). Null means the drivers table applies.</summary>
    public IReadOnlyList<Rational>? RacePoints { get; init; }
}
