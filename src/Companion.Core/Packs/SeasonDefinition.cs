using System.Text.Json.Serialization;
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

    /// <summary>Optional per-race form overlay: round number -> (driver id -> additive pace nudge).
    /// Always applied when the app writes the AMS2 custom-AI file for that round
    /// (<see cref="Companion.Core.Grid"/> staging), so a driver who was hot that weekend is faster on
    /// track and a slumping one slower — grounded in f1db per-race qualifying vs the season baseline.
    /// For a <see cref="Companion.Core.Career.PlayerCareerState.FormAware"/> career (Ratings Phase 3)
    /// the FOLD also overlays it onto the AI seats, so the player's expected finish / OPI / pace anchor
    /// react to who is hot — the SAME additive nudge, so scored == staged. A pre-Phase-3 career never
    /// reads it in the fold, so it stays byte-identical. The scoring engine and f1db oracle never read
    /// it. Absent => no form. Guarded with WhenWritingNull so a form-less pack round-trips
    /// byte-identically.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<int, IReadOnlyDictionary<string, PackDriverForm>>? DriverForm { get; init; }

    /// <summary>Whether the season's rules allow in-race refuelling (AMS2's
    /// <c>PitStopsAllowRefuelling</c>). <c>false</c> = disallowed (1967 cars ran the distance on one
    /// tank; refuelling only arrived ~1982); <c>true</c> = allowed; <c>null</c> = not shown/unknown.
    /// Season-wide. SIM-INERT: shown on the Race-Day briefing only — the career fold and the f1db
    /// oracle never read it, so a pack that sets it folds byte-identically. Guarded with
    /// WhenWritingNull so a pack that omits it round-trips unchanged.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RefuellingAllowed { get; init; }
}

/// <summary>A single driver's per-round form nudge: an ADDITIVE delta on the two pace ratings,
/// clamped into 0..1 when applied. Always written into the staged custom-AI file; the FOLD also reads
/// it for a FormAware career (Ratings Phase 3), never for a pre-Phase-3 one (see
/// <see cref="SeasonDefinition.DriverForm"/>). Absent components default to 0 (no nudge).</summary>
public sealed record PackDriverForm
{
    public double RaceSkill { get; init; }
    public double QualifyingSkill { get; init; }
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

    /// <summary>Optional historical grid for this round (the drivers who ACTUALLY started, mapped
    /// from f1db to pack driver ids, plus the resolved grid size). When present, the resolver
    /// seats only the covering entries whose driver id is in <see cref="PackRoundGrid.StarterDriverIds"/>
    /// (plus guests and the player). When absent, every covering entry fills the grid — the pre-grid
    /// behaviour — so a partial regen that omits it on some rounds cannot regress.</summary>
    public PackRoundGrid? Grid { get; init; }

    /// <summary>Rendered on the Race Day briefing screen. The contract requires one on every
    /// round; the structural validator errors on championship rounds without one.</summary>
    public PackSetupGuide? SetupGuide { get; init; }

    /// <summary>Indy-500-style per-round entrants, in addition to entries.json coverage.</summary>
    public IReadOnlyList<PackGuestEntry> GuestEntries { get; init; } = [];

    /// <summary>Per-round rating tweaks: driver id -> partial ratings patch.</summary>
    public IReadOnlyDictionary<string, PackRatingsPatch> AiOverrides { get; init; } =
        new Dictionary<string, PackRatingsPatch>();

    /// <summary>Optional race-weekend structure (Increment 2): the sessions this round runs —
    /// practice, qualifying, and 1 or 2 races, each with an era-correct label and points table.
    /// ABSENT = today's single race (every bundled pack), so scoring and the fold are unchanged
    /// until a pack opts in. This is the parsed data model; the engine + result-entry wiring land
    /// in later Increment-2 slices.</summary>
    public PackWeekend? Weekend { get; init; }
}

/// <summary>A round's weekend shape: an optional practice + qualifying session and 1–2 races.
/// Additive/optional on <see cref="PackRound"/>; a round without it runs the single-race loop.</summary>
public sealed record PackWeekend
{
    /// <summary>Practice session (optional, informational — no result captured).</summary>
    public PackWeekendSession? Practice { get; init; }

    /// <summary>Qualifying session (optional). When present, its order sets the race grid and
    /// (later slice) calibrates the one-lap pace anchor.</summary>
    public PackWeekendSession? Qualifying { get; init; }

    /// <summary>The scoring races — 1 (a plain era-named Grand Prix) or 2 (e.g. sprint + feature).</summary>
    public required IReadOnlyList<PackWeekendRace> Races { get; init; }
}

/// <summary>A non-scoring weekend session (practice/qualifying): whether it runs, its label, and
/// (additive, SIM-INERT display-only) its AMS2 timed length + per-session weather slots.</summary>
public sealed record PackWeekendSession
{
    public bool Present { get; init; } = true;

    /// <summary>Era-correct display label ("Practice", "Qualifying", "Time Trial"). Null = the
    /// session's default name.</summary>
    public string? Label { get; init; }

    /// <summary>The session's timed length in minutes (AMS2 practice + qualifying are ALWAYS
    /// time-limited, never lap-based). Null = not authored / not shown. Briefing display only —
    /// never a fold input. Guarded with WhenWritingNull so an un-migrated pack round-trips unchanged.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DurationMinutes { get; init; }

    /// <summary>This session's AMS2 weather, up to 4 independent slots (game display labels:
    /// "Clear", "Light Cloud", "Rain", …). Null = not authored — the briefing then falls back to the
    /// round-level <see cref="PackSessionSettings.WeatherSlots"/>. Briefing display only — never a
    /// fold input. Guarded with WhenWritingNull so an un-migrated pack round-trips unchanged.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? WeatherSlots { get; init; }
}

/// <summary>One scoring race in a weekend.</summary>
public sealed record PackWeekendRace
{
    /// <summary>Stable session id ("race", "race2") — the seam <c>SessionId</c> key and journal key.</summary>
    public required string Id { get; init; }

    /// <summary>Era-correct display label — a single-race weekend uses whatever that era called the
    /// race ("Grand Prix", "Feature"); a two-race weekend labels each ("Sprint" / "Grand Prix").</summary>
    public required string Label { get; init; }

    /// <summary>Which points table scores this race: <c>null</c>/"primary" = the season RacePoints,
    /// "sprint" = SprintPoints, or a named <c>alternateRaceTables</c> key. The per-session table
    /// selector is wired into the engine in slice 2c.</summary>
    public string? PointsTable { get; init; }

    /// <summary>How this race's grid is formed: <c>null</c>/"qualifying" = the qualifying order,
    /// "race1Reverse" = reversed race-1 finish, etc. Consumed by later slices; parsed now.</summary>
    public string? GridFrom { get; init; }

    /// <summary>The race session's AMS2 weather, up to 4 independent slots (game display labels).
    /// Null = not authored — the briefing then falls back to the round-level
    /// <see cref="PackSessionSettings.WeatherSlots"/>. Briefing display only — never a fold input.
    /// Guarded with WhenWritingNull so an un-migrated pack round-trips unchanged.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? WeatherSlots { get; init; }
}

/// <summary>The drivers who actually started one round historically, mapped from f1db to this
/// pack's driver ids, plus the resolved grid size. Additive/optional on <see cref="PackRound"/>:
/// a round without it keeps the pre-grid behaviour (every covering entry fills the grid).</summary>
public sealed record PackRoundGrid
{
    /// <summary>Number of cars on the grid for this round = min(historical starter count, the
    /// track's Max AI participants). The resolver never seats more than this; the setup guide's
    /// opponents is <c>size - 1</c> (the player replaces one historical driver).</summary>
    public required int Size { get; init; }

    /// <summary>Pack driver ids (drivers.json ids) of the historical starters. A driver id here
    /// that no entry covers this round is simply ignored — the intersection with covering entries
    /// is what seats the grid, so the block never fabricates a seat.</summary>
    public IReadOnlyList<string> StarterDriverIds { get; init; } = [];
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
