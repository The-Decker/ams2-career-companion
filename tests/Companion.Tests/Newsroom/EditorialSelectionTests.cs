using Companion.Core.Newsroom;
using Xunit;

namespace Companion.Tests.Newsroom;

public class EditorialSelectionTests
{
    [Fact]
    public void CombinationFactsOutrankTheGenericTemplateOfTheSameKind()
    {
        var plainWin = Event(NewsEventKind.RaceWon);
        var firstWin = Event(NewsEventKind.RaceWon, new NewsEventFacts { IsFirstEver = true });
        var titleWin = Event(NewsEventKind.RaceWon, new NewsEventFacts { ClinchedTitle = true, IsFinalRound = true });

        Assert.True(EditorialImportance.Score(firstWin) > EditorialImportance.Score(plainWin));
        Assert.True(EditorialImportance.Score(titleWin) > EditorialImportance.Score(firstWin));
    }

    [Fact]
    public void TiersFollowTheDocumentedThresholds()
    {
        Assert.Equal(EditorialTier.Lead, EditorialImportance.Tier(70));
        Assert.Equal(EditorialTier.Featured, EditorialImportance.Tier(69));
        Assert.Equal(EditorialTier.Featured, EditorialImportance.Tier(50));
        Assert.Equal(EditorialTier.Standard, EditorialImportance.Tier(49));
        Assert.Equal(EditorialTier.Standard, EditorialImportance.Tier(30));
        Assert.Equal(EditorialTier.Brief, EditorialImportance.Tier(29));
        Assert.Equal(EditorialTier.Brief, EditorialImportance.Tier(15));
        Assert.Equal(EditorialTier.ArchiveOnly, EditorialImportance.Tier(14));
    }

    [Fact]
    public void ScoringHasNoRandomness()
    {
        var e = Event(NewsEventKind.PodiumFinish, new NewsEventFacts { StreakLength = 3, UpsetMagnitude = 4, IsWet = true });
        var scores = Enumerable.Range(0, 25).Select(_ => EditorialImportance.Score(e)).Distinct();
        Assert.Single(scores);
    }

    [Fact]
    public void DuplicateDedupeKeysAreDroppedOutright()
    {
        var a = Event(NewsEventKind.RaceWon);
        var b = Event(NewsEventKind.RaceWon, new NewsEventFacts { IsWet = true }); // same key, later duplicate

        var picks = EditorialSelector.SelectRound([a, b]);

        Assert.Single(picks, p => p.Event.Kind == NewsEventKind.RaceWon);
        Assert.False(picks.Single(p => p.Event.Kind == NewsEventKind.RaceWon).Event.Facts.IsWet);
    }

    [Fact]
    public void AQuietWeekendStillPublishesAFewStrongerStories()
    {
        var picks = EditorialSelector.SelectRound(
        [
            Event(NewsEventKind.MidfieldResult),
            Event(NewsEventKind.PointsFinish, subject: "player", round: 3),
            Event(NewsEventKind.LeadChangedHands, subject: "driver.a"),
            Event(NewsEventKind.AiWinStreak, new NewsEventFacts { StreakLength = 2 }, subject: "driver.b"),
        ]);

        Assert.InRange(picks.Count, 3, EditorialSelector.QuietWeekendTarget);
        Assert.DoesNotContain(picks, p => p.Tier == EditorialTier.ArchiveOnly);
    }

    [Fact]
    public void ABlockbusterWeekendIsCappedAndOrderedByScore()
    {
        var events = new List<NewsEvent>
        {
            Event(NewsEventKind.RaceWon, new NewsEventFacts { IsFirstEver = true, ClinchedTitle = true }),
            Event(NewsEventKind.FirstWin, new NewsEventFacts { IsFirstEver = true }),
            Event(NewsEventKind.TitleClinchedEarly, new NewsEventFacts { ClinchedTitle = true }),
            Event(NewsEventKind.ChampionshipLeadTaken),
            Event(NewsEventKind.PolePosition),
            Event(NewsEventKind.WinStreak, new NewsEventFacts { StreakLength = 4 }),
            Event(NewsEventKind.CareerMilestone, new NewsEventFacts { MilestoneValue = 25, MilestoneCounter = "wins" }),
            Event(NewsEventKind.UpsetWinner, subject: "driver.hero"),
            Event(NewsEventKind.AiWinStreak, new NewsEventFacts { StreakLength = 3 }, subject: "driver.b"),
            Event(NewsEventKind.DominantDisplay, subject: "driver.b"),
            Event(NewsEventKind.QualifyingSurprise, new NewsEventFacts { UpsetMagnitude = 6 }),
            Event(NewsEventKind.StandingsClimb),
            Event(NewsEventKind.PointsStreak, new NewsEventFacts { StreakLength = 5 }),
            Event(NewsEventKind.BestFinishImproved),
            Event(NewsEventKind.MidfieldResult, subject: "driver.c"),
            Event(NewsEventKind.LeadChangedHands, subject: "driver.d"),
        };

        var picks = EditorialSelector.SelectRound(events);

        Assert.True(picks.Count <= EditorialSelector.MaxWeekendStories);
        Assert.True(picks.Count >= 8, $"a blockbuster weekend should carry a full package, got {picks.Count}");
        for (var i = 1; i < picks.Count; i++)
        {
            Assert.True(picks[i - 1].Score >= picks[i].Score, "picks must be ordered by score");
        }
        Assert.Equal(EditorialTier.Lead, picks[0].Tier);
    }

    [Fact]
    public void SelectionIsDeterministic()
    {
        var events = CareerNewsEventsTests.RichCareer()
            .SelectMany(s => CareerNewsEvents.Detect([s]))
            .ToList();

        var first = EditorialSelector.SelectRound(events);
        var second = EditorialSelector.SelectRound(events);

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].Event.DedupeKey, second[i].Event.DedupeKey);
            Assert.Equal(first[i].Score, second[i].Score);
        }
    }

    private static NewsEvent Event(
        NewsEventKind kind,
        NewsEventFacts? facts = null,
        string subject = "player",
        int round = 5) => new()
    {
        Kind = kind,
        SeasonOrdinal = 1,
        SeasonYear = 1988,
        Round = round,
        SubjectId = subject,
        Facts = facts ?? new NewsEventFacts(),
    };
}
