namespace Companion.Core.Newsroom;

/// <summary>
/// The shaped, mode-agnostic input <see cref="CareerNewsEvents"/> detects over — the same
/// shape-in-the-session, detect-in-Core split as <c>SmgpNarrativeSeason</c>. The session builds
/// these from stored envelopes, standings snapshots, journal rows, and folded states; Core never
/// touches I/O. Every optional field means "assert only what the stored data proves" — a null
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
    /// <summary>"" | "mechanical" | "driverError" — the player's own DNF cause.</summary>
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

    // SMGP colour (empty for historical careers)
    public string RivalName { get; init; } = "";
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
