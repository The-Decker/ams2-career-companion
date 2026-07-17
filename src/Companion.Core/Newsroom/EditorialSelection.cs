namespace Companion.Core.Newsroom;

/// <summary>
/// The importance model: a documented additive score with NO randomness — the same event facts
/// always produce the same score, and the score alone decides layout tier. Tunable by table,
/// testable by table.
/// </summary>
public static class EditorialImportance
{
    public const int LeadThreshold = 70;
    public const int FeaturedThreshold = 50;
    public const int StandardThreshold = 30;
    public const int BriefThreshold = 15;

    public static int Score(NewsEvent e)
    {
        var score = BaseWeight(e.Kind);
        var f = e.Facts;

        if (f.IsFirstEver) score += 25;
        if (f.ClinchedTitle) score += 40;
        if (f.TookChampionshipLead) score += 15;
        if (f.LostChampionshipLead) score += 12;
        if (f.IsFinalRound) score += 10;
        if (f.IsWet) score += 5;
        if (f.RivalInvolved) score += 8;
        score += Math.Min(15, Math.Max(0, f.StreakLength - 1) * 4);
        score += Math.Min(15, f.DroughtLength);
        score += Math.Min(12, Math.Abs(f.UpsetMagnitude) * 2);
        if (f.MilestoneValue >= 50) score += 10;
        else if (f.MilestoneValue >= 20) score += 6;

        return score;
    }

    public static EditorialTier Tier(int score) => score switch
    {
        >= LeadThreshold => EditorialTier.Lead,
        >= FeaturedThreshold => EditorialTier.Featured,
        >= StandardThreshold => EditorialTier.Standard,
        >= BriefThreshold => EditorialTier.Brief,
        _ => EditorialTier.ArchiveOnly,
    };

    /// <summary>Base editorial weight per trigger. Keep the table sorted by story gravity.</summary>
    public static int BaseWeight(NewsEventKind kind) => kind switch
    {
        NewsEventKind.PlayerDied => 100,
        NewsEventKind.BankruptcyDeclared => 90,
        NewsEventKind.TitleClinchedEarly => 85,
        NewsEventKind.CareerCompleted => 82,
        NewsEventKind.ChampionCrowned => 80,
        NewsEventKind.FirstWin => 78,
        NewsEventKind.Level300Reached => 75,
        NewsEventKind.SeasonEndingInjury => 72,
        NewsEventKind.RaceWon => 60,
        NewsEventKind.FinalRoundShowdown => 58,
        NewsEventKind.FirstPodium => 55,
        NewsEventKind.ChampionshipLeadTaken => 54,
        NewsEventKind.HistoryDiverged => 52,
        NewsEventKind.PlayerInjured => 50,
        NewsEventKind.NearBankruptcy => 50,
        NewsEventKind.WinDroughtEnded => 50,
        NewsEventKind.ChampionshipLeadLost => 48,
        NewsEventKind.TitleRaceLost => 48,
        NewsEventKind.PlayerTeamChanged => 46,
        NewsEventKind.FirstPole => 45,
        NewsEventKind.UpsetWinner => 45,
        NewsEventKind.ReturnedFromInjury => 44,
        NewsEventKind.PodiumFinish => 42,
        NewsEventKind.TitleFightTightens => 42,
        NewsEventKind.RivalryDeveloped => 42,
        NewsEventKind.FirstPoints => 40,
        NewsEventKind.CareerMilestone => 40,
        NewsEventKind.WinStreak => 40,
        NewsEventKind.LevelMilestone => 36,
        NewsEventKind.SponsorSigned => 35,
        NewsEventKind.FinancialWindfall => 35,
        NewsEventKind.AiWinStreak => 38,
        NewsEventKind.PolePosition => 36,
        NewsEventKind.DriverRetired => 36,
        NewsEventKind.RetiredMechanical => 34,
        NewsEventKind.RetiredDriverError => 34,
        NewsEventKind.Overperformed => 34,
        NewsEventKind.SeasonCompleted => 34,
        NewsEventKind.DominantDisplay => 32,
        NewsEventKind.MajorRepairBill => 32,
        NewsEventKind.DevelopmentMilestone => 30,
        NewsEventKind.TeamPromoted => 32,
        NewsEventKind.TeamRelegated => 32,
        NewsEventKind.RetirementConsidered => 30,
        NewsEventKind.OfferReceived => 30,
        NewsEventKind.FirstStart => 30,
        NewsEventKind.CareerCreated => 30,
        NewsEventKind.QualifyingSurprise => 28,
        NewsEventKind.PodiumStreak => 28,
        NewsEventKind.SatOutRound => 28,
        NewsEventKind.FirstTop5 => 26,
        NewsEventKind.PointsDroughtEnded => 26,
        NewsEventKind.Underperformed => 26,
        NewsEventKind.StandingsClimb => 24,
        NewsEventKind.RetirementStreak => 24,
        NewsEventKind.LeadChangedHands => 24,
        NewsEventKind.SmgpCanonDiverged => 26,
        NewsEventKind.SeatFilled => 22,
        NewsEventKind.SeatVacancy => 20,
        NewsEventKind.PointsStreak => 20,
        NewsEventKind.PointsFinish => 20,
        NewsEventKind.FirstRetirement => 20,
        NewsEventKind.SeasonStarted => 20,
        NewsEventKind.BestFinishImproved => 18,
        NewsEventKind.DnqDrama => 18,
        NewsEventKind.HistoryHeld => 14,
        NewsEventKind.SmgpCanonHeld => 12,
        NewsEventKind.MidfieldResult => 12,
        _ => 10,
    };
}

/// <summary>A scored, tiered pick from the selector.</summary>
public sealed record EditorialSelection
{
    public required NewsEvent Event { get; init; }
    public required int Score { get; init; }
    public required EditorialTier Tier { get; init; }
}

/// <summary>
/// Turns a round's candidate events into a controlled editorial package: fewer meaningful
/// stories over exhaustive filler. Deterministic — same events in, same package out.
/// </summary>
public static class EditorialSelector
{
    public const int QuietWeekendTarget = 5;
    public const int BusyWeekendTarget = 8;
    public const int BigWeekendTarget = 12;
    public const int MaxWeekendStories = 14;
    private const int PerCategoryCap = 3;

    /// <summary>
    /// Selects the stories worth publishing for one round. Every event scoring Featured or
    /// higher is kept (capped per kind-family), the rest fill toward the quiet-weekend floor
    /// in score order, and same-dedupe-key duplicates are dropped outright.
    /// </summary>
    public static IReadOnlyList<EditorialSelection> SelectRound(IReadOnlyList<NewsEvent> roundEvents)
    {
        var scored = new List<EditorialSelection>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var e in roundEvents)
        {
            if (!seenKeys.Add(e.DedupeKey))
            {
                continue;
            }

            var score = EditorialImportance.Score(e);
            scored.Add(new EditorialSelection { Event = e, Score = score, Tier = EditorialImportance.Tier(score) });
        }

        // Stable order: score desc, then kind declaration order, then subject.
        var ordered = scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => (int)s.Event.Kind)
            .ThenBy(s => s.Event.SubjectId, StringComparer.Ordinal)
            .ToList();

        // The package grows with the weekend: a lead-story weekend carries a full spread,
        // a merely busy one a solid package, a quiet one a handful of stronger pieces.
        var target = ordered.Any(s => s.Tier == EditorialTier.Lead) ? BigWeekendTarget
            : ordered.Any(s => s.Tier == EditorialTier.Featured) ? BusyWeekendTarget
            : QuietWeekendTarget;

        var picks = new List<EditorialSelection>();
        var perFamily = new Dictionary<NewsEventKind, int>();

        foreach (var s in ordered)
        {
            if (picks.Count >= MaxWeekendStories)
            {
                break;
            }

            var family = s.Event.Kind;
            var count = perFamily.GetValueOrDefault(family);
            if (count >= PerCategoryCap)
            {
                continue;
            }

            var wanted = s.Tier <= EditorialTier.Featured
                || s.Tier == EditorialTier.Standard && picks.Count < target
                || s.Tier == EditorialTier.Brief && picks.Count < QuietWeekendTarget;
            if (!wanted)
            {
                continue;
            }

            perFamily[family] = count + 1;
            picks.Add(s);
        }

        return picks;
    }
}
