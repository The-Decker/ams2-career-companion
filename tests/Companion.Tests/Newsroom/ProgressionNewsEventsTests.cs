using Companion.Core.Newsroom;
using Xunit;

namespace Companion.Tests.Newsroom;

/// <summary>
/// The character-progression triggers of the newsroom event spine (commit 8a0427c): level
/// milestones (25..275) detected monotonically off <see cref="NewsroomRound.PlayerLevelAfter"/> +
/// <see cref="NewsroomSeason.PlayerLevelAtSeasonEnd"/>, the Level 300 cap feature, the
/// injury-comeback story (<see cref="NewsEventKind.ReturnedFromInjury"/> on the first genuine
/// start after DNS rounds), and the campaign-finale retrospective
/// (<see cref="NewsEventKind.CareerCompleted"/>). All display-only detection over shaped facts:
/// deterministic, dedupe-keyed, and never allowed to re-fire a milestone.
/// </summary>
public sealed class ProgressionNewsEventsTests
{
    [Fact]
    public void CrossingOneThresholdEmitsExactlyOneMilestone_AndDetectionIsDeterministic()
    {
        var season = Season(1, 1990,
            Raced(1, levelAfter: 24),   // baseline establishes below the threshold
            Raced(2, levelAfter: 26));  // crosses 25 exactly

        var events = CareerNewsEvents.Detect([season]);

        var milestone = Assert.Single(events, e => e.Kind == NewsEventKind.LevelMilestone);
        Assert.Equal(25, milestone.Facts.MilestoneValue);
        Assert.Equal("level", milestone.Facts.MilestoneCounter);
        Assert.Equal("25", milestone.Discriminator);
        Assert.Equal(2, milestone.Round);
        Assert.Equal("player", milestone.SubjectId);

        // Re-detecting the same shaped seasons yields the identical event set, and every
        // dedupe key in it is unique, the pipeline's stable-identity contract.
        var second = CareerNewsEvents.Detect([season]);
        Assert.Equal(events.Count, second.Count);
        for (var i = 0; i < events.Count; i++)
        {
            Assert.Equal(events[i], second[i]);
        }
        var keys = events.Select(e => e.DedupeKey).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void OneRoundCrossingSeveralThresholdsEmitsOneEventPerThreshold()
    {
        var season = Season(1, 1990,
            Raced(1, levelAfter: 20),
            Raced(2, levelAfter: 80)); // 20 -> 80 crosses 25, 50 and 75 in one award

        var events = CareerNewsEvents.Detect([season]);

        var milestones = events.Where(e => e.Kind == NewsEventKind.LevelMilestone).ToList();
        Assert.Equal([25, 50, 75], milestones.Select(m => m.Facts.MilestoneValue));
        Assert.All(milestones, m => Assert.Equal(2, m.Round));
        // Same kind/round/subject, only the threshold discriminator keeps the keys apart.
        Assert.Equal(3, milestones.Select(m => m.DedupeKey).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void CrossingTheCapEmitsLevel300ReachedExactlyOnce()
    {
        var season = Season(1, 1990,
            Raced(1, levelAfter: 290),  // baseline sweep: fires every 25..275 milestone here
            Raced(2, levelAfter: 300),  // the cap crossing
            Raced(3, levelAfter: 300)); // sitting at the cap says nothing more

        var events = CareerNewsEvents.Detect([season]);

        var capped = Assert.Single(events, e => e.Kind == NewsEventKind.Level300Reached);
        Assert.Equal(2, capped.Round);
        Assert.Equal(300, capped.Facts.MilestoneValue);
        Assert.Equal("level", capped.Facts.MilestoneCounter);

        // The 11 sub-cap milestones all land on the baseline round; the cap round and the
        // at-cap round add none.
        var milestones = events.Where(e => e.Kind == NewsEventKind.LevelMilestone).ToList();
        Assert.Equal(11, milestones.Count);
        Assert.All(milestones, m => Assert.Equal(1, m.Round));
        Assert.DoesNotContain(events, e =>
            e.Round == 3 && e.Kind is NewsEventKind.LevelMilestone or NewsEventKind.Level300Reached);
    }

    [Fact]
    public void SeasonEndCrossingEmitsTheMilestoneAtTheSeasonEndSentinel()
    {
        var season = Season(1, 1990, Raced(1, levelAfter: 48)) with
        {
            Complete = true,
            PlayerLevelAtSeasonEnd = 52, // the season-end awards crossed 50 at the boundary
        };

        var events = CareerNewsEvents.Detect([season]);

        // (The 1 → 48 baseline round also fires the 25 milestone in-round; the boundary story
        // under test is the 50 crossing, which must land on the season-end sentinel.)
        var milestone = Assert.Single(events,
            e => e.Kind == NewsEventKind.LevelMilestone && e.Facts.MilestoneValue == 50);
        Assert.Equal(CareerNewsEvents.SeasonEndRound, milestone.Round);
        Assert.Equal("50", milestone.Discriminator);
    }

    [Fact]
    public void CharacterFreeCareersEmitNoLevelEvents()
    {
        // PlayerLevelAfter stays null throughout (no character): the level detectors stay silent.
        var season = Season(1, 1967, Raced(1), Raced(2), Raced(3)) with { Complete = true };

        var events = CareerNewsEvents.Detect([season]);

        Assert.DoesNotContain(events, e => e.Kind == NewsEventKind.LevelMilestone);
        Assert.DoesNotContain(events, e => e.Kind == NewsEventKind.Level300Reached);
    }

    [Fact]
    public void TheComebackRoundCarriesTheConsecutiveDnsCount_AndEachAbsenceIsItsOwnStory()
    {
        var season = Season(1, 1990,
            Raced(1),
            SatOut(2),
            SatOut(3),
            Raced(4),   // first comeback: 2 rounds missed
            Raced(5),
            SatOut(6),
            Raced(7));  // second comeback: 1 round missed

        var events = CareerNewsEvents.Detect([season]);

        var comebacks = events.Where(e => e.Kind == NewsEventKind.ReturnedFromInjury).ToList();
        Assert.Equal(2, comebacks.Count);

        Assert.Equal(4, comebacks[0].Round);
        Assert.Equal(2, comebacks[0].Facts.MissRaces);
        Assert.Equal(7, comebacks[1].Round);
        Assert.Equal(1, comebacks[1].Facts.MissRaces);

        Assert.NotEqual(comebacks[0].DedupeKey, comebacks[1].DedupeKey);
    }

    [Fact]
    public void ACareerWithNoDnsRoundsHasNoComebackStory()
    {
        var season = Season(1, 1990, Raced(1), Raced(2), Raced(3));

        var events = CareerNewsEvents.Detect([season]);

        Assert.DoesNotContain(events, e => e.Kind == NewsEventKind.ReturnedFromInjury);
        Assert.DoesNotContain(events, e => e.Kind == NewsEventKind.SatOutRound);
    }

    [Fact]
    public void CareerCompletedFiresOnlyForACompleteCampaignFinale()
    {
        // A COMPLETE finale season: the retrospective fires at the season-end sentinel.
        var finale = Season(17, 2006, Raced(1)) with { Complete = true, IsCampaignFinale = true };
        var finaleEvents = CareerNewsEvents.Detect([finale]);
        var completed = Assert.Single(finaleEvents, e => e.Kind == NewsEventKind.CareerCompleted);
        Assert.Equal(CareerNewsEvents.SeasonEndRound, completed.Round);
        Assert.Equal(17, completed.Facts.MilestoneValue);
        Assert.Equal("seasons", completed.Facts.MilestoneCounter);

        // A complete season that is NOT the campaign finale: no retrospective.
        var ordinary = Season(3, 1992, Raced(1)) with { Complete = true };
        Assert.DoesNotContain(CareerNewsEvents.Detect([ordinary]),
            e => e.Kind == NewsEventKind.CareerCompleted);

        // An INCOMPLETE finale season: the campaign is not over yet, so nothing fires.
        var unfinished = Season(17, 2006, Raced(1)) with { Complete = false, IsCampaignFinale = true };
        Assert.DoesNotContain(CareerNewsEvents.Detect([unfinished]),
            e => e.Kind == NewsEventKind.CareerCompleted);
    }

    [Fact]
    public void ALowerLevelEmitsNothingAndNeverResetsFiredMilestones()
    {
        var season = Season(1, 1990,
            Raced(1, levelAfter: 30),  // fires 25
            Raced(2, levelAfter: 20),  // backwards: silent
            Raced(3, levelAfter: 30),  // back to the high-water mark: still silent (25 stays fired)
            Raced(4, levelAfter: 55)); // fires 50 only, 25 must not re-fire

        var events = CareerNewsEvents.Detect([season]);

        var milestones = events.Where(e => e.Kind == NewsEventKind.LevelMilestone).ToList();
        Assert.Equal(2, milestones.Count);
        Assert.Equal(25, milestones[0].Facts.MilestoneValue);
        Assert.Equal(1, milestones[0].Round);
        Assert.Equal(50, milestones[1].Facts.MilestoneValue);
        Assert.Equal(4, milestones[1].Round);
        Assert.DoesNotContain(events, e =>
            e.Kind == NewsEventKind.LevelMilestone && e.Round is 2 or 3);
    }

    // ---------- builders (mirroring CareerNewsEventsTests' season/round shape) ----------

    private static NewsroomSeason Season(int ordinal, int year, params NewsroomRound[] rounds) => new()
    {
        Ordinal = ordinal,
        Year = year,
        ChampionshipRoundCount = Math.Max(rounds.Length, 16),
        PlayerTeamId = "team.default",
        PlayerTeamName = "Team Default",
        Rounds = rounds,
    };

    /// <summary>An ordinary, quiet raced round (finish == expectation, no points) so the level /
    /// comeback triggers under test are the only stories in play.</summary>
    private static NewsroomRound Raced(int round, int? levelAfter = null) => new()
    {
        Round = round,
        Venue = $"Venue {round}",
        PlayerFinish = 6,
        ExpectedFinish = 6,
        PlayerScoredPoints = false,
        PlayerLevelAfter = levelAfter,
    };

    /// <summary>An injury DNS round: the player did not start (auto-simulated absence).</summary>
    private static NewsroomRound SatOut(int round) => new()
    {
        Round = round,
        Venue = $"Venue {round}",
        PlayerDidNotStart = true,
    };
}
