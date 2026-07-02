using Companion.Core.Scoring;

namespace Companion.Core.Packs;

/// <summary>season.json — calendar + points system + season-level rules.</summary>
public sealed record SeasonDefinition
{
    public required int Year { get; init; }

    public required string SeriesName { get; init; }

    /// <summary>EXACT xmlName from data/ams2/classes.json. Existence/casing is checked by the
    /// Ams2 content preflight, not by the structural validator (Core has no content library).</summary>
    public required string Ams2Class { get; init; }

    /// <summary>Exactly the shape of a data/rules/f1-points-systems.json season entry
    /// (racePoints, fastestLap, sharedDrivePolicy, driversBestN, constructors,
    /// roundOverrides, pointsAdjustments, excluded lists ...).</summary>
    public required CatalogSeason PointsSystem { get; init; }

    public required IReadOnlyList<PackRound> Rounds { get; init; }
}

public sealed record PackRound
{
    /// <summary>1-based calendar position; round numbers must be contiguous from 1.</summary>
    public required int Round { get; init; }

    public required string Name { get; init; }

    /// <summary>Historical event date, "yyyy-MM-dd". Kept as text so the validator can report a
    /// bad date as an issue instead of failing the whole parse.</summary>
    public required string Date { get; init; }

    /// <summary>False marks a non-championship event on the calendar.</summary>
    public bool Championship { get; init; } = true;

    public required PackTrackRef Track { get; init; }

    /// <summary>100% historical race distance — the whole app assumes full-length races.</summary>
    public required int Laps { get; init; }

    /// <summary>Rendered on the Race Day briefing screen. The contract requires one on every
    /// round; the structural validator errors on championship rounds without one.</summary>
    public PackSetupGuide? SetupGuide { get; init; }

    /// <summary>Indy-500-style per-round entrants, in addition to entries.json coverage.</summary>
    public IReadOnlyList<PackGuestEntry> GuestEntries { get; init; } = [];

    /// <summary>Per-round rating tweaks: driver id -> partial ratings patch.</summary>
    public IReadOnlyDictionary<string, PackRatingsPatch> AiOverrides { get; init; } =
        new Dictionary<string, PackRatingsPatch>();
}

public sealed record PackTrackRef
{
    /// <summary>Internal id from data/ams2/tracks.json — the AMS2 track actually driven,
    /// which for placeholder rounds is NOT the historical venue.</summary>
    public required string Id { get; init; }

    /// <summary>The historical venue's name, shown in the briefing, standings, and records
    /// regardless of what track is driven (v1.1, locked decision #6).</summary>
    public string? RealVenue { get; init; }

    /// <summary>True when the historical venue does not exist in AMS2 and <see cref="Id"/>
    /// is an explicit placeholder. Placeholder rounds recompute laps so the REAL race
    /// distance in km is preserved at the placeholder's lap length.</summary>
    public bool IsPlaceholder { get; init; }

    /// <summary>Alternates when <see cref="Id"/> itself is missing locally (unowned DLC) —
    /// a different problem than placeholder substitution.</summary>
    public IReadOnlyList<string> Fallbacks { get; init; } = [];
}

/// <summary>Exact in-game custom-race session settings plus optional author notes.</summary>
public sealed record PackSetupGuide
{
    public required PackSessionSettings Session { get; init; }

    /// <summary>Author free text (car-setup hints), optional.</summary>
    public string? Notes { get; init; }
}

public sealed record PackSessionSettings
{
    /// <summary>Grid minus the player; the Ams2 preflight checks opponents + 1 against the
    /// venue's AI cap.</summary>
    public required int Opponents { get; init; }

    /// <summary>In-game session start time, "HH:mm".</summary>
    public string? StartTime { get; init; }

    /// <summary>In-game date (weather/sun position), "yyyy-MM-dd".</summary>
    public string? Date { get; init; }

    /// <summary>Up to 4 AMS2 weather slot names (e.g. "Clear").</summary>
    public IReadOnlyList<string> WeatherSlots { get; init; } = [];

    /// <summary>In-game time progression setting (e.g. "1x").</summary>
    public string? TimeProgression { get; init; }

    public bool MandatoryPitStop { get; init; }
}

/// <summary>A per-round entrant. Team and driver must still exist in teams.json/drivers.json.</summary>
public sealed record PackGuestEntry
{
    public required string TeamId { get; init; }

    public required string DriverId { get; init; }

    public string? Number { get; init; }

    /// <summary>EXACT livery display name (case-sensitive) — the load-bearing binding.</summary>
    public required string Ams2LiveryName { get; init; }
}
