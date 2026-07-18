namespace Companion.Core.Smgp;

/// <summary>One driver's line in the championship order after a round, the minimum the world-story detector
/// needs. Shaped by the ViewModels from the folded standings; the detector is pure.</summary>
public sealed record SmgpWorldStanding
{
    /// <summary>1-based championship position after the round.</summary>
    public required int Position { get; init; }
    public required string DriverId { get; init; }
    public required string Name { get; init; }
    public required int Points { get; init; }
    public string TeamId { get; init; } = "";
}

/// <summary>One round's grid-level facts for <see cref="SmgpWorldStories"/>: the race winner and the
/// championship order after the round. Ordered by (season, round). A pure DATA carrier, the ViewModels
/// build it from the folded results + standings snapshots.</summary>
public sealed record SmgpWorldRound
{
    /// <summary>1-based campaign season ordinal, a change marks a season boundary (streaks/leader reset).</summary>
    public required int Season { get; init; }

    /// <summary>The pack round number (fills the dispatch's round sort key + venue label).</summary>
    public required int Round { get; init; }

    public required string Venue { get; init; }

    /// <summary>1-based index of this round WITHIN its season, and the season's total scored rounds, used
    /// to gate "title tightens" to the latter half of a campaign. When 0/0 the gate treats the round as
    /// eligible (a thin fixture still exercises the rule).</summary>
    public int RoundIndex { get; init; }
    public int SeasonRounds { get; init; }

    /// <summary>The race winner (a classified P1), or null when nobody was classified P1.</summary>
    public string? WinnerId { get; init; }
    public string WinnerName { get; init; } = "";
    public string WinnerTeamId { get; init; } = "";

    /// <summary>The championship order after the round, position ascending (P1 first). Only the top few are
    /// read, but the full list is accepted.</summary>
    public required IReadOnlyList<SmgpWorldStanding> Standings { get; init; }
}

/// <summary>The kind of AI-world story the detector emits.</summary>
public enum SmgpWorldStoryKind
{
    /// <summary>A driver on a run of consecutive race wins (2, 3, 4, 5 in a row).</summary>
    RivalStreak,

    /// <summary>The benchmark (A. Senna / the Madonna #1) reasserting dominance, a season's second win.</summary>
    Benchmark,

    /// <summary>The championship lead changing hands.</summary>
    LeaderChange,

    /// <summary>The title race tightening late in a season (a small gap between P1 and P2).</summary>
    TitleTightens,

    /// <summary>A change at the front of the second-place battle ("X takes P2 off Y").</summary>
    StandingsMove,
}

/// <summary>One detected AI-world story with the structured facts a dispatch renders from. DISPLAY-ONLY.</summary>
public sealed record SmgpWorldStory
{
    public required SmgpWorldStoryKind Kind { get; init; }
    public required int Season { get; init; }
    public required int Round { get; init; }
    public required string Venue { get; init; }

    /// <summary>The story's subject driver (the streaking / newly-leading / newly-P2 driver, or the
    /// benchmark). Empty when the story has no single subject.</summary>
    public string SubjectId { get; init; } = "";
    public string SubjectName { get; init; } = "";
    public string SubjectTeamId { get; init; } = "";

    /// <summary>The other party's name (the dethroned leader, the displaced P2, the title-race pursued).</summary>
    public string OtherName { get; init; } = "";

    /// <summary>A number the story carries (the streak length, the points gap, the position). 0 when none.</summary>
    public int Number { get; init; }
}

/// <summary>
/// Detects the reactive AI-WORLD stories from the folded per-round grid facts, a rival's win streak, the
/// benchmark reasserting itself, the championship lead changing, the title race tightening, and the
/// second-place battle turning over. Pure and deterministic: it walks the rounds in order, resetting its
/// per-season memory at each season boundary, and emits each story once at the round it occurs. The
/// PLAYER's own results are told through the milestone/race dispatches, so the player is excluded here as a
/// story subject (win streaks, benchmark) but still appears as the "other" party of a leader/P2 change, the
/// grid reacting TO the player is exactly the point. A pure projection over immutable results: never a fold
/// input.
/// </summary>
public static class SmgpWorldStories
{
    /// <summary>The consecutive-win streak lengths worth a story, a dominant run gets a few escalating
    /// dispatches (2 → 3 → 4 → 5), then goes quiet.</summary>
    private static readonly int[] StreakMilestones = [2, 3, 4, 5];

    /// <summary>The championship points gap at or below which the late-season title race is "tightening".</summary>
    public const int TitleTightGap = 8;

    public static IReadOnlyList<SmgpWorldStory> Detect(
        IReadOnlyList<SmgpWorldRound> rounds, string playerId, string benchmarkId)
    {
        var stories = new List<SmgpWorldStory>();

        int curSeason = int.MinValue;
        string? prevWinner = null;      // consecutive-win tracking within a season
        int winStreak = 0;
        string? prevLeader = null;      // championship P1 last round
        string? prevSecond = null;      // championship P2 last round
        int benchmarkWins = 0;          // benchmark's wins so far this season
        bool benchmarkTold = false;     // the once-per-season benchmark story fired
        bool titleTold = false;         // the once-per-season "title tightens" story fired

        foreach (var r in rounds)
        {
            if (r.Season != curSeason)
            {
                // New season, the ladder, the standings and every streak start fresh.
                curSeason = r.Season;
                prevWinner = null;
                winStreak = 0;
                prevLeader = null;
                prevSecond = null;
                benchmarkWins = 0;
                benchmarkTold = false;
                titleTold = false;
            }

            // --- Consecutive-win streaks + the benchmark (from the race winner) ---
            if (r.WinnerId is { Length: > 0 } winnerId)
            {
                winStreak = string.Equals(winnerId, prevWinner, StringComparison.Ordinal) ? winStreak + 1 : 1;
                prevWinner = winnerId;

                bool isPlayer = string.Equals(winnerId, playerId, StringComparison.Ordinal);
                if (!isPlayer && StreakMilestones.Contains(winStreak))
                    stories.Add(new SmgpWorldStory
                    {
                        Kind = SmgpWorldStoryKind.RivalStreak,
                        Season = r.Season, Round = r.Round, Venue = r.Venue,
                        SubjectId = winnerId, SubjectName = r.WinnerName, SubjectTeamId = r.WinnerTeamId,
                        Number = winStreak,
                    });

                if (!isPlayer && benchmarkId.Length > 0
                    && string.Equals(winnerId, benchmarkId, StringComparison.Ordinal))
                {
                    benchmarkWins++;
                    // The benchmark reasserting itself, the second win of a season, once (the first win
                    // reads as a normal result; two says "still untouchable").
                    if (benchmarkWins == 2 && !benchmarkTold)
                    {
                        benchmarkTold = true;
                        stories.Add(new SmgpWorldStory
                        {
                            Kind = SmgpWorldStoryKind.Benchmark,
                            Season = r.Season, Round = r.Round, Venue = r.Venue,
                            SubjectId = winnerId, SubjectName = r.WinnerName, SubjectTeamId = r.WinnerTeamId,
                            Number = benchmarkWins,
                        });
                    }
                }
            }
            else
            {
                winStreak = 0;
                prevWinner = null;
            }

            // --- Championship order stories (leader change, P2 turnover, title tightening) ---
            var leader = r.Standings.FirstOrDefault(s => s.Position == 1);
            var second = r.Standings.FirstOrDefault(s => s.Position == 2);

            if (leader is not null)
            {
                // The player as the SUBJECT of an active-verb story ("YOU takes the lead") reads wrong (their
                // display name is a pronoun); their rise is told through the milestone feed. They still appear
                // as the dethroned OTHER party ("X takes the lead off you"), which the grid reacting to the
                // player is the whole point.
                bool leaderIsPlayer = string.Equals(leader.DriverId, playerId, StringComparison.Ordinal);
                if (prevLeader is not null && !leaderIsPlayer
                    && !string.Equals(leader.DriverId, prevLeader, StringComparison.Ordinal))
                {
                    string oldName = r.Standings.FirstOrDefault(s =>
                        string.Equals(s.DriverId, prevLeader, StringComparison.Ordinal))?.Name ?? "";
                    stories.Add(new SmgpWorldStory
                    {
                        Kind = SmgpWorldStoryKind.LeaderChange,
                        Season = r.Season, Round = r.Round, Venue = r.Venue,
                        SubjectId = leader.DriverId, SubjectName = leader.Name, SubjectTeamId = leader.TeamId,
                        OtherName = oldName, Number = 1,
                    });
                }
                prevLeader = leader.DriverId;
            }

            if (second is not null)
            {
                bool secondIsPlayer = string.Equals(second.DriverId, playerId, StringComparison.Ordinal);
                if (prevSecond is not null && !secondIsPlayer
                    && !string.Equals(second.DriverId, prevSecond, StringComparison.Ordinal))
                {
                    string displaced = r.Standings.FirstOrDefault(s =>
                        string.Equals(s.DriverId, prevSecond, StringComparison.Ordinal))?.Name ?? "";
                    stories.Add(new SmgpWorldStory
                    {
                        Kind = SmgpWorldStoryKind.StandingsMove,
                        Season = r.Season, Round = r.Round, Venue = r.Venue,
                        SubjectId = second.DriverId, SubjectName = second.Name, SubjectTeamId = second.TeamId,
                        OtherName = displaced, Number = 2,
                    });
                }
                prevSecond = second.DriverId;
            }

            // Title tightening, latter half of the season, a small gap, once per season.
            bool latterHalf = r.SeasonRounds <= 0 || r.RoundIndex * 2 >= r.SeasonRounds;
            if (!titleTold && latterHalf && leader is not null && second is not null)
            {
                int gap = leader.Points - second.Points;
                if (gap >= 0 && gap <= TitleTightGap)
                {
                    titleTold = true;
                    stories.Add(new SmgpWorldStory
                    {
                        Kind = SmgpWorldStoryKind.TitleTightens,
                        Season = r.Season, Round = r.Round, Venue = r.Venue,
                        SubjectId = leader.DriverId, SubjectName = leader.Name, SubjectTeamId = leader.TeamId,
                        OtherName = second.Name, Number = gap,
                    });
                }
            }
        }

        return stories;
    }
}
