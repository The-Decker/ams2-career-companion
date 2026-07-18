using Companion.Core.Newsroom;
using Xunit;

namespace Companion.Tests.Newsroom;

public class CareerNewsEventsTests
{
    [Fact]
    public void ADebutWinEmitsTheFullFirstWinPackage()
    {
        var season = Season(1, 1988, Round(1, finish: 1, scored: true, winnerIsPlayer: true));

        var events = CareerNewsEvents.Detect([season]);

        Assert.Contains(events, e => e.Kind == NewsEventKind.CareerCreated);
        Assert.Contains(events, e => e.Kind == NewsEventKind.SeasonStarted);
        Assert.Contains(events, e => e.Kind == NewsEventKind.FirstStart);
        var won = Assert.Single(events, e => e.Kind == NewsEventKind.RaceWon);
        Assert.True(won.Facts.IsFirstEver);
        Assert.Contains(events, e => e.Kind == NewsEventKind.FirstWin);
        Assert.Contains(events, e => e.Kind == NewsEventKind.FirstPoints);
        // A debut WIN is the story, the lesser first-podium/top5 angles are folded into it.
        Assert.DoesNotContain(events, e => e.Kind == NewsEventKind.FirstPodium);
        Assert.DoesNotContain(events, e => e.Kind == NewsEventKind.FirstTop5);
    }

    [Fact]
    public void FirstsFireExactlyOnceAcrossSeasons()
    {
        var s1 = Season(1, 1988, Round(1, finish: 2, scored: true), Round(2, finish: 1, scored: true, winnerIsPlayer: true));
        var s2 = Season(2, 1989, Round(1, finish: 1, scored: true, winnerIsPlayer: true));

        var events = CareerNewsEvents.Detect([s1, s2]);

        Assert.Single(events, e => e.Kind == NewsEventKind.FirstPodium);
        Assert.Single(events, e => e.Kind == NewsEventKind.FirstWin);
        Assert.Single(events, e => e.Kind == NewsEventKind.FirstPoints);
        Assert.Single(events, e => e.Kind == NewsEventKind.FirstStart);
        var laterWin = Assert.Single(events, e => e.Kind == NewsEventKind.RaceWon && e.SeasonOrdinal == 2);
        Assert.False(laterWin.Facts.IsFirstEver);
    }

    [Fact]
    public void WinStreaksHitMilestonesAndResetOnDefeat()
    {
        var rounds = new[]
        {
            Round(1, finish: 1, scored: true, winnerIsPlayer: true),
            Round(2, finish: 1, scored: true, winnerIsPlayer: true),
            Round(3, finish: 1, scored: true, winnerIsPlayer: true),
            Round(4, finish: 4, scored: true),
            Round(5, finish: 1, scored: true, winnerIsPlayer: true),
        };

        var events = CareerNewsEvents.Detect([Season(1, 1988, rounds)]);

        var streaks = events.Where(e => e.Kind == NewsEventKind.WinStreak).Select(e => e.Facts.StreakLength).ToList();
        Assert.Equal([2, 3], streaks);
        var lastWin = events.Last(e => e.Kind == NewsEventKind.RaceWon);
        Assert.Equal(1, lastWin.Facts.StreakLength);
    }

    [Fact]
    public void AWinAfterALongDroughtIsItsOwnStory()
    {
        var rounds = new List<NewsroomRound> { Round(1, finish: 1, scored: true, winnerIsPlayer: true) };
        for (var r = 2; r <= 12; r++)
        {
            rounds.Add(Round(r, finish: 8, scored: false));
        }
        rounds.Add(Round(13, finish: 1, scored: true, winnerIsPlayer: true));

        var events = CareerNewsEvents.Detect([Season(1, 1988, [.. rounds])]);

        var drought = Assert.Single(events, e => e.Kind == NewsEventKind.WinDroughtEnded);
        Assert.Equal(11, drought.Facts.DroughtLength);
    }

    [Fact]
    public void DnfCausesSplitIntoMechanicalAndDriverError()
    {
        var rounds = new[]
        {
            Round(1, dnfCause: "mechanical"),
            Round(2, dnfCause: "driverError"),
            Round(3, dnfCause: "mechanical"),
        };

        var events = CareerNewsEvents.Detect([Season(1, 1988, rounds)]);

        Assert.Equal(2, events.Count(e => e.Kind == NewsEventKind.RetiredMechanical));
        Assert.Single(events, e => e.Kind == NewsEventKind.RetiredDriverError);
        Assert.Single(events, e => e.Kind == NewsEventKind.FirstRetirement);
        var streaks = events.Where(e => e.Kind == NewsEventKind.RetirementStreak).Select(e => e.Facts.StreakLength);
        Assert.Equal([2, 3], streaks);
    }

    [Fact]
    public void LeadChangesDistinguishPlayerAngles()
    {
        var rounds = new[]
        {
            Round(1, finish: 1, scored: true, winnerIsPlayer: true, leaderId: "player", leaderPoints: 9, secondPoints: 6),
            Round(2, finish: 5, scored: true, leaderId: "driver.rival", leaderName: "N. Piquet", leaderPoints: 12, secondPoints: 11),
            Round(3, finish: 8, scored: false, leaderId: "driver.other", leaderName: "A. Prost", leaderPoints: 16, secondPoints: 14),
        };

        var events = CareerNewsEvents.Detect([Season(1, 1988, rounds)]);

        var taken = Assert.Single(events, e => e.Kind == NewsEventKind.ChampionshipLeadTaken);
        Assert.True(taken.Facts.IsSeasonOpener);
        var lost = Assert.Single(events, e => e.Kind == NewsEventKind.ChampionshipLeadLost);
        Assert.Equal("N. Piquet", lost.Facts.RivalName);
        var neutral = Assert.Single(events, e => e.Kind == NewsEventKind.LeadChangedHands);
        Assert.Equal("driver.other", neutral.SubjectId);
        Assert.Equal("N. Piquet", neutral.Facts.RivalName);
    }

    [Fact]
    public void TheClinchIsMathematicallySafeAndFiresOnce()
    {
        var rounds = new[]
        {
            // 20 points still available, gap 15, not clinched.
            Round(1, finish: 1, scored: true, winnerIsPlayer: true,
                leaderId: "player", leaderPoints: 45, secondPoints: 30, maxRemaining: 20, maxPerRound: 10),
            // 10 points left, gap 12, clinched by the player.
            Round(2, finish: 2, scored: true,
                leaderId: "player", leaderPoints: 51, secondPoints: 39, maxRemaining: 10, maxPerRound: 10),
            // Still clinched, must not fire again.
            Round(3, finish: 3, scored: true,
                leaderId: "player", leaderPoints: 55, secondPoints: 40, maxRemaining: 0, maxPerRound: 10, final: true),
        };

        var events = CareerNewsEvents.Detect([Season(1, 1988, rounds)]);

        var clinch = Assert.Single(events, e => e.Kind == NewsEventKind.TitleClinchedEarly);
        Assert.Equal("player", clinch.SubjectId);
        Assert.Equal(2, clinch.Round);
        Assert.True(clinch.Facts.ClinchedTitle);
    }

    [Fact]
    public void TitleRaceLostNeedsThePlayerToHaveBeenAContender()
    {
        // Player never ran top-2: mathematical elimination stays quiet.
        var backmarker = Season(1, 1988,
            Round(1, finish: 12, scored: false, playerPosition: 15, leaderId: "driver.a", leaderPoints: 9, secondPoints: 6, maxRemaining: 10, maxPerRound: 10),
            Round(2, finish: 14, scored: false, playerPosition: 16, playerPoints: 0, leaderId: "driver.a", leaderPoints: 18, secondPoints: 12, maxRemaining: 0, maxPerRound: 10, final: true));
        Assert.DoesNotContain(CareerNewsEvents.Detect([backmarker]), e => e.Kind == NewsEventKind.TitleRaceLost);

        // A faded contender gets the story.
        var contender = Season(1, 1988,
            Round(1, finish: 2, scored: true, playerPosition: 2, playerPoints: 6, leaderId: "driver.a", leaderPoints: 9, secondPoints: 6, maxRemaining: 20, maxPerRound: 10),
            Round(2, finish: 10, scored: false, playerPosition: 4, playerPoints: 6, leaderId: "driver.a", leaderPoints: 18, secondPoints: 12, maxRemaining: 10, maxPerRound: 10),
            Round(3, finish: 11, scored: false, playerPosition: 5, playerPoints: 6, leaderId: "driver.a", leaderPoints: 27, secondPoints: 15, maxRemaining: 0, maxPerRound: 10, final: true));
        Assert.Single(CareerNewsEvents.Detect([contender]), e => e.Kind == NewsEventKind.TitleRaceLost);
    }

    [Fact]
    public void AiStoriesCoverStreaksUpsetsAndPoleToFlag()
    {
        var rounds = new[]
        {
            Round(1, finish: 6, scored: true, winnerId: "driver.senna", winnerName: "A. Senna", winnerTier: 5, poleId: "driver.senna"),
            Round(2, finish: 7, scored: false, winnerId: "driver.senna", winnerName: "A. Senna", winnerTier: 5),
            Round(3, finish: 5, scored: true, winnerId: "driver.hero", winnerName: "Local Hero", winnerTier: 2),
        };

        var events = CareerNewsEvents.Detect([Season(1, 1988, rounds)]);

        var streak = Assert.Single(events, e => e.Kind == NewsEventKind.AiWinStreak);
        Assert.Equal(2, streak.Facts.StreakLength);
        Assert.Equal("driver.senna", streak.SubjectId);
        var dominant = Assert.Single(events, e => e.Kind == NewsEventKind.DominantDisplay);
        Assert.Equal(1, dominant.Round);
        var upset = Assert.Single(events, e => e.Kind == NewsEventKind.UpsetWinner);
        Assert.Equal("driver.hero", upset.SubjectId);
    }

    [Fact]
    public void SeasonNotesBecomeSeasonEndEvents()
    {
        var season = Season(1, 1988, Round(1, finish: 5, scored: true)) with
        {
            Complete = true,
            ChampionId = "driver.senna",
            ChampionName = "A. Senna",
            SeasonNotes =
            [
                new NewsroomSeasonNote { Kind = NewsroomSeasonNoteKind.TeamRelegated, SubjectId = "team.minor", SubjectName = "Minor GP" },
                new NewsroomSeasonNote { Kind = NewsroomSeasonNoteKind.RetirementConsidered, SubjectId = "driver.vet", SubjectName = "Old Hand", Value = 39 },
                new NewsroomSeasonNote { Kind = NewsroomSeasonNoteKind.OfferReceived, SubjectName = "Big Team", Value = 4 },
            ],
        };

        var events = CareerNewsEvents.Detect([season]);

        Assert.Contains(events, e => e.Kind == NewsEventKind.TeamRelegated && e.SubjectId == "team.minor" && e.Round == CareerNewsEvents.SeasonEndRound);
        Assert.Contains(events, e => e.Kind == NewsEventKind.RetirementConsidered && e.Facts.MilestoneValue == 39);
        Assert.Contains(events, e => e.Kind == NewsEventKind.OfferReceived && e.SubjectId == "player");
        Assert.Contains(events, e => e.Kind == NewsEventKind.SeasonCompleted);
        var crowned = Assert.Single(events, e => e.Kind == NewsEventKind.ChampionCrowned);
        Assert.Equal("driver.senna", crowned.SubjectId);
        Assert.False(crowned.Facts.ClinchedTitle);
    }

    [Fact]
    public void APlayerChampionshipCrownsThePlayer()
    {
        var season = Season(1, 1988, Round(1, finish: 1, scored: true, winnerIsPlayer: true)) with
        {
            Complete = true,
            ChampionId = "driver.player-entrant",
            ChampionName = "You",
            PlayerChampion = true,
        };

        var crowned = Assert.Single(CareerNewsEvents.Detect([season]), e => e.Kind == NewsEventKind.ChampionCrowned);
        Assert.Equal("player", crowned.SubjectId);
        Assert.True(crowned.Facts.ClinchedTitle);
    }

    [Fact]
    public void DeathEndsTheCoverage()
    {
        var s1 = Season(1, 1988,
            Round(1, finish: 5, scored: true),
            Round(2, dnfCause: "driverError", accidentOutcome: "death"),
            Round(3, finish: 1, scored: true, winnerIsPlayer: true));
        var s2 = Season(2, 1989, Round(1, finish: 1, scored: true, winnerIsPlayer: true));

        var events = CareerNewsEvents.Detect([s1, s2]);

        Assert.Single(events, e => e.Kind == NewsEventKind.PlayerDied);
        Assert.DoesNotContain(events, e => e.Round == 3 && e.SeasonOrdinal == 1);
        Assert.DoesNotContain(events, e => e.SeasonOrdinal == 2);
    }

    [Fact]
    public void TeamChangesBetweenSeasonsAreDetected()
    {
        var s1 = Season(1, 1988, Round(1, finish: 5, scored: true)) with { PlayerTeamId = "team.a", PlayerTeamName = "Alpha" };
        var s2 = Season(2, 1989, Round(1, finish: 5, scored: true)) with { PlayerTeamId = "team.b", PlayerTeamName = "Beta" };

        var moved = Assert.Single(CareerNewsEvents.Detect([s1, s2]), e => e.Kind == NewsEventKind.PlayerTeamChanged);
        Assert.Equal(2, moved.SeasonOrdinal);
        Assert.Equal("Alpha", moved.Facts.RivalName);
        Assert.Equal("Beta", moved.SubjectTeamName);
    }

    [Fact]
    public void DetectionIsDeterministicAndKeysAreUnique()
    {
        var seasons = RichCareer();

        var first = CareerNewsEvents.Detect(seasons);
        var second = CareerNewsEvents.Detect(seasons);

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i], second[i]);
        }

        var keys = first.Select(e => e.DedupeKey).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void MilestonesLandingTogetherKeepDistinctKeys()
    {
        // 25th start and 5th win on the same afternoon: two milestone stories, two keys.
        var rounds = new List<NewsroomRound>();
        for (var r = 1; r <= 24; r++)
        {
            var win = r <= 4;
            rounds.Add(Round(r, finish: win ? 1 : 6, scored: win, winnerIsPlayer: win));
        }
        rounds.Add(Round(25, finish: 1, scored: true, winnerIsPlayer: true));

        var events = CareerNewsEvents.Detect([Season(1, 1988, [.. rounds]) with { ChampionshipRoundCount = 25 }]);

        var milestones = events.Where(e => e.Kind == NewsEventKind.CareerMilestone && e.Round == 25).ToList();
        Assert.Equal(2, milestones.Count);
        Assert.Equal(2, milestones.Select(m => m.DedupeKey).Distinct().Count());
        Assert.Contains(milestones, m => m.Facts.MilestoneCounter == "starts" && m.Facts.MilestoneValue == 25);
        Assert.Contains(milestones, m => m.Facts.MilestoneCounter == "wins" && m.Facts.MilestoneValue == 5);
    }

    internal static NewsroomSeason Season(int ordinal, int year, params NewsroomRound[] rounds) => new()
    {
        Ordinal = ordinal,
        Year = year,
        ChampionshipRoundCount = Math.Max(rounds.Length, 16),
        PlayerTeamId = "team.default",
        PlayerTeamName = "Team Default",
        Rounds = rounds,
    };

    internal static NewsroomRound Round(
        int round,
        int? finish = null,
        string dnfCause = "",
        bool scored = false,
        bool winnerIsPlayer = false,
        string winnerId = "",
        string winnerName = "",
        int winnerTier = 0,
        string poleId = "",
        string leaderId = "",
        string leaderName = "",
        double leaderPoints = 0,
        double secondPoints = 0,
        int? playerPosition = null,
        double playerPoints = 0,
        double maxRemaining = 0,
        double maxPerRound = 9,
        bool final = false,
        string accidentOutcome = "") => new()
    {
        Round = round,
        Venue = $"Venue {round}",
        IsFinalChampionshipRound = final,
        PlayerFinish = finish,
        PlayerDnfCause = dnfCause,
        PlayerScoredPoints = scored,
        ExpectedFinish = 6,
        WinnerId = winnerIsPlayer ? "player" : winnerId,
        WinnerName = winnerIsPlayer ? "You" : winnerName,
        WinnerTeamTier = winnerTier,
        PoleDriverId = poleId,
        LeaderId = leaderId,
        LeaderName = leaderName,
        LeaderPoints = leaderPoints,
        SecondPoints = secondPoints,
        PlayerPosition = playerPosition,
        PlayerPoints = playerPoints,
        MaxRemainingPoints = maxRemaining,
        MaxPointsPerRound = maxPerRound,
        AccidentOutcome = accidentOutcome,
        AccidentMissRaces = accidentOutcome == "minorInjury" ? 1 : 0,
    };

    internal static IReadOnlyList<NewsroomSeason> RichCareer()
    {
        var s1 = Season(1, 1988,
            Round(1, finish: 4, scored: true, winnerId: "driver.senna", winnerName: "A. Senna", winnerTier: 5, poleId: "driver.senna",
                leaderId: "driver.senna", leaderName: "A. Senna", leaderPoints: 9, secondPoints: 6, maxRemaining: 27, maxPerRound: 9),
            Round(2, finish: 1, scored: true, winnerIsPlayer: true,
                leaderId: "player", leaderPoints: 15, secondPoints: 12, maxRemaining: 18, maxPerRound: 9),
            Round(3, dnfCause: "mechanical",
                leaderId: "driver.senna", leaderName: "A. Senna", leaderPoints: 21, secondPoints: 15, maxRemaining: 9, maxPerRound: 9),
            Round(4, finish: 2, scored: true, playerPosition: 2, playerPoints: 21,
                winnerId: "driver.hero", winnerName: "Local Hero", winnerTier: 2,
                leaderId: "driver.senna", leaderName: "A. Senna", leaderPoints: 24, secondPoints: 21,
                maxRemaining: 0, maxPerRound: 9, final: true))
            with
        { Complete = true, ChampionId = "driver.senna", ChampionName = "A. Senna", ChampionshipRoundCount = 4 };

        var s2 = Season(2, 1989,
            Round(1, finish: 1, scored: true, winnerIsPlayer: true, poleId: "player",
                leaderId: "player", leaderPoints: 9, secondPoints: 6, maxRemaining: 9, maxPerRound: 9),
            Round(2, finish: 3, scored: true, playerPosition: 1, playerPoints: 13,
                leaderId: "player", leaderPoints: 13, secondPoints: 9, maxRemaining: 0, maxPerRound: 9, final: true))
            with
        { Complete = true, ChampionId = "driver.player-entrant", ChampionName = "You", PlayerChampion = true, ChampionshipRoundCount = 2, PlayerTeamId = "team.big", PlayerTeamName = "Big Team" };

        return [s1, s2];
    }
}
