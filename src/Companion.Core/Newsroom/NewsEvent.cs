namespace Companion.Core.Newsroom;

/// <summary>
/// Every trigger the newsroom can cover. Detected by <see cref="CareerNewsEvents"/> as a pure
/// projection over already-folded facts — an event NEVER feeds a fold input, so detection rules
/// may evolve freely without touching replay. Names are data-format identifiers (they key
/// dedupe keys and corpus template families): additive only, never rename.
/// </summary>
public enum NewsEventKind
{
    // Career / season boundaries
    CareerCreated,
    SeasonStarted,
    SeasonCompleted,
    ChampionCrowned,
    TitleClinchedEarly,
    TitleRaceLost,
    FinalRoundShowdown,

    // Player race results (per round; combination facts refine coverage)
    RaceWon,
    PodiumFinish,
    PointsFinish,
    MidfieldResult,
    Overperformed,
    Underperformed,
    RetiredMechanical,
    RetiredDriverError,
    SatOutRound,

    // Player firsts (career-wide, once each)
    FirstStart,
    FirstPoints,
    FirstTop5,
    FirstPodium,
    FirstWin,
    FirstPole,
    FirstRetirement,

    // Qualifying
    PolePosition,
    QualifyingSurprise,

    // Streaks and droughts
    WinStreak,
    PodiumStreak,
    PointsStreak,
    RetirementStreak,
    WinDroughtEnded,
    PointsDroughtEnded,

    // Championship movement
    ChampionshipLeadTaken,
    ChampionshipLeadLost,
    LeadChangedHands,
    TitleFightTightens,
    StandingsClimb,

    // Records and milestones
    CareerMilestone,
    BestFinishImproved,

    // The AI world
    AiWinStreak,
    UpsetWinner,
    DominantDisplay,

    // Teams
    TeamPromoted,
    TeamRelegated,

    // Driver market / careers
    OfferReceived,
    PlayerTeamChanged,
    SeatVacancy,
    SeatFilled,
    RetirementConsidered,
    DriverRetired,

    // Mortality / availability
    PlayerInjured,
    SeasonEndingInjury,
    PlayerDied,

    // Divergence (emitted by the divergence engine, slice 5)
    HistoryDiverged,
    HistoryHeld,

    // SMGP flavour (mode-gated; the SMGP beat detectors remain the source of richer beats)
    RivalryDeveloped,
    DnqDrama,

    // Character progression (level milestones from the folded XP facts; display-only detection)
    LevelMilestone,
    Level300Reached,

    // Medical comeback: the first start after injury sit-out rounds
    ReturnedFromInjury,

    // Campaign completion: the final pinned season closed — the career retrospective moment
    CareerCompleted,
}

/// <summary>
/// The combination facts that turn a generic trigger into the RIGHT story: a win reads
/// differently when it is a first, ends a drought, comes in the wet, seals the title, or lands
/// on the season finale. Every field is optional-by-default so detectors only assert what the
/// stored data actually proves; the composer degrades missing facts gracefully.
/// </summary>
public sealed record NewsEventFacts
{
    public bool IsFirstEver { get; init; }
    public int StreakLength { get; init; }
    public int DroughtLength { get; init; }
    public bool IsWet { get; init; }
    public bool IsFinalRound { get; init; }
    public bool IsSeasonOpener { get; init; }
    public bool TookChampionshipLead { get; init; }
    public bool LostChampionshipLead { get; init; }
    public bool ClinchedTitle { get; init; }
    /// <summary>expected - actual finish; positive = beat expectation by that many places.</summary>
    public int UpsetMagnitude { get; init; }
    public int? PlayerFinish { get; init; }
    public int? ExpectedFinish { get; init; }
    public int? QualifyingPosition { get; init; }
    public int? ChampionshipPosition { get; init; }
    public int? ChampionshipDelta { get; init; }
    public double? PointsGapToLeader { get; init; }
    public bool RivalInvolved { get; init; }
    public string RivalName { get; init; } = "";
    public int MilestoneValue { get; init; }
    /// <summary>"wins" | "podiums" | "starts" | "points" — which counter hit the milestone.</summary>
    public string MilestoneCounter { get; init; } = "";
    public string WinnerName { get; init; } = "";
    public string WinnerTeamName { get; init; } = "";
    /// <summary>Low-prestige upset context for AI stories (team tier at the time, 1..5).</summary>
    public int WinnerTeamTier { get; init; }
    public string RetirementReason { get; init; } = "";
    public int MissRaces { get; init; }
}

/// <summary>
/// One detected, coverable happening. <see cref="DedupeKey"/> is the stable identity for the
/// whole pipeline: template selection seeds off it, generated stories carry it as their key,
/// reading state references it, and the selector drops same-key duplicates. Shape:
/// <c>{kind}:{season}:{round}:{subject}</c> (season = career-relative ordinal so era
/// transitions cannot collide; round 0 = season-level).
/// </summary>
public sealed record NewsEvent
{
    public required NewsEventKind Kind { get; init; }
    /// <summary>1-based career season ordinal (stable across era transitions).</summary>
    public required int SeasonOrdinal { get; init; }
    public required int SeasonYear { get; init; }
    /// <summary>Calendar round the event landed on; 0 for season-level events.</summary>
    public required int Round { get; init; }
    /// <summary>Primary subject id: "player", a driver id, or a team id.</summary>
    public required string SubjectId { get; init; }
    public string SubjectName { get; init; } = "";
    public string SubjectTeamId { get; init; } = "";
    public string SubjectTeamName { get; init; } = "";
    public string VenueName { get; init; } = "";
    public NewsEventFacts Facts { get; init; } = new();
    /// <summary>Disambiguates same-kind same-round events on one subject (e.g. two career
    /// milestones landing together: "starts" and "wins"). Empty for the common case.</summary>
    public string Discriminator { get; init; } = "";

    public string DedupeKey =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{Kind.ToString().ToLowerInvariant()}:{SeasonOrdinal}:{Round}:{SubjectId}{(Discriminator.Length > 0 ? ":" + Discriminator : "")}");
}
