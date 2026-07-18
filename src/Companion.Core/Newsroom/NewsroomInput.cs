namespace Companion.Core.Newsroom;

/// <summary>
/// The shaped, mode-agnostic input <see cref="CareerNewsEvents"/> detects over, the same
/// shape-in-the-session, detect-in-Core split as <c>SmgpNarrativeSeason</c>. The session builds
/// these from stored envelopes, standings snapshots, journal rows, and folded states; Core never
/// touches I/O. Every optional field means "assert only what the stored data proves", a null
/// simply suppresses the triggers that need it (older careers stay quieter, never wrong).
/// </summary>
public sealed record NewsroomSeason
{
    /// <summary>1-based career season ordinal (stable across era transitions).</summary>
    public required int Ordinal { get; init; }
    public required int Year { get; init; }
    /// <summary>Championship rounds in the calendar (for finale/remaining math).</summary>
    public required int ChampionshipRoundCount { get; init; }
    public bool Complete { get; init; }
    public string ChampionId { get; init; } = "";
    public string ChampionName { get; init; } = "";
    public bool PlayerChampion { get; init; }
    /// <summary>Player's team at season start (id/name), for team-change detection.</summary>
    public string PlayerTeamId { get; init; } = "";
    public string PlayerTeamName { get; init; } = "";
    public required IReadOnlyList<NewsroomRound> Rounds { get; init; }
    /// <summary>Season-end happenings shaped from journal rows (round = null in the journal).</summary>
    public IReadOnlyList<NewsroomSeasonNote> SeasonNotes { get; init; } = [];

    /// <summary>The player's character level after the season-end awards folded, or null when the
    /// career has no character (level milestones crossed at the season boundary detect off this).</summary>
    public int? PlayerLevelAtSeasonEnd { get; init; }

    /// <summary>True when completing this season completes the whole campaign (SMGP season 17 of
    /// 17, or the final pinned season of a bounded historical campaign), the career-retrospective
    /// trigger.</summary>
    public bool IsCampaignFinale { get; init; }

    // Dynasty owner economy (from the economy.season journal row; all empty/default for every
    // non-economy career, so older careers stay quieter, never wrong)

    /// <summary>The season settlement's total money in, pre-formatted for display ("" = none).</summary>
    public string EconomySeasonAmount { get; init; } = "";

    /// <summary>The season settlement was a WINDFALL (a front-running constructors' cheque
    /// and/or title bonuses, the session decides against the rules tables).</summary>
    public bool EconomyWindfall { get; init; }
}

/// <summary>One applied round's facts. Only championship rounds drive standings triggers.</summary>
public sealed record NewsroomRound
{
    public required int Round { get; init; }
    public bool Championship { get; init; } = true;
    public string Venue { get; init; } = "";
    public bool IsFinalChampionshipRound { get; init; }

    // Player result facts (from the race.result journal row + stored envelope)
    public int? PlayerFinish { get; init; }
    public int? ExpectedFinish { get; init; }
    /// <summary>"" | "mechanical" | "driverError", the player's own DNF cause.</summary>
    public string PlayerDnfCause { get; init; } = "";
    public bool PlayerDidNotStart { get; init; }
    public bool PlayerScoredPoints { get; init; }
    public bool? IsWet { get; init; }

    // Qualifying facts (from envelope.QualifyingOrder when the weekend ran qualifying)
    public int? PlayerQualifyingPosition { get; init; }
    public string PoleDriverId { get; init; } = "";

    // Winner facts (from the stored classification)
    public string WinnerId { get; init; } = "";
    public string WinnerName { get; init; } = "";
    public string WinnerTeamId { get; init; } = "";
    public string WinnerTeamName { get; init; } = "";
    /// <summary>Winner team tier at the time (5 = front-runner … 1 = backmarker; 0 = unknown).</summary>
    public int WinnerTeamTier { get; init; }

    // Championship picture AFTER this round (championship rounds only)
    public string LeaderId { get; init; } = "";
    public string LeaderName { get; init; } = "";
    public double LeaderPoints { get; init; }
    public double SecondPoints { get; init; }
    public int? PlayerPosition { get; init; }
    public double PlayerPoints { get; init; }
    /// <summary>Upper bound on points any driver can still add after this round (drops ignored —
    /// counted points never decrease, so a clinch under this bound is mathematically safe).</summary>
    public double MaxRemainingPoints { get; init; }
    /// <summary>Maximum points obtainable from a single remaining round (era-aware).</summary>
    public double MaxPointsPerRound { get; init; }

    // Mortality (from player.accident / player.dns journal rows)
    /// <summary>"" | "minorInjury" | "seasonEnding" | "death".</summary>
    public string AccidentOutcome { get; init; } = "";
    public int AccidentMissRaces { get; init; }

    // Character progression (from the player.xp journal row)
    /// <summary>The player's character level AFTER this round's XP folded; null when the career
    /// has no character or the row predates level journaling.</summary>
    public int? PlayerLevelAfter { get; init; }

    // SMGP colour (empty for historical careers)
    public string RivalName { get; init; } = "";

    // Dynasty owner economy (from the economy.applied / economy.round / economy.bankruptcy
    // journal rows; all empty/default for every non-economy career)

    /// <summary>Sponsors SIGNED at this round (display names), in journal seq order.</summary>
    public IReadOnlyList<string> EconomySponsorsSigned { get; init; } = [];

    /// <summary>The round's repair bill, pre-formatted for display ("" = no repairs).</summary>
    public string EconomyRepairAmount { get; init; } = "";

    /// <summary>The repair bill was MAJOR (the session compares against the era-scaled heavy-crash
    /// rate; false when the rules are unavailable).</summary>
    public bool EconomyMajorRepair { get; init; }

    /// <summary>The settlement left the team ON THE BRINK: the deficit streak has consumed the
    /// whole grace window, one more deficit round folds the team.</summary>
    public bool EconomyOnTheBrink { get; init; }

    /// <summary>The team's balance after this round, pre-formatted ("" = no economy).</summary>
    public string EconomyBalance { get; init; } = "";

    /// <summary>This round's settlement BANKRUPTED the team, terminal.</summary>
    public bool EconomyBankrupt { get; init; }

    /// <summary>Car development level after this round's decisions, or null (no economy).</summary>
    public int? EconomyDevelopmentLevel { get; init; }

    /// <summary>The development programme is at its cap after this round (the rules' max level).</summary>
    public bool EconomyDevelopmentMaxed { get; init; }
}

/// <summary>
/// A season-boundary happening shaped from the journal: team tier moves, retirements and
/// foreshadows, seat-market rows, offers. Kind vocabulary is fixed here (not free strings).
/// </summary>
public sealed record NewsroomSeasonNote
{
    public required NewsroomSeasonNoteKind Kind { get; init; }
    public string SubjectId { get; init; } = "";
    public string SubjectName { get; init; } = "";
    public string Detail { get; init; } = "";
    /// <summary>Tier for team moves; offer tier for offers; age for retirements; 0 otherwise.</summary>
    public int Value { get; init; }
}

public enum NewsroomSeasonNoteKind
{
    TeamPromoted,
    TeamRelegated,
    DriverRetired,
    RetirementConsidered,
    SeatVacancy,
    SeatFilled,
    OfferReceived,
}
