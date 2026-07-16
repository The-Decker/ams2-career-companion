namespace Companion.Core.Newsroom;

public enum StoryThreadType
{
    TitleFight,
    ReliabilityCrisis,
    WinDrought,
    HotStreak,
    InjuryRecovery,
    Rivalry,
    DriverMarket,
}

/// <summary>Narrative lifecycle of a developing story. Forward-only per thread.</summary>
public enum StoryThreadState
{
    Emerging,
    Developing,
    Escalating,
    Resolved,
    Dormant,
    Historic,
}

/// <summary>One development inside a thread — a reference to the article that covers it.</summary>
public sealed record StoryThreadEntry
{
    /// <summary>The event/article dedupe key this development links to.</summary>
    public required string StoryKey { get; init; }
    public required int SeasonOrdinal { get; init; }
    public required int Round { get; init; }
    public required string Summary { get; init; }
}

/// <summary>
/// A remembered, developing story: stable <see cref="Key"/>, a typed lifecycle, and the ordered
/// developments that got it here. Pure derivation over the event spine — re-derived on read,
/// deterministic, never stored (docs/dev/newsroom-history-overhaul.md D1/D3).
/// </summary>
public sealed record StoryThread
{
    public required string Key { get; init; }
    public required StoryThreadType Type { get; init; }
    public required StoryThreadState State { get; init; }
    public required string Title { get; init; }
    public required int SeasonOrdinal { get; init; }
    public IReadOnlyList<StoryThreadEntry> Entries { get; init; } = [];
    public int LastRound => Entries.Count > 0 ? Entries[^1].Round : 0;
}

/// <summary>
/// Derives the narrative threads from the chronological event stream. Deterministic and pure:
/// the same events always produce the same threads in the same states.
/// </summary>
public static class StoryThreads
{
    private const int DroughtEmergesAt = 6;
    private const int CrisisEmergesAt = 2;

    public static IReadOnlyList<StoryThread> Build(IReadOnlyList<NewsEvent> events)
    {
        var threads = new List<StoryThread>();
        foreach (var seasonGroup in events.GroupBy(e => e.SeasonOrdinal).OrderBy(g => g.Key))
        {
            var season = seasonGroup.OrderBy(e => e.Round).ToList();
            var complete = season.Any(e => e.Kind == NewsEventKind.SeasonCompleted);
            BuildTitleFight(season, seasonGroup.Key, complete, threads);
            BuildReliabilityCrisis(season, seasonGroup.Key, complete, threads);
            BuildFormThreads(season, seasonGroup.Key, complete, threads);
            BuildInjuryRecovery(season, seasonGroup.Key, complete, threads);
            BuildRivalry(season, seasonGroup.Key, complete, threads);
            BuildDriverMarket(season, seasonGroup.Key, threads);
        }

        return threads;
    }

    private static void BuildTitleFight(
        List<NewsEvent> season, int ordinal, bool complete, List<StoryThread> threads)
    {
        var beats = season.Where(e => e.Kind is NewsEventKind.ChampionshipLeadTaken
            or NewsEventKind.ChampionshipLeadLost or NewsEventKind.LeadChangedHands
            or NewsEventKind.TitleFightTightens or NewsEventKind.FinalRoundShowdown
            or NewsEventKind.TitleClinchedEarly or NewsEventKind.TitleRaceLost
            or NewsEventKind.ChampionCrowned).ToList();
        if (beats.Count == 0)
        {
            return;
        }

        var resolved = beats.Any(e => e.Kind is NewsEventKind.TitleClinchedEarly
            or NewsEventKind.ChampionCrowned or NewsEventKind.TitleRaceLost);
        var escalating = beats.Any(e => e.Kind is NewsEventKind.TitleFightTightens
            or NewsEventKind.FinalRoundShowdown);

        var state = complete ? StoryThreadState.Historic
            : resolved ? StoryThreadState.Resolved
            : escalating ? StoryThreadState.Escalating
            : beats.Count > 1 ? StoryThreadState.Developing
            : StoryThreadState.Emerging;

        threads.Add(new StoryThread
        {
            Key = $"thread:title:{ordinal}",
            Type = StoryThreadType.TitleFight,
            State = state,
            Title = $"The season {ordinal} title race",
            SeasonOrdinal = ordinal,
            Entries = Entries(beats),
        });
    }

    private static void BuildReliabilityCrisis(
        List<NewsEvent> season, int ordinal, bool complete, List<StoryThread> threads)
    {
        var failures = season.Where(e => e.Kind == NewsEventKind.RetiredMechanical).ToList();
        if (failures.Count < CrisisEmergesAt)
        {
            return;
        }

        // Resolved once the machinery holds again: a later classified result exists.
        var lastFailureRound = failures[^1].Round;
        var recoveredAfter = season.Any(e => e.Round > lastFailureRound
            && e.Kind is NewsEventKind.RaceWon or NewsEventKind.PodiumFinish
                or NewsEventKind.PointsFinish or NewsEventKind.MidfieldResult
                or NewsEventKind.Overperformed or NewsEventKind.Underperformed);

        var state = complete ? StoryThreadState.Historic
            : recoveredAfter ? StoryThreadState.Resolved
            : failures.Count >= 3 || failures.Any(f => f.Facts.StreakLength >= 2)
                ? StoryThreadState.Escalating
                : StoryThreadState.Developing;

        threads.Add(new StoryThread
        {
            Key = $"thread:reliability:{ordinal}",
            Type = StoryThreadType.ReliabilityCrisis,
            State = state,
            Title = "A season fighting the machinery",
            SeasonOrdinal = ordinal,
            Entries = Entries(failures),
        });
    }

    private static void BuildFormThreads(
        List<NewsEvent> season, int ordinal, bool complete, List<StoryThread> threads)
    {
        var streakBeats = season.Where(e => e.Kind is NewsEventKind.WinStreak
            or NewsEventKind.PodiumStreak).ToList();
        if (streakBeats.Count > 0)
        {
            var lastBeatRound = streakBeats[^1].Round;
            var broken = season.Any(e => e.Round > lastBeatRound
                && (e.Kind is NewsEventKind.RetiredMechanical or NewsEventKind.RetiredDriverError
                    or NewsEventKind.MidfieldResult or NewsEventKind.Underperformed));
            threads.Add(new StoryThread
            {
                Key = $"thread:form:{ordinal}",
                Type = StoryThreadType.HotStreak,
                State = complete ? StoryThreadState.Historic
                    : broken ? StoryThreadState.Resolved
                    : streakBeats.Max(e => e.Facts.StreakLength) >= 3
                        ? StoryThreadState.Escalating
                        : StoryThreadState.Developing,
                Title = "A run of form the field cannot ignore",
                SeasonOrdinal = ordinal,
                Entries = Entries(streakBeats),
            });
        }

        var droughtEnd = season.FirstOrDefault(e => e.Kind == NewsEventKind.WinDroughtEnded);
        if (droughtEnd is not null && droughtEnd.Facts.DroughtLength >= DroughtEmergesAt)
        {
            threads.Add(new StoryThread
            {
                Key = $"thread:drought:{ordinal}",
                Type = StoryThreadType.WinDrought,
                State = complete ? StoryThreadState.Historic : StoryThreadState.Resolved,
                Title = $"The {droughtEnd.Facts.DroughtLength}-race wait for a win",
                SeasonOrdinal = ordinal,
                Entries = Entries([droughtEnd]),
            });
        }
    }

    private static void BuildInjuryRecovery(
        List<NewsEvent> season, int ordinal, bool complete, List<StoryThread> threads)
    {
        var beats = season.Where(e => e.Kind is NewsEventKind.PlayerInjured
            or NewsEventKind.SeasonEndingInjury or NewsEventKind.SatOutRound
            or NewsEventKind.ReturnedFromInjury).ToList();
        if (beats.Count == 0)
        {
            return;
        }

        var lastAbsence = beats.Where(e => e.Kind != NewsEventKind.ReturnedFromInjury)
            .Select(e => e.Round).DefaultIfEmpty(0).Max();
        // The explicit comeback event is the honest resolver; a later classified result keeps
        // covering older careers whose events predate ReturnedFromInjury detection.
        var returned = beats.Any(e => e.Kind == NewsEventKind.ReturnedFromInjury && e.Round > lastAbsence)
            || season.Any(e => e.Round > lastAbsence
                && e.Kind is NewsEventKind.RaceWon or NewsEventKind.PodiumFinish
                    or NewsEventKind.PointsFinish or NewsEventKind.MidfieldResult
                    or NewsEventKind.Overperformed or NewsEventKind.Underperformed);
        var seasonEnding = beats.Any(e => e.Kind == NewsEventKind.SeasonEndingInjury);

        threads.Add(new StoryThread
        {
            Key = $"thread:injury:{ordinal}",
            Type = StoryThreadType.InjuryRecovery,
            State = complete ? StoryThreadState.Historic
                : returned ? StoryThreadState.Resolved
                : seasonEnding ? StoryThreadState.Dormant
                : StoryThreadState.Developing,
            Title = "The road back from injury",
            SeasonOrdinal = ordinal,
            Entries = Entries(beats),
        });
    }

    private static void BuildRivalry(
        List<NewsEvent> season, int ordinal, bool complete, List<StoryThread> threads)
    {
        foreach (var rivalGroup in season
            .Where(e => e.Facts.RivalInvolved && e.Facts.RivalName.Length > 0)
            .GroupBy(e => e.Facts.RivalName, StringComparer.Ordinal))
        {
            var beats = rivalGroup.ToList();
            threads.Add(new StoryThread
            {
                Key = $"thread:rivalry:{ordinal}:{Slug(rivalGroup.Key)}",
                Type = StoryThreadType.Rivalry,
                State = complete ? StoryThreadState.Historic
                    : beats.Count >= 3 ? StoryThreadState.Escalating
                    : beats.Count == 2 ? StoryThreadState.Developing
                    : StoryThreadState.Emerging,
                Title = $"The duel with {rivalGroup.Key}",
                SeasonOrdinal = ordinal,
                Entries = Entries(beats),
            });
        }
    }

    private static void BuildDriverMarket(
        List<NewsEvent> season, int ordinal, List<StoryThread> threads)
    {
        var beats = season.Where(e => e.Kind is NewsEventKind.OfferReceived
            or NewsEventKind.SeatVacancy or NewsEventKind.SeatFilled
            or NewsEventKind.RetirementConsidered or NewsEventKind.DriverRetired
            or NewsEventKind.PlayerTeamChanged).ToList();
        if (beats.Count == 0)
        {
            return;
        }

        threads.Add(new StoryThread
        {
            Key = $"thread:market:{ordinal}",
            Type = StoryThreadType.DriverMarket,
            // The market thread lives between seasons; it goes historic when the next season's
            // moves land (a PlayerTeamChanged in a LATER season closes it, handled by readers).
            State = beats.Count >= 3 ? StoryThreadState.Escalating
                : beats.Count == 2 ? StoryThreadState.Developing
                : StoryThreadState.Emerging,
            Title = $"Silly season, year {ordinal}",
            SeasonOrdinal = ordinal,
            Entries = Entries(beats),
        });
    }

    private static IReadOnlyList<StoryThreadEntry> Entries(IReadOnlyList<NewsEvent> beats) =>
        beats.Select(b => new StoryThreadEntry
        {
            StoryKey = b.DedupeKey,
            SeasonOrdinal = b.SeasonOrdinal,
            Round = b.Round,
            Summary = Summarize(b),
        }).ToList();

    private static string Summarize(NewsEvent e)
    {
        var venue = e.VenueName.Length > 0 ? $" — {e.VenueName}" : "";
        return e.Kind switch
        {
            NewsEventKind.ChampionshipLeadTaken => $"Takes the championship lead{venue}",
            NewsEventKind.ChampionshipLeadLost => $"Loses the lead{(e.Facts.RivalName.Length > 0 ? $" to {e.Facts.RivalName}" : "")}",
            NewsEventKind.LeadChangedHands => $"{e.SubjectName} takes over at the top",
            NewsEventKind.TitleFightTightens => $"Gap at the top closes to {e.Facts.PointsGapToLeader:0.##} points",
            NewsEventKind.FinalRoundShowdown => "The title goes to the final round",
            NewsEventKind.TitleClinchedEarly => "The championship is sealed early",
            NewsEventKind.TitleRaceLost => "The title challenge ends",
            NewsEventKind.ChampionCrowned => $"{e.SubjectName} crowned champion",
            NewsEventKind.RetiredMechanical => $"Mechanical retirement{venue}",
            NewsEventKind.WinStreak => $"{e.Facts.StreakLength} wins in a row",
            NewsEventKind.PodiumStreak => $"{e.Facts.StreakLength} podiums in a row",
            NewsEventKind.WinDroughtEnded => $"Wins again after {e.Facts.DroughtLength} races",
            NewsEventKind.PlayerInjured => $"Injured{venue}",
            NewsEventKind.SeasonEndingInjury => "Season ended by injury",
            NewsEventKind.SatOutRound => $"Sits out round {e.Round}",
            NewsEventKind.OfferReceived => $"An offer arrives{(e.SubjectName.Length > 0 ? $" from {e.SubjectName}" : "")}",
            NewsEventKind.SeatVacancy => "A seat opens",
            NewsEventKind.SeatFilled => $"{e.SubjectName} takes an open seat",
            NewsEventKind.RetirementConsidered => $"{e.SubjectName} weighs the future",
            NewsEventKind.DriverRetired => $"{e.SubjectName} retires",
            NewsEventKind.PlayerTeamChanged => $"Moves to {e.SubjectTeamName}",
            _ => e.Kind.ToString(),
        };
    }

    private static string Slug(string name)
    {
        var chars = name.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        return new string(chars).Trim('-');
    }
}
