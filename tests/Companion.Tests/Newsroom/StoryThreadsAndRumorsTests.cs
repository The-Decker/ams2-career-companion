using Companion.Core.Newsroom;
using Xunit;

namespace Companion.Tests.Newsroom;

public class StoryThreadsAndRumorsTests
{
    [Fact]
    public void ATitleFightEscalatesAndResolvesAcrossTheSeason()
    {
        var events = new List<NewsEvent>
        {
            E(NewsEventKind.ChampionshipLeadTaken, round: 2),
            E(NewsEventKind.ChampionshipLeadLost, round: 5, facts: new NewsEventFacts { RivalName = "A. Senna" }),
            E(NewsEventKind.TitleFightTightens, round: 9, subject: "driver.senna",
                facts: new NewsEventFacts { PointsGapToLeader = 3 }),
            E(NewsEventKind.ChampionCrowned, round: CareerNewsEvents.SeasonEndRound, subject: "driver.senna"),
        };

        var open = StoryThreads.Build(events);
        var fight = Assert.Single(open, t => t.Type == StoryThreadType.TitleFight);
        Assert.Equal(StoryThreadState.Resolved, fight.State);
        Assert.Equal(4, fight.Entries.Count);
        Assert.All(fight.Entries, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Summary)));

        // A completed season turns the thread historic.
        events.Add(E(NewsEventKind.SeasonCompleted, round: CareerNewsEvents.SeasonEndRound));
        var closed = StoryThreads.Build(events);
        Assert.Equal(StoryThreadState.Historic,
            Assert.Single(closed, t => t.Type == StoryThreadType.TitleFight).State);
    }

    [Fact]
    public void AReliabilityCrisisResolvesWhenTheCarHoldsAgain()
    {
        var failing = new List<NewsEvent>
        {
            E(NewsEventKind.RetiredMechanical, round: 3),
            E(NewsEventKind.RetiredMechanical, round: 4, facts: new NewsEventFacts { StreakLength = 2 }),
        };
        var crisis = Assert.Single(StoryThreads.Build(failing), t => t.Type == StoryThreadType.ReliabilityCrisis);
        Assert.Equal(StoryThreadState.Escalating, crisis.State);

        failing.Add(E(NewsEventKind.PointsFinish, round: 5, facts: new NewsEventFacts { PlayerFinish = 5 }));
        var recovered = Assert.Single(StoryThreads.Build(failing), t => t.Type == StoryThreadType.ReliabilityCrisis);
        Assert.Equal(StoryThreadState.Resolved, recovered.State);
    }

    [Fact]
    public void OneMechanicalFailureIsNotACrisis()
    {
        var events = new List<NewsEvent> { E(NewsEventKind.RetiredMechanical, round: 3) };
        Assert.DoesNotContain(StoryThreads.Build(events), t => t.Type == StoryThreadType.ReliabilityCrisis);
    }

    [Fact]
    public void InjuryThreadsTrackTheRoadBack()
    {
        var events = new List<NewsEvent>
        {
            E(NewsEventKind.PlayerInjured, round: 4, facts: new NewsEventFacts { MissRaces = 2 }),
            E(NewsEventKind.SatOutRound, round: 5),
            E(NewsEventKind.SatOutRound, round: 6),
        };
        var recovering = Assert.Single(StoryThreads.Build(events), t => t.Type == StoryThreadType.InjuryRecovery);
        Assert.Equal(StoryThreadState.Developing, recovering.State);

        events.Add(E(NewsEventKind.MidfieldResult, round: 7, facts: new NewsEventFacts { PlayerFinish = 9 }));
        var back = Assert.Single(StoryThreads.Build(events), t => t.Type == StoryThreadType.InjuryRecovery);
        Assert.Equal(StoryThreadState.Resolved, back.State);
    }

    [Fact]
    public void RivalryThreadsGroupByRivalAndEscalateWithMeetings()
    {
        var events = new List<NewsEvent>
        {
            E(NewsEventKind.PodiumFinish, round: 2, facts: RivalFacts()),
            E(NewsEventKind.RaceWon, round: 5, facts: RivalFacts()),
            E(NewsEventKind.PointsFinish, round: 8, facts: RivalFacts()),
            E(NewsEventKind.PodiumFinish, round: 9, facts: new NewsEventFacts { RivalInvolved = true, RivalName = "Other Guy" }),
        };

        var threads = StoryThreads.Build(events).Where(t => t.Type == StoryThreadType.Rivalry).ToList();
        Assert.Equal(2, threads.Count);
        var main = Assert.Single(threads, t => t.Title.Contains("G. Ceara"));
        Assert.Equal(StoryThreadState.Escalating, main.State);
        Assert.Equal(StoryThreadState.Emerging, Assert.Single(threads, t => t.Title.Contains("Other Guy")).State);

        static NewsEventFacts RivalFacts() => new() { RivalInvolved = true, RivalName = "G. Ceara" };
    }

    [Fact]
    public void ARetirementWhisperResolvesHonestly()
    {
        var whisper = E(NewsEventKind.RetirementConsidered, round: CareerNewsEvents.SeasonEndRound,
            subject: "driver.vet", subjectName: "Old Hand");

        // Open while the story can still go either way.
        var open = RumorBook.Build([whisper]);
        Assert.Equal(RumorResolutionKind.Open, Assert.Single(open).Resolution);
        Assert.Equal("", Assert.Single(open).ResolvedByKey);

        // Confirmed by the retirement the following season - linked, never rewritten.
        var retired = E(NewsEventKind.DriverRetired, round: CareerNewsEvents.SeasonEndRound,
            subject: "driver.vet", subjectName: "Old Hand", ordinal: 2);
        var confirmed = Assert.Single(RumorBook.Build([whisper, retired]));
        Assert.Equal(RumorResolutionKind.Confirmed, confirmed.Resolution);
        Assert.Equal(retired.DedupeKey, confirmed.ResolvedByKey);
        Assert.Equal(whisper.DedupeKey, confirmed.RumorKey);

        // Denied by time: two seasons later, still racing.
        var laterSeason = E(NewsEventKind.SeasonStarted, round: 0, ordinal: 3);
        var denied = Assert.Single(RumorBook.Build([whisper, laterSeason]));
        Assert.Equal(RumorResolutionKind.Denied, denied.Resolution);
        Assert.Equal("", denied.ResolvedByKey);
    }

    [Fact]
    public void AnOfferRumorConfirmsOnlyWhenTheMoveMatchesTheTeam()
    {
        var offer = E(NewsEventKind.OfferReceived, round: CareerNewsEvents.SeasonEndRound,
            subjectName: "Big Team");

        var movedThere = E(NewsEventKind.PlayerTeamChanged, round: 0, ordinal: 2, teamName: "Big Team");
        var confirmed = Assert.Single(RumorBook.Build([offer, movedThere]));
        Assert.Equal(RumorResolutionKind.Confirmed, confirmed.Resolution);

        var stayedPut = E(NewsEventKind.SeasonStarted, round: 0, ordinal: 2);
        var denied = Assert.Single(RumorBook.Build([offer, stayedPut]));
        Assert.Equal(RumorResolutionKind.Denied, denied.Resolution);
    }

    [Fact]
    public void ThreadsAndRumorsAreDeterministic()
    {
        var events = CareerNewsEventsTests.RichCareer().SelectMany(s => CareerNewsEvents.Detect([s])).ToList();
        Assert.Equal(
            StoryThreads.Build(events).Select(t => t.Key + ":" + t.State),
            StoryThreads.Build(events).Select(t => t.Key + ":" + t.State));
        Assert.Equal(
            RumorBook.Build(events).Select(r => r.RumorKey + ":" + r.Resolution),
            RumorBook.Build(events).Select(r => r.RumorKey + ":" + r.Resolution));
    }

    private static NewsEvent E(
        NewsEventKind kind,
        int round,
        string subject = "player",
        string subjectName = "",
        string teamName = "",
        NewsEventFacts? facts = null,
        int ordinal = 1) => new()
    {
        Kind = kind,
        SeasonOrdinal = ordinal,
        SeasonYear = 1987 + ordinal,
        Round = round,
        SubjectId = subject,
        SubjectName = subjectName,
        SubjectTeamName = teamName,
        VenueName = round is > 0 and < CareerNewsEvents.SeasonEndRound ? $"Venue {round}" : "",
        Facts = facts ?? new NewsEventFacts(),
    };
}
