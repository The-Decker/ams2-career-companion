namespace Companion.Core.Newsroom;

/// <summary>
/// The mode-agnostic career event detector — the newsroom's single trigger source. A pure
/// projection over shaped, already-folded facts (<see cref="NewsroomSeason"/>): no I/O, no RNG,
/// no fold input, so it is free to evolve without touching replay. Walks the career
/// chronologically carrying career-wide memory (firsts, streaks, droughts, milestones, best
/// finish, championship lead) and per-season memory (clinch/tightens/showdown once-flags,
/// AI win streaks), emitting one <see cref="NewsEvent"/> per distinct story opportunity.
/// Round sentinels mirror the SMGP dispatch contract: 0 = season start, 9999 = season end.
/// </summary>
public static class CareerNewsEvents
{
    public const int SeasonStartRound = 0;
    public const int SeasonEndRound = 9999;

    private static readonly int[] WinStreakMilestones = [2, 3, 4, 5];
    private static readonly int[] PodiumStreakMilestones = [3, 5, 8];
    private static readonly int[] PointsStreakMilestones = [5, 8, 12];
    private static readonly int[] RetirementStreakMilestones = [2, 3, 5];
    private static readonly int[] AiWinStreakMilestones = [2, 3, 4, 5];
    private static readonly int[] WinMilestones = [5, 10, 20, 25, 50, 75, 100];
    private static readonly int[] PodiumMilestones = [10, 25, 50, 100];
    private static readonly int[] StartMilestones = [25, 50, 100, 150, 200, 250, 300];

    /// <summary>Character-level milestones worth a story (~11 across an exceptional career; the
    /// 300 cap gets its own <see cref="NewsEventKind.Level300Reached"/> feature instead).</summary>
    private static readonly int[] CharacterLevelMilestones = [25, 50, 75, 100, 125, 150, 175, 200, 225, 250, 275];
    private const int CharacterLevelCap = 300;
    private const int WinDroughtThreshold = 10;
    private const int PointsDroughtThreshold = 5;
    private const int UpsetThreshold = 3;
    private const int QualifyingSurpriseThreshold = 5;

    public static IReadOnlyList<NewsEvent> Detect(IReadOnlyList<NewsroomSeason> seasons)
    {
        var events = new List<NewsEvent>();
        var memory = new CareerMemory();

        foreach (var season in seasons)
        {
            if (memory.CareerEnded)
            {
                break;
            }

            DetectSeasonStart(season, memory, events);

            var perSeason = new SeasonMemory();
            foreach (var round in season.Rounds)
            {
                DetectRound(season, round, memory, perSeason, events);
                if (memory.CareerEnded)
                {
                    break;
                }
            }

            if (!memory.CareerEnded)
            {
                DetectSeasonNotes(season, events);
                DetectSeasonEnd(season, memory, events);
            }

            memory.PreviousPlayerTeamId = season.PlayerTeamId;
            memory.PreviousPlayerTeamName = season.PlayerTeamName;
        }

        return events;
    }

    private static void DetectSeasonStart(NewsroomSeason season, CareerMemory memory, List<NewsEvent> events)
    {
        if (season.Ordinal == 1)
        {
            events.Add(Player(season, SeasonStartRound, NewsEventKind.CareerCreated, season.PlayerTeamName));
        }

        events.Add(Player(season, SeasonStartRound, NewsEventKind.SeasonStarted, season.PlayerTeamName,
            new NewsEventFacts { IsSeasonOpener = true }));

        var moved = season.Ordinal > 1
            && season.PlayerTeamId.Length > 0
            && memory.PreviousPlayerTeamId.Length > 0
            && !string.Equals(season.PlayerTeamId, memory.PreviousPlayerTeamId, StringComparison.Ordinal);
        if (moved)
        {
            events.Add(Player(season, SeasonStartRound, NewsEventKind.PlayerTeamChanged, season.PlayerTeamName,
                new NewsEventFacts { RivalName = memory.PreviousPlayerTeamName }));
        }
    }

    private static void DetectRound(
        NewsroomSeason season,
        NewsroomRound round,
        CareerMemory memory,
        SeasonMemory perSeason,
        List<NewsEvent> events)
    {
        if (round.PlayerDidNotStart)
        {
            events.Add(Player(season, round.Round, NewsEventKind.SatOutRound, season.PlayerTeamName,
                new NewsEventFacts { MissRaces = round.AccidentMissRaces }, round.Venue));
            // Injury sit-outs are today's only DNS cause — remember the absence so the next
            // genuine start reads as the comeback story.
            memory.PendingInjuryReturn = true;
            memory.MissedWhileInjured++;
        }
        else
        {
            DetectPlayerResult(season, round, memory, perSeason, events);
            DetectQualifying(season, round, memory, events);
        }

        DetectAccident(season, round, memory, events);
        if (memory.CareerEnded)
        {
            return;
        }

        EmitLevelMilestones(season, round.Round, round.Venue, round.PlayerLevelAfter, memory, events);

        DetectEconomy(season, round, memory, perSeason, events);
        if (memory.CareerEnded)
        {
            return;
        }

        DetectAiWorld(season, round, perSeason, events);

        if (round.Championship)
        {
            DetectChampionshipMovement(season, round, memory, perSeason, events);
        }
    }

    /// <summary>Dynasty owner-economy stories (economy §8) — pure projections of the shaped
    /// ledger facts. Every field is empty/default for a non-economy career, so this emits
    /// nothing there and older careers' event sets are unchanged.</summary>
    private static void DetectEconomy(
        NewsroomSeason season,
        NewsroomRound round,
        CareerMemory memory,
        SeasonMemory perSeason,
        List<NewsEvent> events)
    {
        // Sponsors signed at this round's decision window — one story each (the sponsor name
        // disambiguates same-round signings).
        foreach (string sponsor in round.EconomySponsorsSigned)
        {
            events.Add(Player(season, round.Round, NewsEventKind.SponsorSigned, season.PlayerTeamName,
                new NewsEventFacts { SponsorName = sponsor }, round.Venue) with
            {
                Discriminator = sponsor,
            });
        }

        if (round.EconomyMajorRepair && round.EconomyRepairAmount.Length > 0)
        {
            events.Add(Player(season, round.Round, NewsEventKind.MajorRepairBill, season.PlayerTeamName,
                new NewsEventFacts { MoneyAmount = round.EconomyRepairAmount }, round.Venue));
        }

        // Terminal: the settlement folded the team — the economy's PlayerDied.
        if (round.EconomyBankrupt)
        {
            events.Add(Player(season, round.Round, NewsEventKind.BankruptcyDeclared, season.PlayerTeamName,
                new NewsEventFacts { MoneyAmount = round.EconomyBalance }, round.Venue));
            memory.CareerEnded = true;
            return;
        }

        // Edge-triggered brink coverage: one story per deficit streak that consumes the whole
        // grace window; a recovery re-arms it so a later brush is its own story.
        if (round.EconomyOnTheBrink)
        {
            if (!memory.EconomyBrinkAnnounced)
            {
                memory.EconomyBrinkAnnounced = true;
                events.Add(Player(season, round.Round, NewsEventKind.NearBankruptcy, season.PlayerTeamName,
                    new NewsEventFacts { MoneyAmount = round.EconomyBalance }, round.Venue));
            }
        }
        else
        {
            memory.EconomyBrinkAnnounced = false;
        }

        if (round.EconomyDevelopmentMaxed && !perSeason.EconomyDevelopmentMaxedEmitted)
        {
            perSeason.EconomyDevelopmentMaxedEmitted = true;
            events.Add(Player(season, round.Round, NewsEventKind.DevelopmentMilestone, season.PlayerTeamName,
                new NewsEventFacts
                {
                    MilestoneValue = round.EconomyDevelopmentLevel ?? 0,
                    MilestoneCounter = "development",
                }, round.Venue));
        }
    }

    private static void DetectPlayerResult(
        NewsroomSeason season,
        NewsroomRound round,
        CareerMemory memory,
        SeasonMemory perSeason,
        List<NewsEvent> events)
    {
        var raced = round.PlayerFinish is not null || round.PlayerDnfCause.Length > 0;
        if (!raced)
        {
            return;
        }

        if (!memory.Started)
        {
            memory.Started = true;
            events.Add(Player(season, round.Round, NewsEventKind.FirstStart, season.PlayerTeamName,
                venue: round.Venue));
        }

        // The first genuine start after injury sit-out rounds is the comeback story — emitted
        // once per absence (the dedupe key is the comeback round itself).
        if (memory.PendingInjuryReturn)
        {
            events.Add(Player(season, round.Round, NewsEventKind.ReturnedFromInjury, season.PlayerTeamName,
                new NewsEventFacts
                {
                    MissRaces = memory.MissedWhileInjured,
                    PlayerFinish = round.PlayerFinish,
                    ExpectedFinish = round.ExpectedFinish,
                    IsWet = round.IsWet == true,
                }, round.Venue));
            memory.PendingInjuryReturn = false;
            memory.MissedWhileInjured = 0;
        }

        memory.CareerStarts++;
        EmitCounterMilestone(season, round, events, memory.CareerStarts, StartMilestones, "starts");

        var finish = round.PlayerFinish;
        var dnf = finish is null;
        var upset = round.ExpectedFinish is int expected && finish is int actual ? expected - actual : 0;
        // perSeason.LeaderId still holds the PRE-round leader here — championship movement
        // (which updates it) runs after result detection in DetectRound.
        var tookLead = round.Championship
            && round.LeaderId == "player"
            && !string.Equals(perSeason.LeaderId, "player", StringComparison.Ordinal);

        var facts = new NewsEventFacts
        {
            PlayerFinish = finish,
            ExpectedFinish = round.ExpectedFinish,
            UpsetMagnitude = upset,
            IsWet = round.IsWet == true,
            IsFinalRound = round.IsFinalChampionshipRound,
            QualifyingPosition = round.PlayerQualifyingPosition,
            ChampionshipPosition = round.PlayerPosition,
            RivalInvolved = round.RivalName.Length > 0,
            RivalName = round.RivalName,
            TookChampionshipLead = tookLead,
        };

        if (dnf)
        {
            DetectPlayerDnf(season, round, memory, events, facts);
            return;
        }

        var position = finish!.Value;
        memory.RetirementStreak = 0;

        // Streak/drought bookkeeping BEFORE emitting so lengths describe the run including today.
        var endedWinDrought = position == 1 && memory.WonEver && memory.RacesSinceWin >= WinDroughtThreshold;
        var winDroughtLength = memory.RacesSinceWin;
        var endedPointsDrought = round.PlayerScoredPoints && memory.ScoredEver
            && memory.RacesSincePoints >= PointsDroughtThreshold;
        var pointsDroughtLength = memory.RacesSincePoints;

        if (position == 1)
        {
            memory.WinStreak++;
            memory.CareerWins++;
            memory.RacesSinceWin = 0;
        }
        else
        {
            memory.WinStreak = 0;
            if (memory.WonEver) memory.RacesSinceWin++;
        }

        if (position <= 3)
        {
            memory.PodiumStreak++;
            memory.CareerPodiums++;
        }
        else
        {
            memory.PodiumStreak = 0;
        }

        if (round.PlayerScoredPoints)
        {
            memory.PointsStreak++;
            memory.RacesSincePoints = 0;
        }
        else
        {
            memory.PointsStreak = 0;
            if (memory.ScoredEver) memory.RacesSincePoints++;
        }

        var kind = position switch
        {
            1 => NewsEventKind.RaceWon,
            <= 3 => NewsEventKind.PodiumFinish,
            _ when upset >= UpsetThreshold => NewsEventKind.Overperformed,
            _ when upset <= -UpsetThreshold => NewsEventKind.Underperformed,
            _ when round.PlayerScoredPoints => NewsEventKind.PointsFinish,
            _ => NewsEventKind.MidfieldResult,
        };

        var firstOfItsKind = kind switch
        {
            NewsEventKind.RaceWon => !memory.WonEver,
            NewsEventKind.PodiumFinish => !memory.PodiumEver,
            _ => round.PlayerScoredPoints && !memory.ScoredEver,
        };

        events.Add(Player(season, round.Round, kind, season.PlayerTeamName,
            facts with
            {
                IsFirstEver = firstOfItsKind,
                StreakLength = position == 1 ? memory.WinStreak : position <= 3 ? memory.PodiumStreak : 0,
                DroughtLength = endedWinDrought ? winDroughtLength : 0,
            },
            round.Venue));

        // Career-first feature stories (distinct kinds → distinct dedupe keys → their own angle).
        if (round.PlayerScoredPoints && !memory.ScoredEver)
        {
            memory.ScoredEver = true;
            events.Add(Player(season, round.Round, NewsEventKind.FirstPoints, season.PlayerTeamName, facts, round.Venue));
        }
        if (position <= 5 && !memory.Top5Ever)
        {
            memory.Top5Ever = true;
            if (position > 3)
            {
                events.Add(Player(season, round.Round, NewsEventKind.FirstTop5, season.PlayerTeamName, facts, round.Venue));
            }
        }
        if (position <= 3 && !memory.PodiumEver)
        {
            memory.PodiumEver = true;
            if (position > 1)
            {
                events.Add(Player(season, round.Round, NewsEventKind.FirstPodium, season.PlayerTeamName, facts, round.Venue));
            }
        }
        if (position == 1 && !memory.WonEver)
        {
            memory.WonEver = true;
            memory.PodiumEver = true;
            memory.Top5Ever = true;
            events.Add(Player(season, round.Round, NewsEventKind.FirstWin, season.PlayerTeamName, facts, round.Venue));
        }

        if (endedWinDrought)
        {
            events.Add(Player(season, round.Round, NewsEventKind.WinDroughtEnded, season.PlayerTeamName,
                facts with { DroughtLength = winDroughtLength }, round.Venue));
        }
        else if (endedPointsDrought)
        {
            events.Add(Player(season, round.Round, NewsEventKind.PointsDroughtEnded, season.PlayerTeamName,
                facts with { DroughtLength = pointsDroughtLength }, round.Venue));
        }

        EmitStreakMilestone(season, round, events, NewsEventKind.WinStreak, memory.WinStreak, WinStreakMilestones, facts);
        EmitStreakMilestone(season, round, events, NewsEventKind.PodiumStreak, memory.PodiumStreak, PodiumStreakMilestones, facts);
        EmitStreakMilestone(season, round, events, NewsEventKind.PointsStreak, memory.PointsStreak, PointsStreakMilestones, facts);

        EmitCounterMilestone(season, round, events, position == 1 ? memory.CareerWins : -1, WinMilestones, "wins");
        EmitCounterMilestone(season, round, events, position <= 3 ? memory.CareerPodiums : -1, PodiumMilestones, "podiums");

        if (memory.BestFinish is null || position < memory.BestFinish)
        {
            var improved = memory.BestFinish is not null && position > 1 && !firstOfItsKind;
            memory.BestFinish = position;
            if (improved)
            {
                events.Add(Player(season, round.Round, NewsEventKind.BestFinishImproved, season.PlayerTeamName,
                    facts, round.Venue));
            }
        }
    }

    private static void DetectPlayerDnf(
        NewsroomSeason season,
        NewsroomRound round,
        CareerMemory memory,
        List<NewsEvent> events,
        NewsEventFacts facts)
    {
        memory.WinStreak = 0;
        memory.PodiumStreak = 0;
        memory.PointsStreak = 0;
        memory.RetirementStreak++;
        if (memory.WonEver) memory.RacesSinceWin++;
        if (memory.ScoredEver) memory.RacesSincePoints++;

        var kind = string.Equals(round.PlayerDnfCause, "mechanical", StringComparison.OrdinalIgnoreCase)
            ? NewsEventKind.RetiredMechanical
            : NewsEventKind.RetiredDriverError;

        events.Add(Player(season, round.Round, kind, season.PlayerTeamName,
            facts with { IsFirstEver = !memory.RetiredEver, StreakLength = memory.RetirementStreak },
            round.Venue));

        if (!memory.RetiredEver)
        {
            memory.RetiredEver = true;
            events.Add(Player(season, round.Round, NewsEventKind.FirstRetirement, season.PlayerTeamName, facts, round.Venue));
        }

        EmitStreakMilestone(season, round, events, NewsEventKind.RetirementStreak, memory.RetirementStreak,
            RetirementStreakMilestones, facts);
    }

    private static void DetectQualifying(
        NewsroomSeason season,
        NewsroomRound round,
        CareerMemory memory,
        List<NewsEvent> events)
    {
        if (round.PlayerQualifyingPosition is not int quali)
        {
            return;
        }

        var facts = new NewsEventFacts
        {
            QualifyingPosition = quali,
            ExpectedFinish = round.ExpectedFinish,
            IsWet = round.IsWet == true,
        };

        if (quali == 1)
        {
            events.Add(Player(season, round.Round, NewsEventKind.PolePosition, season.PlayerTeamName,
                facts with { IsFirstEver = !memory.PoleEver }, round.Venue));
            if (!memory.PoleEver)
            {
                memory.PoleEver = true;
                events.Add(Player(season, round.Round, NewsEventKind.FirstPole, season.PlayerTeamName, facts, round.Venue));
            }
        }
        else if (round.ExpectedFinish is int expected
            && Math.Abs(quali - expected) >= QualifyingSurpriseThreshold)
        {
            events.Add(Player(season, round.Round, NewsEventKind.QualifyingSurprise, season.PlayerTeamName,
                facts with { UpsetMagnitude = expected - quali }, round.Venue));
        }
    }

    private static void DetectAccident(
        NewsroomSeason season,
        NewsroomRound round,
        CareerMemory memory,
        List<NewsEvent> events)
    {
        if (round.AccidentOutcome.Length == 0)
        {
            return;
        }

        var facts = new NewsEventFacts { MissRaces = round.AccidentMissRaces, IsWet = round.IsWet == true };
        switch (round.AccidentOutcome)
        {
            case "minorInjury":
                events.Add(Player(season, round.Round, NewsEventKind.PlayerInjured, season.PlayerTeamName, facts, round.Venue));
                break;
            case "seasonEnding":
                events.Add(Player(season, round.Round, NewsEventKind.SeasonEndingInjury, season.PlayerTeamName, facts, round.Venue));
                break;
            case "death":
                events.Add(Player(season, round.Round, NewsEventKind.PlayerDied, season.PlayerTeamName, facts, round.Venue));
                memory.CareerEnded = true;
                break;
        }
    }

    private static void DetectAiWorld(
        NewsroomSeason season,
        NewsroomRound round,
        SeasonMemory perSeason,
        List<NewsEvent> events)
    {
        if (round.WinnerId.Length == 0 || round.WinnerId == "player")
        {
            perSeason.AiStreakWinnerId = "";
            perSeason.AiStreakLength = 0;
            return;
        }

        if (string.Equals(perSeason.AiStreakWinnerId, round.WinnerId, StringComparison.Ordinal))
        {
            perSeason.AiStreakLength++;
        }
        else
        {
            perSeason.AiStreakWinnerId = round.WinnerId;
            perSeason.AiStreakLength = 1;
        }

        var winnerFacts = new NewsEventFacts
        {
            WinnerName = round.WinnerName,
            WinnerTeamName = round.WinnerTeamName,
            WinnerTeamTier = round.WinnerTeamTier,
            IsWet = round.IsWet == true,
            StreakLength = perSeason.AiStreakLength,
        };

        if (Array.IndexOf(AiWinStreakMilestones, perSeason.AiStreakLength) >= 0)
        {
            events.Add(Subject(season, round.Round, NewsEventKind.AiWinStreak,
                round.WinnerId, round.WinnerName, round.WinnerTeamId, round.WinnerTeamName,
                winnerFacts, round.Venue));
        }

        if (round.WinnerTeamTier is 1 or 2)
        {
            events.Add(Subject(season, round.Round, NewsEventKind.UpsetWinner,
                round.WinnerId, round.WinnerName, round.WinnerTeamId, round.WinnerTeamName,
                winnerFacts, round.Venue));
        }

        if (round.PoleDriverId.Length > 0
            && string.Equals(round.PoleDriverId, round.WinnerId, StringComparison.Ordinal))
        {
            events.Add(Subject(season, round.Round, NewsEventKind.DominantDisplay,
                round.WinnerId, round.WinnerName, round.WinnerTeamId, round.WinnerTeamName,
                winnerFacts, round.Venue));
        }
    }

    private static void DetectChampionshipMovement(
        NewsroomSeason season,
        NewsroomRound round,
        CareerMemory memory,
        SeasonMemory perSeason,
        List<NewsEvent> events)
    {
        if (round.LeaderId.Length == 0)
        {
            return;
        }

        var previousLeader = perSeason.LeaderId;
        perSeason.LeaderId = round.LeaderId;
        perSeason.RoundsSeen++;

        if (round.PlayerPosition is int pos && pos <= 2)
        {
            perSeason.PlayerWasTop2 = true;
        }

        var gap = round.LeaderPoints - round.SecondPoints;
        var climb = perSeason.PreviousPlayerPosition is int prev && round.PlayerPosition is int now
            ? prev - now
            : 0;
        perSeason.PreviousPlayerPosition = round.PlayerPosition;

        var facts = new NewsEventFacts
        {
            ChampionshipPosition = round.PlayerPosition,
            ChampionshipDelta = climb,
            PointsGapToLeader = round.PlayerPoints > 0 ? round.LeaderPoints - round.PlayerPoints : null,
            IsFinalRound = round.IsFinalChampionshipRound,
        };

        // Lead changes (player angles take precedence over the neutral AI story).
        if (previousLeader.Length > 0
            && !string.Equals(previousLeader, round.LeaderId, StringComparison.Ordinal))
        {
            if (round.LeaderId == "player")
            {
                events.Add(Player(season, round.Round, NewsEventKind.ChampionshipLeadTaken,
                    season.PlayerTeamName, facts with { TookChampionshipLead = true }, round.Venue));
            }
            else if (previousLeader == "player")
            {
                events.Add(Player(season, round.Round, NewsEventKind.ChampionshipLeadLost,
                    season.PlayerTeamName,
                    facts with { LostChampionshipLead = true, RivalName = round.LeaderName }, round.Venue));
            }
            else
            {
                events.Add(Subject(season, round.Round, NewsEventKind.LeadChangedHands,
                    round.LeaderId, round.LeaderName, "", "",
                    facts with { RivalName = perSeason.PreviousLeaderName }, round.Venue));
            }
        }
        else if (previousLeader.Length == 0 && round.LeaderId == "player" && perSeason.RoundsSeen == 1)
        {
            events.Add(Player(season, round.Round, NewsEventKind.ChampionshipLeadTaken,
                season.PlayerTeamName,
                facts with { TookChampionshipLead = true, IsSeasonOpener = true }, round.Venue));
        }

        perSeason.PreviousLeaderName = round.LeaderName;

        var oneRoundOfPointsLeft = round.MaxRemainingPoints > 0
            && round.MaxRemainingPoints <= round.MaxPointsPerRound;

        // Mathematically safe clinch: counted points never decrease, a challenger can add at
        // most MaxRemainingPoints (drops only reduce), so gap > MaxRemainingPoints settles it.
        if (!perSeason.ClinchEmitted
            && round.MaxRemainingPoints > 0
            && gap > round.MaxRemainingPoints)
        {
            perSeason.ClinchEmitted = true;
            var clinchFacts = facts with { ClinchedTitle = true, PointsGapToLeader = gap };
            events.Add(round.LeaderId == "player"
                ? Player(season, round.Round, NewsEventKind.TitleClinchedEarly, season.PlayerTeamName, clinchFacts, round.Venue)
                : Subject(season, round.Round, NewsEventKind.TitleClinchedEarly,
                    round.LeaderId, round.LeaderName, "", "", clinchFacts, round.Venue));
        }

        if (!perSeason.TitleLostEmitted
            && perSeason.PlayerWasTop2
            && round.LeaderId != "player"
            && round.PlayerPoints + round.MaxRemainingPoints < round.LeaderPoints)
        {
            perSeason.TitleLostEmitted = true;
            events.Add(Player(season, round.Round, NewsEventKind.TitleRaceLost, season.PlayerTeamName,
                facts with { RivalName = round.LeaderName }, round.Venue));
        }

        if (!perSeason.TightensEmitted
            && !perSeason.ClinchEmitted
            && round.Round * 2 >= season.ChampionshipRoundCount
            && !round.IsFinalChampionshipRound
            && gap >= 0
            && gap <= round.MaxPointsPerRound
            && round.SecondPoints > 0)
        {
            perSeason.TightensEmitted = true;
            events.Add(Subject(season, round.Round, NewsEventKind.TitleFightTightens,
                round.LeaderId, round.LeaderName, "", "",
                facts with { PointsGapToLeader = gap }, round.Venue));
        }

        if (!perSeason.ShowdownEmitted
            && !perSeason.ClinchEmitted
            && oneRoundOfPointsLeft
            && gap <= round.MaxPointsPerRound)
        {
            perSeason.ShowdownEmitted = true;
            events.Add(Subject(season, round.Round, NewsEventKind.FinalRoundShowdown,
                round.LeaderId, round.LeaderName, "", "",
                facts with { PointsGapToLeader = gap }, round.Venue));
        }

        if (climb >= 2 && round.PlayerPosition is int p && p <= 5)
        {
            events.Add(Player(season, round.Round, NewsEventKind.StandingsClimb, season.PlayerTeamName,
                facts, round.Venue));
        }
    }

    private static void DetectSeasonNotes(NewsroomSeason season, List<NewsEvent> events)
    {
        foreach (var note in season.SeasonNotes)
        {
            var kind = note.Kind switch
            {
                NewsroomSeasonNoteKind.TeamPromoted => NewsEventKind.TeamPromoted,
                NewsroomSeasonNoteKind.TeamRelegated => NewsEventKind.TeamRelegated,
                NewsroomSeasonNoteKind.DriverRetired => NewsEventKind.DriverRetired,
                NewsroomSeasonNoteKind.RetirementConsidered => NewsEventKind.RetirementConsidered,
                NewsroomSeasonNoteKind.SeatVacancy => NewsEventKind.SeatVacancy,
                NewsroomSeasonNoteKind.SeatFilled => NewsEventKind.SeatFilled,
                NewsroomSeasonNoteKind.OfferReceived => NewsEventKind.OfferReceived,
                _ => (NewsEventKind?)null,
            };
            if (kind is null)
            {
                continue;
            }

            events.Add(new NewsEvent
            {
                Kind = kind.Value,
                SeasonOrdinal = season.Ordinal,
                SeasonYear = season.Year,
                Round = SeasonEndRound,
                SubjectId = note.SubjectId.Length > 0 ? note.SubjectId : "player",
                SubjectName = note.SubjectName,
                Facts = new NewsEventFacts { MilestoneValue = note.Value, RetirementReason = note.Detail },
            });
        }
    }

    private static void DetectSeasonEnd(NewsroomSeason season, CareerMemory memory, List<NewsEvent> events)
    {
        if (!season.Complete)
        {
            return;
        }

        events.Add(Player(season, SeasonEndRound, NewsEventKind.SeasonCompleted, season.PlayerTeamName));

        if (season.ChampionId.Length > 0)
        {
            events.Add(Subject(season, SeasonEndRound, NewsEventKind.ChampionCrowned,
                season.PlayerChampion ? "player" : season.ChampionId,
                season.ChampionName, "", "",
                new NewsEventFacts { ClinchedTitle = season.PlayerChampion }));
        }

        // Season-end XP awards can cross milestone levels at the boundary too.
        EmitLevelMilestones(season, SeasonEndRound, "", season.PlayerLevelAtSeasonEnd, memory, events);

        // Dynasty economy: a front-running constructors' cheque (and any title bonuses) is a
        // WINDFALL story at the review. Empty/false for every non-economy career.
        if (season.EconomyWindfall && season.EconomySeasonAmount.Length > 0)
        {
            events.Add(Player(season, SeasonEndRound, NewsEventKind.FinancialWindfall, season.PlayerTeamName,
                new NewsEventFacts { MoneyAmount = season.EconomySeasonAmount }));
        }

        if (season.IsCampaignFinale)
        {
            events.Add(Player(season, SeasonEndRound, NewsEventKind.CareerCompleted, season.PlayerTeamName,
                new NewsEventFacts
                {
                    ClinchedTitle = season.PlayerChampion,
                    MilestoneValue = season.Ordinal,
                    MilestoneCounter = "seasons",
                }));
        }
    }

    /// <summary>Emits one story per character-level milestone crossed since the last observed
    /// level, plus the Level 300 feature at the cap. Detection is monotonic over the chronological
    /// walk, so re-detecting the same career emits the same set (the dedupe key carries the
    /// threshold) and a milestone can never fire twice.</summary>
    private static void EmitLevelMilestones(
        NewsroomSeason season,
        int round,
        string venue,
        int? levelAfter,
        CareerMemory memory,
        List<NewsEvent> events)
    {
        if (levelAfter is not int level)
        {
            return;
        }

        // Careers start at level 1; the first shaped value simply establishes the baseline when
        // it did not cross anything.
        int previous = memory.LastSeenLevel ?? 1;
        if (level <= previous)
        {
            memory.LastSeenLevel = Math.Max(previous, level);
            return;
        }

        foreach (int threshold in CharacterLevelMilestones)
        {
            if (previous < threshold && threshold <= level)
            {
                events.Add(Player(season, round, NewsEventKind.LevelMilestone, season.PlayerTeamName,
                    new NewsEventFacts { MilestoneValue = threshold, MilestoneCounter = "level" },
                    venue) with
                { Discriminator = threshold.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            }
        }

        if (previous < CharacterLevelCap && level >= CharacterLevelCap)
        {
            events.Add(Player(season, round, NewsEventKind.Level300Reached, season.PlayerTeamName,
                new NewsEventFacts { MilestoneValue = CharacterLevelCap, MilestoneCounter = "level" },
                venue));
        }

        memory.LastSeenLevel = level;
    }

    private static void EmitStreakMilestone(
        NewsroomSeason season,
        NewsroomRound round,
        List<NewsEvent> events,
        NewsEventKind kind,
        int streak,
        int[] milestones,
        NewsEventFacts baseFacts)
    {
        if (Array.IndexOf(milestones, streak) < 0)
        {
            return;
        }

        events.Add(Player(season, round.Round, kind, season.PlayerTeamName,
            baseFacts with { StreakLength = streak }, round.Venue));
    }

    private static void EmitCounterMilestone(
        NewsroomSeason season,
        NewsroomRound round,
        List<NewsEvent> events,
        int counter,
        int[] milestones,
        string counterName)
    {
        if (counter < 0 || Array.IndexOf(milestones, counter) < 0)
        {
            return;
        }

        events.Add(Player(season, round.Round, NewsEventKind.CareerMilestone, season.PlayerTeamName,
            new NewsEventFacts { MilestoneValue = counter, MilestoneCounter = counterName },
            round.Venue) with
        { Discriminator = counterName });
    }

    private static NewsEvent Player(
        NewsroomSeason season,
        int round,
        NewsEventKind kind,
        string teamName,
        NewsEventFacts? facts = null,
        string venue = "")
        => new()
        {
            Kind = kind,
            SeasonOrdinal = season.Ordinal,
            SeasonYear = season.Year,
            Round = round,
            SubjectId = "player",
            SubjectTeamId = season.PlayerTeamId,
            SubjectTeamName = teamName,
            VenueName = venue,
            Facts = facts ?? new NewsEventFacts(),
        };

    private static NewsEvent Subject(
        NewsroomSeason season,
        int round,
        NewsEventKind kind,
        string subjectId,
        string subjectName,
        string teamId,
        string teamName,
        NewsEventFacts facts,
        string venue = "")
        => new()
        {
            Kind = kind,
            SeasonOrdinal = season.Ordinal,
            SeasonYear = season.Year,
            Round = round,
            SubjectId = subjectId,
            SubjectName = subjectName,
            SubjectTeamId = teamId,
            SubjectTeamName = teamName,
            VenueName = venue,
            Facts = facts,
        };

    private sealed class CareerMemory
    {
        public bool Started;
        public bool ScoredEver;
        public bool Top5Ever;
        public bool PodiumEver;
        public bool WonEver;
        public bool PoleEver;
        public bool RetiredEver;
        public bool CareerEnded;
        public int WinStreak;
        public int PodiumStreak;
        public int PointsStreak;
        public int RetirementStreak;
        public int RacesSinceWin;
        public int RacesSincePoints;
        public int CareerStarts;
        public int CareerWins;
        public int CareerPodiums;
        public int? BestFinish;
        public string PreviousPlayerTeamId = "";
        public string PreviousPlayerTeamName = "";
        public int? LastSeenLevel;
        public bool PendingInjuryReturn;
        public int MissedWhileInjured;
        public bool EconomyBrinkAnnounced;
    }

    private sealed class SeasonMemory
    {
        public string LeaderId = "";
        public string PreviousLeaderName = "";
        public int? PreviousPlayerPosition;
        public int RoundsSeen;
        public bool PlayerWasTop2;
        public bool ClinchEmitted;
        public bool TitleLostEmitted;
        public bool TightensEmitted;
        public bool ShowdownEmitted;
        public string AiStreakWinnerId = "";
        public int AiStreakLength;
        public bool EconomyDevelopmentMaxedEmitted;
    }
}
