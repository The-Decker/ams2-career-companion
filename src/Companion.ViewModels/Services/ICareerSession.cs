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

    /// <summary>The next era pack for sign-and-continue (M6). Null while the season is
    /// incomplete or when no discovered pack has a season year greater than the current one.
    /// v1 rule: the pack with the SMALLEST year strictly greater than the current season year
    /// wins; the years in between are bridged, never blocked. Additive member — sessions
    /// without era-transition support report "no next pack".</summary>
    NextSeasonInfo? NextSeason() => null;

    /// <summary>Signs the ACCEPTED offer into the next era pack: builds the
    /// <c>EraTransition</c> plan from the completed season's persisted end states and starts
    /// the new season via <c>CareerStore.StartNextSeason</c>. <paramref name="teamId"/> must
    /// be the accepted offer's team. Throws <see cref="InvalidOperationException"/> carrying
    /// the plan's validation errors (e.g. the accepted team missing from the new pack) — the
    /// review screen surfaces them. After success THIS session still points at the finished
    /// season: reopen the career file to land in the new season. Additive member.</summary>
    void StartNextSeason(string teamId) => throw new NotSupportedException(
        "This career session does not support era transitions.");

    SeasonPack Pack { get; }
}

/// <summary>The discovered next era pack for the season review's sign-and-continue block.</summary>
public sealed record NextSeasonInfo
{
    public required string PackDirectory { get; init; }

    public required string PackId { get; init; }

    public required string PackName { get; init; }

    public required int SeasonYear { get; init; }

    /// <summary>The years between the finished season and the next pack (ascending, empty
    /// for consecutive years). v1 BRIDGES them: everyone ages through the gap — the review
    /// shows the bridge note, e.g. "1968 has no pack — your career bridges through it".</summary>
    public required IReadOnlyList<int> BridgedYears { get; init; }
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

    /// <summary>True when staging wrote nothing ONLY because the installed class XML is the
    /// user's own community file (no generated marker) and staging over it requires the
    /// explicit "Stage anyway" choice. Success is false, but this is an EXPECTED gate, not a
    /// failure — the briefing shows an informational (amber) banner, never a red one.</summary>
    public bool BlockedByForceGate { get; init; }

    public required IReadOnlyList<string> Messages { get; init; }

    /// <summary>Per-file detail lines behind <see cref="Messages"/>' aggregate summaries
    /// (e.g. the livery scan's unreadable files) — shown collapsed behind an expander,
    /// never as a wall of rows.</summary>
    public IReadOnlyList<string> Details { get; init; } = [];
}

/// <summary>What the result-entry screen produces. Positions are implied by list order.</summary>
public sealed record ResultDraft
{
    /// <summary>Driver ids in finishing order (index 0 = P1).</summary>
    public required IReadOnlyList<string> Classified { get; init; }

    /// <summary>Driver id → one-letter DNF reason ("m" mechanical, "a" accident, "o" other).
    /// The stable machine seam: the letter alone is enough for the fold's blame model and for
    /// every existing consumer. Free-text customisation of "o" (and driver-error attribution)
    /// rides alongside in <see cref="DidNotFinishDetail"/> — this map never carries anything
    /// but m/a/o.</summary>
    public required IReadOnlyDictionary<string, string> DidNotFinish { get; init; }

    public required IReadOnlyList<string> Disqualified { get; init; }

    /// <summary>Optional per-DNF custom detail, additive over <see cref="DidNotFinish"/> — the
    /// keys are a subset of that map's keys. Present for a customised "Other" (e.g.
    /// "Engine fire", "Spun off"); absent drivers keep the plain letter meaning. The
    /// <see cref="DnfDetail.DriverAttributed"/> flag lets a custom "other" opt IN to
    /// driver-error blame (default: no blame, matching bare "o"). Older producers omit this
    /// map entirely; consumers must treat a missing key as "no detail".</summary>
    public IReadOnlyDictionary<string, DnfDetail> DidNotFinishDetail { get; init; } =
        new Dictionary<string, DnfDetail>(StringComparer.Ordinal);

    /// <summary>Optional free-text DSQ reason per disqualified driver (e.g. "Underweight",
    /// "Illegal wing"). Keys are a subset of <see cref="Disqualified"/>; absence means no
    /// stated reason. Additive — older producers omit it.</summary>
    public IReadOnlyDictionary<string, string> DisqualifiedDetail { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The in-game Opponent Skill slider the round was actually driven at (asked on
    /// the result screen, prefilled with the last recommendation, editable 70–120). Stored in
    /// the round's raw-result envelope. Null falls back to the current recommendation.</summary>
    public double? SliderUsed { get; init; }
}

/// <summary>A customised DNF cause carried beside the one-letter code in
/// <see cref="ResultDraft.DidNotFinishDetail"/>: free text plus whether the cause is the
/// driver's fault. <see cref="DriverAttributed"/> only re-colours the sim's blame model for a
/// custom "other" — 'm'/'a' keep their fixed meaning (mechanical = no blame, accident =
/// driver error) whatever this flag says.</summary>
public sealed record DnfDetail
{
    /// <summary>Free-text cause shown in the UI and journalled (e.g. "Engine fire"). May be
    /// empty when only the attribution matters.</summary>
    public string Text { get; init; } = "";

    /// <summary>True when the user marked this custom "other" cause as the driver's fault, so
    /// the OPI DNF-cause rule treats it as driver-error rather than the no-blame default.</summary>
    public bool DriverAttributed { get; init; }
}

public sealed record ConfirmModel
{
    /// <summary>Per-driver points earned this round (round contribution only).</summary>
    public required IReadOnlyList<(string DriverId, Rational Points)> RoundPoints { get; init; }

    /// <summary>Standings movement: driver, previous position (null at round 1), new position.</summary>
    public required IReadOnlyList<(string DriverId, int? From, int? To)> Movements { get; init; }

    public required string Headline { get; init; }
}
