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

    /// <summary>Recommended Opponent Skill slider (70–120) for the CURRENT round, from the
    /// last folded round's pace anchor. Null before the anchor calibrates (fresh careers).
    /// Shown in the briefing and prefilled on the result screen — never auto-applied.</summary>
    int? CurrentSliderRecommendation();

    /// <summary>The season review + offers screen data; null until the season is complete.
    /// The first call after the final round runs the season-end pipeline (offers scored from
    /// the final round's FOLDED player state) if it has not run yet.</summary>
    SeasonReviewModel? SeasonReview();

    /// <summary>Accepts one offer letter (at most one acceptance per season — a new choice
    /// clears the previous one) and journals the choice. Throws when the team made no offer.</summary>
    void AcceptOffer(string teamId);

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

    /// <summary>Reputation after the last FOLDED round (0–100); null before round 1.</summary>
    public double? Reputation { get; init; }

    /// <summary>Overperformance index after the last folded round; null before round 1.</summary>
    public double? Opi { get; init; }

    /// <summary>Reputation movement of the last folded round (vs the round before it, or the
    /// season-start state after round 1); null when no trend exists yet.</summary>
    public double? ReputationDelta { get; init; }

    /// <summary>OPI movement of the last folded round; null when no trend exists yet.</summary>
    public double? OpiDelta { get; init; }
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

    /// <summary>The difficulty recommendation for this round (70–120 Opponent Skill percent),
    /// from the folded pace anchor. Null before the anchor calibrates.</summary>
    public int? RecommendedSlider { get; init; }
}

public sealed record CopyableSetting(string Label, string Value);

public sealed record StageOutcome
{
    public required bool Success { get; init; }
    public string? WrittenPath { get; init; }
    public string? BackupPath { get; init; }

    /// <summary>True when staging wrote NOTHING because the installed class XML already
    /// matches the round's generated grid (NAMeS-first diff-aware staging). Success is also
    /// true; <see cref="WrittenPath"/> points at the installed file satisfying the round.</summary>
    public bool NoOpAlreadyMatches { get; init; }

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

    /// <summary>The in-game Opponent Skill slider the round was actually driven at (asked on
    /// the result screen, prefilled with the last recommendation, editable 70–120). Stored in
    /// the round's raw-result envelope. Null falls back to the current recommendation.</summary>
    public double? SliderUsed { get; init; }
}

public sealed record ConfirmModel
{
    /// <summary>Per-driver points earned this round (round contribution only).</summary>
    public required IReadOnlyList<(string DriverId, Rational Points)> RoundPoints { get; init; }

    /// <summary>Standings movement: driver, previous position (null at round 1), new position.</summary>
    public required IReadOnlyList<(string DriverId, int? From, int? To)> Movements { get; init; }

    public required string Headline { get; init; }
}
