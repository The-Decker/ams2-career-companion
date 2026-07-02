using Companion.Core.Grid;
using Companion.Core.Numerics;
using Companion.Core.Packs;
using Companion.Core.Scoring;

namespace Companion.ViewModels.Services;

/// <summary>
/// The app's only gateway to career state (docs/dev/app-shell.md "Services seam").
/// v1 backs this with CareerDatabase + StandingsEngine + packs + Grid; the M5 career sim
/// (OPI/reputation updates, season-end offers, real headlines) extends the implementation —
/// additively, without redesigning this interface.
/// </summary>
public interface ICareerSession
{
    CareerSummary Summary { get; }

    /// <summary>Briefing data for the current round (null when the season is complete).</summary>
    BriefingModel? CurrentBriefing();

    /// <summary>Stage the current round's generated grid into the AMS2 install
    /// (backup-first). Returns the staging outcome for the briefing banner.</summary>
    StageOutcome StageCurrentGrid();

    /// <summary>The current round's seats, in grid order, for the result-entry screen.</summary>
    IReadOnlyList<GridSeat> CurrentGrid();

    /// <summary>Score a draft without committing — feeds the confirm screen.</summary>
    ConfirmModel Preview(ResultDraft draft);

    /// <summary>Persist the result (raw payload + journal), advance to the next round.</summary>
    void Apply(ResultDraft draft);

    /// <summary>Standings after the most recently applied round (null before round 1).</summary>
    StandingsSnapshot? CurrentStandings();

    /// <summary>Every per-round snapshot so far, for the round matrix.</summary>
    IReadOnlyList<StandingsSnapshot> AllSnapshots();

    SeasonPack Pack { get; }
}

public sealed record CareerSummary
{
    public required string CareerName { get; init; }
    public required int SeasonYear { get; init; }
    public required string SeriesName { get; init; }
    public required int CurrentRound { get; init; }
    public required int RoundCount { get; init; }
    public required string PlayerDriverId { get; init; }
    public required string PlayerLiveryName { get; init; }
    /// <summary>Championship position after the last applied round; null before round 1.</summary>
    public int? PlayerPosition { get; init; }
    public bool SeasonComplete { get; init; }
}

public sealed record BriefingModel
{
    public required PackRound Round { get; init; }
    /// <summary>Real venue name; equals the track name unless the round is a placeholder.</summary>
    public required string VenueDisplayName { get; init; }
    public required bool IsPlaceholder { get; init; }
    /// <summary>Ordered label/value pairs, each rendered with a copy button — the exact
    /// in-game strings (track, class, laps, date, time, weather, opponents).</summary>
    public required IReadOnlyList<CopyableSetting> Settings { get; init; }
    public string? SetupNotes { get; init; }
    /// <summary>Set after staging; the file watcher monitors this path.</summary>
    public string? StagedFilePath { get; init; }
}

public sealed record CopyableSetting(string Label, string Value);

public sealed record StageOutcome
{
    public required bool Success { get; init; }
    public string? WrittenPath { get; init; }
    public string? BackupPath { get; init; }
    public required IReadOnlyList<string> Messages { get; init; }
}

/// <summary>What the result-entry screen produces. Positions are implied by list order.</summary>
public sealed record ResultDraft
{
    /// <summary>Driver ids in finishing order (index 0 = P1).</summary>
    public required IReadOnlyList<string> Classified { get; init; }

    /// <summary>Driver id → one-letter DNF reason ("m" mechanical, "a" accident, "o" other).</summary>
    public required IReadOnlyDictionary<string, string> DidNotFinish { get; init; }

    public required IReadOnlyList<string> Disqualified { get; init; }
}

public sealed record ConfirmModel
{
    /// <summary>Per-driver points earned this round (round contribution only).</summary>
    public required IReadOnlyList<(string DriverId, Rational Points)> RoundPoints { get; init; }

    /// <summary>Standings movement: driver, previous position (null at round 1), new position.</summary>
    public required IReadOnlyList<(string DriverId, int? From, int? To)> Movements { get; init; }

    public required string Headline { get; init; }
}
