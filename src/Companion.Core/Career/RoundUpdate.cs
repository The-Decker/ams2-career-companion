using Companion.Core.Character;
using Companion.Core.Determinism;
using Companion.Core.Grid;

namespace Companion.Core.Career;

/// <summary>Everything the per-round player update consumes.</summary>
public sealed record RoundUpdateContext
{
    /// <summary>The round's resolved grid; must contain the player seat.</summary>
    public required GridPlan Grid { get; init; }

    public required PlayerCareerState Player { get; init; }

    /// <summary>Budget tier of the player's team this round (1–5).</summary>
    public required int PlayerTeamTier { get; init; }

    /// <summary>
    /// Folded start-of-season team tiers keyed by team id. Expectation model v2 uses this only as a
    /// fallback when the resolved grid has no authored car-performance hierarchy.
    /// </summary>
    public IReadOnlyDictionary<string, int>? TeamTiers { get; init; }

    /// <summary>Classified finishing position; null on DNF (then <see cref="PlayerDnf"/> is required).</summary>
    public int? PlayerFinish { get; init; }

    public DnfCause? PlayerDnf { get; init; }

    /// <summary>True when the player has at least one teammate on this grid.</summary>
    public required bool HasTeammate { get; init; }

    /// <summary>Best classified finish among the player's teammates; null when none classified.</summary>
    public int? TeammateFinish { get; init; }

    /// <summary>The Opponent Skill slider the round was actually driven at, in percent.</summary>
    public required double SliderUsed { get; init; }

    /// <summary>How many positions score points this round, from the round's RESOLVED scoring
    /// definition (alternate shortened-race tables included) — drives the "points" race cause.
    /// Never a hard-coded top-6.</summary>
    public required int PointsPositions { get; init; }

    public required StreamFactory Streams { get; init; }

    /// <summary>When present, one race headline is selected and journaled.</summary>
    public HeadlineBank? Headlines { get; init; }

    /// <summary>Display name for {player} headline tokens; defaults to the player seat's name.</summary>
    public string? PlayerName { get; init; }

    /// <summary>The player's qualifying position this round (1-based), when the round ran a
    /// qualifying session; null on single-race rounds → no qualifying anchor and no event, so
    /// single-race careers emit the identical journal sequence. (Increment 2.)</summary>
    public int? PlayerQualifyingPosition { get; init; }

    /// <summary>The player's resolved character modifier, or null for a pre-character career — then
    /// every OPI/reputation/pace-anchor call takes its exact shipped path and the round is
    /// byte-identical. Built once by the fold from the folded character. (Increment 4a.)</summary>
    public PlayerPerkModifiers? Modifiers { get; init; }

    /// <summary>The character rules (perks.json) when this career carries a character — supplies the
    /// XP curve + sources for the <c>player.xp</c> row. Null for a pre-character career (no XP row,
    /// journal sequence unchanged). (Increment 4a.)</summary>
    public CharacterRules? CharacterRules { get; init; }

    /// <summary>The per-error injury contribution this race banks toward the season injury load — the
    /// driverErrorDnf-gated <c>perErrorAdd</c> isolated from the unconditional base, so it stacks
    /// exactly once per driver-error DNF. 0 on a clean round or a character without a perErrorAdd
    /// injury perk ⇒ the folded player state is byte-identical. (Task #18.)</summary>
    public double InjuryLoadDelta { get; init; }

    /// <summary>Whether this event belongs to the championship domain. Version-2 XP uses the
    /// pinned championship-round population; v0/v1 ignore this gate.</summary>
    public required bool IsChampionshipRound { get; init; }

    /// <summary>True only for the first race session in a weekend. The v2 denominator is authored
    /// per championship round, so secondary races remain fold-visible but award no v2 XP.</summary>
    public required bool IsPrimaryRace { get; init; }

    /// <summary>The Setup Gamble the player called before the race — the finishing position they bet
    /// on (1-based), or null for no bet. Resolved against the actual finish + the sim's expected
    /// finish only when it is a real gamble (called better than expected); otherwise a no-op, so a
    /// round without a gamble is byte-identical. (Setup Gamble, 4b.)</summary>
    public int? CalledShot { get; init; }
}

public sealed record RoundUpdateResult
{
    public required PlayerCareerState Player { get; init; }

    /// <summary>Ordered journal events: race.result, player.opi, player.reputation,
    /// player.paceAnchor, then news.headline when a bank is present.</summary>
    public required IReadOnlyList<JournalEvent> Events { get; init; }

    /// <summary>Recommended Opponent Skill slider for the NEXT round (70–120, whole percent).
    /// Shown in the briefing, never auto-applied.</summary>
    public required int RecommendedSlider { get; init; }

    public required int ExpectedFinish { get; init; }

    public string? Headline { get; init; }
}

/// <summary>
/// Pure per-round career update for the player: OPI, reputation, pace anchor, difficulty
/// recommendation, and the round's single headline — all journaled.
/// </summary>
public static class RoundUpdate
{
    public static RoundUpdateResult Apply(RoundUpdateContext context)
    {
        var grid = context.Grid;
        var player = context.Player;
        int gridSize = grid.Seats.Count;
        int playerIndex = SeatStrengthModel.PlayerSeatIndex(grid);

        var mods = context.Modifiers;
        int expectationModelVersion = player.Character?.ExpectationModelVersion ?? 0;
        int expected = SeatStrengthModel.ExpectedFinish(
            grid, playerIndex, player.Opi, expectationModelVersion, context.TeamTiers);
        double effective = OpiMath.EffectiveFinish(expected, context.PlayerFinish, context.PlayerDnf, gridSize, mods);

        double newOpi = OpiMath.Update(player.Opi, expected, effective, mods);

        bool beatTeammate = context.HasTeammate
                            && context.PlayerFinish is { } finish
                            && (context.TeammateFinish is null || finish < context.TeammateFinish);
        double repDelta = ReputationMath.RoundDelta(
            expected, effective, context.PlayerFinish, beatTeammate, context.PlayerTeamTier, mods);
        double repAfterRound = ReputationMath.Apply(player.Reputation, repDelta);

        // Setup Gamble (called shot, 4b): if the player called a finish better than the sim expected,
        // resolve the bet against the actual finish — a hit banks the stake in reputation (chained on
        // top of the round move), a miss costs it. Pure function of the journaled call, so it
        // re-simulates exactly; no call (or a non-gamble call) leaves rep untouched → byte-identical.
        bool calledShotResolved = context.CalledShot is { } cs && CalledShotMath.IsGamble(cs, expected);
        double calledShotRepDelta = calledShotResolved
            ? CalledShotMath.ReputationDelta(context.CalledShot!.Value, context.PlayerFinish, expected)
            : 0.0;
        int calledShotXpBonus = calledShotResolved
            ? CalledShotMath.XpBonus(context.CalledShot!.Value, context.PlayerFinish, expected)
            : 0;
        double newRep = ReputationMath.Apply(repAfterRound, calledShotRepDelta);

        // The anchor calibrates only on classified finishes: a DNF carries no pace signal.
        double newAnchor = player.PaceAnchor;
        if (context.PlayerFinish is { } classified)
        {
            double implied = PaceAnchorMath.ImpliedPlayerPace(grid, classified, context.SliderUsed);
            newAnchor = PaceAnchorMath.Update(player.PaceAnchor, implied, mods);
        }

        // Qualifying (one-lap) anchor: calibrates only on rounds that ran qualifying (Inc 2).
        double newQualiAnchor = player.QualifyingAnchor;
        if (context.PlayerQualifyingPosition is { } qualiPos)
        {
            double impliedQuali = PaceAnchorMath.ImpliedPlayerQualiPace(grid, qualiPos, context.SliderUsed);
            newQualiAnchor = PaceAnchorMath.Update(player.QualifyingAnchor, impliedQuali, mods);
        }

        int recommendedSlider = newAnchor > 0.0
            ? DifficultyModel.RecommendSlider(newAnchor, PaceAnchorMath.MedianAiRaceSkill(grid))
            : (int)Math.Round(context.SliderUsed, MidpointRounding.AwayFromZero);

        string cause = RaceCause(context, expected, effective);

        // Character XP (Increment 4a): a pure function of this round's result. Accrued and journaled
        // only for a character career — a pre-character career emits no player.xp row, so its journal
        // sequence is unchanged.
        long newXp = player.Xp;
        int newLevel = player.Level;
        long newXpScaleRemainder = player.XpScaleRemainder;
        JournalEvent? xpEvent = null;
        if (player.Character is not null && context.CharacterRules is { } charRules)
        {
            int xpRound = XpMath.PerRound(charRules.Levels.XpSources.PerRound, new XpMath.RoundInputs(
                ExpectedFinish: expected,
                EffectiveFinish: effective,
                FinishPosition: context.PlayerFinish,
                ScoredPoints: context.PlayerFinish is { } scored && scored <= context.PointsPositions,
                BeatTeammate: beatTeammate,
                Dnf: context.PlayerDnf),
                context.Modifiers); // xpRate perks scale the gain per cause (null mods = shipped formula)
            // A hit Setup Gamble also rewards character growth (0 without a bet or on a miss).
            if (player.Character.ProgressionVersion == CharacterLevelProgression.Level300Version)
            {
                xpRound = checked(xpRound + calledShotXpBonus);
                var plan = RequireVersionTwoPlan(player);
                var normalized = CharacterProgressionV2Math.NormalizeXpAward(
                    xpRound,
                    context.IsChampionshipRound && context.IsPrimaryRace,
                    player.XpScaleRemainder,
                    plan);
                newXp = checked(player.Xp + normalized.AppliedXp);
                newXpScaleRemainder = normalized.RemainderAfter;
                newLevel = CharacterLevelProgression.LevelForTotalXp(
                    player.Character.ProgressionVersion,
                    newXp,
                    grid.Year,
                    charRules);
                xpEvent = new JournalEvent
                {
                    Phase = JournalPhases.PlayerXp,
                    Entity = "player",
                    DeltaJson = CareerJson.Serialize(new
                    {
                        from = player.Xp,
                        to = newXp,
                        round = xpRound,
                        level = newLevel,
                        signedRawXp = normalized.SignedRawXp,
                        eligibleRawXp = normalized.EligibleRawXp,
                        appliedXp = normalized.AppliedXp,
                        remainderBefore = normalized.RemainderBefore,
                        remainderAfter = normalized.RemainderAfter,
                    }),
                    Cause = cause,
                };
            }
            else
            {
                // Preserve the shipped v0/v1 arithmetic and delta shape byte-for-byte.
                xpRound += calledShotXpBonus;
                newXp = Math.Max(0, player.Xp + xpRound);
                newLevel = CharacterLevelProgression.LevelForTotalXp(
                    player.Character.ProgressionVersion,
                    newXp,
                    grid.Year,
                    charRules);
                xpEvent = new JournalEvent
                {
                    Phase = JournalPhases.PlayerXp,
                    Entity = "player",
                    DeltaJson = CareerJson.Serialize(new { from = player.Xp, to = newXp, round = xpRound, level = newLevel }),
                    Cause = cause,
                };
            }
        }

        var events = new List<JournalEvent>
        {
            new()
            {
                Phase = JournalPhases.RaceResult,
                Entity = "player",
                DeltaJson = CareerJson.Serialize(new
                {
                    round = grid.Round,
                    expectedFinish = expected,
                    actualFinish = context.PlayerFinish,
                    dnf = context.PlayerDnf,
                }),
                Cause = cause,
            },
            new()
            {
                Phase = JournalPhases.PlayerOpi,
                Entity = "player",
                DeltaJson = CareerJson.Serialize(new
                {
                    from = Round4(player.Opi),
                    to = Round4(newOpi),
                }),
                Cause = cause,
            },
            new()
            {
                Phase = JournalPhases.PlayerReputation,
                Entity = "player",
                DeltaJson = CareerJson.Serialize(new
                {
                    from = Round4(player.Reputation),
                    to = Round4(newRep),
                    delta = Round4(repDelta),
                    beatTeammate,
                }),
                Cause = cause,
            },
        };

        // Setup Gamble resolution (4b): a fixed position right after the reputation row it stakes,
        // emitted ONLY when the player made a real gamble — so every ordinary round is unchanged.
        if (calledShotResolved)
        {
            bool hit = CalledShotMath.Hit(context.CalledShot!.Value, context.PlayerFinish);
            events.Add(new JournalEvent
            {
                Phase = JournalPhases.PlayerCall,
                Entity = "player",
                DeltaJson = CareerJson.Serialize(new
                {
                    called = context.CalledShot!.Value,
                    expectedFinish = expected,
                    actualFinish = context.PlayerFinish,
                    hit,
                    from = Round4(repAfterRound),
                    to = Round4(newRep),
                    delta = Round4(calledShotRepDelta),
                    xpBonus = calledShotXpBonus,
                }),
                Cause = hit ? "gamble-hit" : "gamble-miss",
            });
        }

        // player.xp rides between the reputation and pace-anchor rows (a fixed position, absent for
        // a character-free career).
        if (xpEvent is not null)
            events.Add(xpEvent);

        events.Add(new JournalEvent
        {
            Phase = JournalPhases.PlayerPaceAnchor,
            Entity = "player",
            DeltaJson = CareerJson.Serialize(new
            {
                from = Round4(player.PaceAnchor),
                to = Round4(newAnchor),
                recommendedSlider,
            }),
            Cause = cause,
        });

        // Qualifying anchor row (weekend rounds only) — a fixed position after the pace anchor,
        // absent for single-race rounds so their journal sequence is unchanged.
        if (context.PlayerQualifyingPosition is { } qPos)
        {
            events.Add(new JournalEvent
            {
                Phase = JournalPhases.PlayerQualiAnchor,
                Entity = "player",
                DeltaJson = CareerJson.Serialize(new
                {
                    from = Round4(player.QualifyingAnchor),
                    to = Round4(newQualiAnchor),
                    qualiPosition = qPos,
                }),
                Cause = cause,
            });
        }

        string? headline = null;
        if (context.Headlines is { } bank)
        {
            var seat = grid.Seats[playerIndex];
            var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["player"] = context.PlayerName ?? seat.DriverName,
                ["team"] = seat.TeamName,
                ["race"] = grid.RoundName,
                ["year"] = grid.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["position"] = context.PlayerFinish is { } p ? HeadlineSelector.Ordinal(p) : "out",
            };
            headline = HeadlineSelector.Select(
                bank, JournalPhases.RaceResult, cause, grid.Year, tokens,
                context.Streams.CreateStream(CareerStreams.Headlines, grid.Year, grid.Round, "race"));
            if (headline is not null)
            {
                events.Add(new JournalEvent
                {
                    Phase = JournalPhases.Headline,
                    Entity = "race",
                    DeltaJson = CareerJson.Serialize(new { text = headline }),
                    Cause = cause,
                });
            }
        }

        return new RoundUpdateResult
        {
            Player = player with
            {
                Opi = newOpi,
                Reputation = newRep,
                PaceAnchor = newAnchor,
                QualifyingAnchor = newQualiAnchor,
                Xp = newXp,
                Level = newLevel,
                XpScaleRemainder = newXpScaleRemainder,
                SeasonInjuryLoad = player.SeasonInjuryLoad + context.InjuryLoadDelta,
            },
            Events = events,
            RecommendedSlider = recommendedSlider,
            ExpectedFinish = expected,
            Headline = headline,
        };
    }

    /// <summary>Classifies the round for journal cause + headline keying, in priority order.</summary>
    private static string RaceCause(RoundUpdateContext context, int expected, double effective)
    {
        if (context.PlayerFinish is null)
        {
            return context.PlayerDnf == DnfCause.Mechanical ? "dnf-mechanical" : "dnf-driver-error";
        }

        int finish = context.PlayerFinish.Value;
        if (finish == 1)
            return "win";
        if (finish <= 3)
            return "podium";
        if (expected - effective >= 3.0)
            return "overperformed";
        if (effective - expected >= 3.0)
            return "underperformed";
        if (finish <= context.PointsPositions)
            return "points";
        return "midfield";
    }

    private static double Round4(double value) => Math.Round(value, 4);

    private static CampaignProgressionPlan RequireVersionTwoPlan(PlayerCareerState player)
    {
        var plan = player.CampaignProgressionPlan
            ?? throw new InvalidOperationException(
                "A version-2 character requires a pinned campaign progression plan.");
        plan.Validate();
        if (!string.Equals(player.ExperienceMode, plan.Mode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The player's experience mode does not match the pinned campaign progression plan.");
        }
        return plan;
    }
}
