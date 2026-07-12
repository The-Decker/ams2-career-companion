using Companion.Core.Smgp;

namespace Companion.Tests.Career;

/// <summary>
/// The pure AI-world story detector (Task 4): given per-round grid facts (the race winner + the championship
/// order after the round), it emits the reactive grid stories — a rival's win streak, the benchmark
/// reasserting itself, the championship lead changing, the second-place battle turning over, and the title
/// race tightening. These pin the detection + dedup + season-reset + player-exclusion rules the ViewModels
/// feed real folded state into. Pure and deterministic — never a fold input.
/// </summary>
public sealed class SmgpWorldStoriesTests
{
    private const string Player = "driver.player";
    private const string Senna = "driver.senna"; // the benchmark

    private static SmgpWorldStanding S(int pos, string id, int pts) =>
        new() { Position = pos, DriverId = id, Name = id, Points = pts, TeamId = id + ".team" };

    private static SmgpWorldRound Round(
        int season, int round, string venue, string? winner,
        IReadOnlyList<SmgpWorldStanding> standings, int roundIndex = 0, int seasonRounds = 0) => new()
    {
        Season = season, Round = round, Venue = venue,
        RoundIndex = roundIndex, SeasonRounds = seasonRounds,
        WinnerId = winner, WinnerName = winner ?? "", WinnerTeamId = winner is null ? "" : winner + ".team",
        Standings = standings,
    };

    private static IReadOnlyList<SmgpWorldStory> Detect(params SmgpWorldRound[] rounds) =>
        SmgpWorldStories.Detect(rounds, Player, Senna);

    [Fact]
    public void No_rounds_yields_no_stories() =>
        Assert.Empty(SmgpWorldStories.Detect([], Player, Senna));

    [Fact]
    public void A_rivals_second_straight_win_fires_a_streak_story_but_the_first_does_not()
    {
        var stories = Detect(
            Round(1, 1, "Monaco", "driver.a", [S(1, "driver.a", 9), S(2, Player, 6)]),
            Round(1, 2, "Spa", "driver.a", [S(1, "driver.a", 18), S(2, Player, 12)]));

        var streaks = stories.Where(s => s.Kind == SmgpWorldStoryKind.RivalStreak).ToList();
        Assert.Single(streaks);                 // only the 2-in-a-row, not the lone first win
        Assert.Equal(2, streaks[0].Number);
        Assert.Equal("driver.a", streaks[0].SubjectId);
        Assert.Equal("Spa", streaks[0].Venue);
    }

    [Fact]
    public void Streak_escalates_at_each_milestone_then_a_new_winner_resets_it()
    {
        var stories = Detect(
            Round(1, 1, "R1", "driver.a", [S(1, "driver.a", 9)]),
            Round(1, 2, "R2", "driver.a", [S(1, "driver.a", 18)]),   // streak 2
            Round(1, 3, "R3", "driver.a", [S(1, "driver.a", 27)]),   // streak 3
            Round(1, 4, "R4", "driver.b", [S(1, "driver.a", 33)]),   // resets
            Round(1, 5, "R5", "driver.b", [S(1, "driver.a", 39)]));  // driver.b streak 2

        var streaks = stories.Where(s => s.Kind == SmgpWorldStoryKind.RivalStreak).ToList();
        Assert.Equal([2, 3, 2], streaks.Select(s => s.Number));
        Assert.Equal(["driver.a", "driver.a", "driver.b"], streaks.Select(s => s.SubjectId));
    }

    [Fact]
    public void The_player_on_a_run_gets_no_rival_streak_story()
    {
        var stories = Detect(
            Round(1, 1, "R1", Player, [S(1, Player, 9)]),
            Round(1, 2, "R2", Player, [S(1, Player, 18)]),
            Round(1, 3, "R3", Player, [S(1, Player, 27)]));

        Assert.DoesNotContain(stories, s => s.Kind == SmgpWorldStoryKind.RivalStreak);
    }

    [Fact]
    public void The_benchmarks_second_win_of_a_season_fires_once()
    {
        var stories = Detect(
            Round(1, 1, "R1", Senna, [S(1, Senna, 9)]),
            Round(1, 2, "R2", "driver.b", [S(1, Senna, 15)]),
            Round(1, 3, "R3", Senna, [S(1, Senna, 24)]),   // Senna's 2nd win -> benchmark
            Round(1, 4, "R4", Senna, [S(1, Senna, 33)]));  // 3rd win, no second benchmark story

        var bench = stories.Where(s => s.Kind == SmgpWorldStoryKind.Benchmark).ToList();
        Assert.Single(bench);
        Assert.Equal(Senna, bench[0].SubjectId);
        Assert.Equal("R3", bench[0].Venue);
    }

    [Fact]
    public void A_change_at_the_top_of_the_standings_fires_a_leader_change()
    {
        var stories = Detect(
            Round(1, 1, "R1", "driver.a", [S(1, "driver.a", 9), S(2, Player, 6)]),
            Round(1, 2, "R2", Player, [S(1, Player, 15), S(2, "driver.a", 12)]));  // player takes the lead

        var lead = Assert.Single(stories, s => s.Kind == SmgpWorldStoryKind.LeaderChange);
        Assert.Equal(Player, lead.SubjectId);
        Assert.Equal("driver.a", lead.OtherName);   // dethroned leader named
    }

    [Fact]
    public void A_turnover_of_second_place_fires_a_standings_move()
    {
        var stories = Detect(
            Round(1, 1, "R1", "driver.a", [S(1, "driver.a", 9), S(2, "driver.b", 6), S(3, "driver.c", 4)]),
            Round(1, 2, "R2", "driver.a", [S(1, "driver.a", 18), S(2, "driver.c", 12), S(3, "driver.b", 10)]));

        var move = Assert.Single(stories, s => s.Kind == SmgpWorldStoryKind.StandingsMove);
        Assert.Equal("driver.c", move.SubjectId);   // climbed into P2
        Assert.Equal("driver.b", move.OtherName);   // displaced
    }

    [Fact]
    public void The_title_race_tightens_only_in_the_latter_half_and_only_once()
    {
        var stories = Detect(
            // First half: a small gap does NOT fire (round 1 of 4).
            Round(1, 1, "R1", "driver.a", [S(1, "driver.a", 9), S(2, "driver.b", 6)], roundIndex: 1, seasonRounds: 4),
            // Latter half: gap of 3 <= 8 fires once...
            Round(1, 3, "R3", "driver.b", [S(1, "driver.a", 27), S(2, "driver.b", 24)], roundIndex: 3, seasonRounds: 4),
            // ...and does not fire again later the same season.
            Round(1, 4, "R4", "driver.b", [S(1, "driver.a", 33), S(2, "driver.b", 30)], roundIndex: 4, seasonRounds: 4));

        var tight = Assert.Single(stories, s => s.Kind == SmgpWorldStoryKind.TitleTightens);
        Assert.Equal("R3", tight.Venue);
        Assert.Equal(3, tight.Number);              // the gap
    }

    [Fact]
    public void A_wide_late_gap_does_not_tighten_the_title()
    {
        var stories = Detect(
            Round(1, 4, "R4", "driver.a",
                [S(1, "driver.a", 60), S(2, "driver.b", 20)], roundIndex: 4, seasonRounds: 4));

        Assert.DoesNotContain(stories, s => s.Kind == SmgpWorldStoryKind.TitleTightens);
    }

    [Fact]
    public void A_new_season_resets_streaks_and_the_leader_baseline()
    {
        var stories = Detect(
            Round(1, 1, "R1", "driver.a", [S(1, "driver.a", 9)]),
            Round(1, 2, "R2", "driver.a", [S(1, "driver.a", 18)]),  // season 1 streak 2
            // Season 2 opens: the SAME winner should NOT already be "2 in a row", and its round-1 leader
            // is a fresh baseline (no leader-change fired for the very first standings of the new season).
            Round(2, 1, "R1", "driver.a", [S(1, "driver.a", 9), S(2, Player, 6)]),
            Round(2, 2, "R2", "driver.a", [S(1, "driver.a", 18), S(2, Player, 12)]));

        var streaks = stories.Where(s => s.Kind == SmgpWorldStoryKind.RivalStreak).ToList();
        // One streak in each season (the 2-in-a-row), not a carried-over 3+.
        Assert.Equal(2, streaks.Count);
        Assert.All(streaks, s => Assert.Equal(2, s.Number));
        // No leader-change fired for a season's opening round (nothing to change FROM).
        Assert.DoesNotContain(stories, s => s.Kind == SmgpWorldStoryKind.LeaderChange);
    }
}
