using Companion.Core.Career;
using Companion.Core.Packs;

namespace Companion.Core.Smgp;

/// <summary>
/// Folds ONE round's rival battle into the career's <see cref="SmgpState"/> (M3 slice 2) — the
/// pure half the round fold calls when the raw envelope stored a rival call. Consumes
/// <see cref="SmgpRules"/> for the outcome/tally/trigger math and resolves the seat movements
/// against the pack's authored team LADDER (teams.json order, top tier first — LEVEL A down to
/// the floor team last, per docs/dev/smgp-design.md). Everything derives from pinned pack +
/// stored inputs + carried state, so replay re-folds byte-identically.
/// </summary>
public static class SmgpBattleFold
{
    public static SmgpBattleFoldResult Apply(SmgpBattleFoldContext context)
    {
        // The Madonna title defense OWNS its two rounds' battles against the reserved
        // challenger — the ordinary two-wins ladder never runs there (losing both must fire
        // you to Dardan, not demote you one tier).
        if (SmgpSchedule.IsTitleDefenseRound(context.State, context.Round) &&
            string.Equals(context.RivalDriverId, SmgpSchedule.DefenseChallenger(context.Pack), StringComparison.Ordinal))
            return ApplyTitleDefense(context);

        var state = context.State;
        var outcome = SmgpRules.BattleOutcome(context.PlayerFinish, context.RivalFinish);
        var update = SmgpRules.ApplyBattle(state.TallyFor(context.RivalDriverId), outcome);
        state = state.WithTally(context.RivalDriverId, update.Tally);

        var ladder = Ladder(context.Pack);
        var seatEvents = new List<JournalEvent>();

        if (update.Trigger == SmgpTrigger.SeatSwapOfferToPlayer && context.SeatSwapAccepted == true)
            state = ApplyAcceptedSwap(context, ladder, state, seatEvents);
        else if (update.Trigger == SmgpTrigger.PlayerSeatForfeit)
            state = ApplyForfeit(context, ladder, state, seatEvents);

        var events = new List<JournalEvent>
        {
            BattleEvent(context, outcome, update.Tally, update.Trigger, state.CareerOver,
                cause: outcome switch
                {
                    SmgpBattleOutcome.PlayerBeatRival => "battle-won",
                    SmgpBattleOutcome.RivalBeatPlayer => "battle-lost",
                    _ => "battle-void",
                }),
        };
        events.AddRange(seatEvents);

        return new SmgpBattleFoldResult { State = state, Events = events };
    }

    /// <summary>The Madonna title defense (M3 slice 4): round 1's outcome is carried on the
    /// state; round 2 resolves both via <see cref="SmgpRules.TitleDefense"/> — win at least one
    /// → Madonna kept; lose both → fired to Dardan (the challenger takes the player's car, the
    /// player takes the demotion seat, its occupant takes the challenger's old car). Defense
    /// battles never touch the two-wins tallies.</summary>
    private static SmgpBattleFoldResult ApplyTitleDefense(SmgpBattleFoldContext context)
    {
        var state = context.State;
        var outcome = SmgpRules.BattleOutcome(context.PlayerFinish, context.RivalFinish);
        var events = new List<JournalEvent>();

        if (context.Round <= 1)
        {
            state = state with { DefenseRound1 = outcome };
            events.Add(BattleEvent(context, outcome, state.TallyFor(context.RivalDriverId),
                SmgpTrigger.None, state.CareerOver,
                cause: outcome == SmgpBattleOutcome.PlayerBeatRival ? "defense-round-won" : "defense-round-lost"));
            return new SmgpBattleFoldResult { State = state, Events = events };
        }

        var verdict = SmgpRules.TitleDefense(state.DefenseRound1, outcome);
        state = state with { TitleDefense = false, DefenseRound1 = SmgpBattleOutcome.Void };

        if (verdict == SmgpTitleDefense.FiredToDardan)
        {
            string playerSeat = state.CurrentSeatLivery;
            string? challengerSeat = CurrentSeatOf(context.Pack, state, context.RivalDriverId);
            string? demotionSeat = SmgpSchedule.DemotionSeat(context.Pack, playerSeat, challengerSeat);
            if (demotionSeat is not null && challengerSeat is not null &&
                !string.Equals(challengerSeat, playerSeat, StringComparison.Ordinal))
            {
                string? displacedDriver = OccupantOf(context.Pack, state, demotionSeat);
                state = state with { CurrentSeatLivery = demotionSeat };
                state = state.WithAiSeatOverride(context.RivalDriverId, playerSeat);
                if (displacedDriver is not null)
                    state = state.WithAiSeatOverride(displacedDriver, challengerSeat);
                events.Add(BattleEvent(context, outcome, state.TallyFor(context.RivalDriverId),
                    SmgpTrigger.None, state.CareerOver, cause: "defense-lost"));
                events.Add(SeatEvent("defense-lost", playerSeat, demotionSeat,
                    context.RivalDriverId, challengerSeat, playerSeat,
                    displacedDriver, displacedDriver is null ? null : demotionSeat,
                    displacedDriver is null ? null : challengerSeat));
                return new SmgpBattleFoldResult { State = state, Events = events };
            }
        }

        events.Add(BattleEvent(context, outcome, state.TallyFor(context.RivalDriverId),
            SmgpTrigger.None, state.CareerOver, cause: "defense-held"));
        return new SmgpBattleFoldResult { State = state, Events = events };
    }

    private static JournalEvent BattleEvent(
        SmgpBattleFoldContext context, SmgpBattleOutcome outcome, SmgpBattleTally tally,
        SmgpTrigger trigger, bool careerOver, string cause) => new()
    {
        Phase = JournalPhases.SmgpBattle,
        Entity = "player",
        DeltaJson = CareerJson.Serialize(new
        {
            rival = context.RivalDriverId,
            forced = context.Forced,
            playerFinish = context.PlayerFinish,
            rivalFinish = context.RivalFinish,
            outcome,
            playerStreak = tally.PlayerStreak,
            rivalStreak = tally.RivalStreak,
            trigger,
            swapAccepted = context.SeatSwapAccepted,
            careerOver,
        }),
        Cause = cause,
    };

    /// <summary>The two-wins offer, accepted: the verified displacement chain via
    /// <see cref="SmgpRules.PlayerSeatSwap"/> — player takes the rival's car, the rival drops to
    /// the first ladder team one tier below the player's OLD tier, that seat's occupant takes the
    /// player's old car. No resolvable rival seat / player seat off the ladder → no movement
    /// (the battle row still folds; a UI can only offer rivals that race).</summary>
    private static SmgpState ApplyAcceptedSwap(
        SmgpBattleFoldContext context, IReadOnlyList<LadderSeat> ladder, SmgpState state,
        List<JournalEvent> seatEvents)
    {
        string playerSeat = state.CurrentSeatLivery;
        char? playerTier = TierOf(ladder, playerSeat);
        string? rivalSeat = CurrentSeatOf(context.Pack, state, context.RivalDriverId);
        if (playerTier is not { } tier || rivalSeat is null ||
            string.Equals(rivalSeat, playerSeat, StringComparison.Ordinal))
            return state;

        var displacementByTier = new Dictionary<char, string>();
        if (SmgpRules.TierBelow(tier) is { } below &&
            FirstSeatAt(ladder, below, playerSeat, rivalSeat) is { } displacement)
            displacementByTier[below] = displacement;

        var swap = SmgpRules.PlayerSeatSwap(playerSeat, tier, rivalSeat, displacementByTier);

        // Resolve the displaced occupant against the PRE-swap state, then move all three.
        string? displacedDriver = swap.DisplacedSeat is { } displacedSeat
            ? OccupantOf(context.Pack, state, displacedSeat)
            : null;
        state = state with { CurrentSeatLivery = swap.PlayerNewSeat };
        state = state.WithAiSeatOverride(context.RivalDriverId, swap.RivalNewSeat);
        if (displacedDriver is not null && swap.DisplacedDriverNewSeat is { } displacedTo)
            state = state.WithAiSeatOverride(displacedDriver, displacedTo);

        seatEvents.Add(SeatEvent("seat-swap", playerSeat, swap.PlayerNewSeat,
            context.RivalDriverId, rivalSeat, swap.RivalNewSeat,
            displacedDriver, swap.DisplacedSeat, swap.DisplacedDriverNewSeat));
        return state;
    }

    /// <summary>The rival beat the player twice: he takes the player's car and the player is
    /// demoted — to the first ladder seat one tier below, or one ladder step down WITHIN the
    /// floor tier; at the floor team itself (nothing below, the last ladder seat) the career is
    /// over (<see cref="SmgpRules.IsCareerOver"/>) and no seat moves. The displaced occupant of
    /// the player's destination takes the rival's old car (the mirrored chain).</summary>
    private static SmgpState ApplyForfeit(
        SmgpBattleFoldContext context, IReadOnlyList<LadderSeat> ladder, SmgpState state,
        List<JournalEvent> seatEvents)
    {
        string playerSeat = state.CurrentSeatLivery;
        char? playerTier = TierOf(ladder, playerSeat);
        string? rivalSeat = CurrentSeatOf(context.Pack, state, context.RivalDriverId);
        if (playerTier is not { } tier || rivalSeat is null ||
            string.Equals(rivalSeat, playerSeat, StringComparison.Ordinal))
            return state;

        bool atFloorTeam = ladder.Count > 0 &&
            string.Equals(ladder[^1].Livery, playerSeat, StringComparison.Ordinal);
        if (SmgpRules.IsCareerOver(SmgpTrigger.PlayerSeatForfeit, tier, atFloorTeam))
            return state with { CareerOver = true };

        string? target = SmgpRules.TierBelow(tier) is { } below
            ? FirstSeatAt(ladder, below, playerSeat, rivalSeat)
            : NextSeatDownWithin(ladder, tier, playerSeat, rivalSeat);
        // Nothing to demote into (a pack without a lower rung): a straight two-way swap.
        target ??= rivalSeat;

        string? displacedDriver = string.Equals(target, rivalSeat, StringComparison.Ordinal)
            ? null
            : OccupantOf(context.Pack, state, target);
        state = state with { CurrentSeatLivery = target };
        state = state.WithAiSeatOverride(context.RivalDriverId, playerSeat);
        if (displacedDriver is not null)
            state = state.WithAiSeatOverride(displacedDriver, rivalSeat);

        seatEvents.Add(SeatEvent("seat-forfeit", playerSeat, target,
            context.RivalDriverId, rivalSeat, playerSeat,
            displacedDriver, displacedDriver is null ? null : target, displacedDriver is null ? null : rivalSeat));
        return state;
    }

    private static JournalEvent SeatEvent(
        string cause, string playerFrom, string playerTo,
        string rivalDriverId, string rivalFrom, string rivalTo,
        string? displacedDriverId, string? displacedFrom, string? displacedTo) => new()
    {
        Phase = JournalPhases.SmgpSeat,
        Entity = "player",
        DeltaJson = CareerJson.Serialize(new
        {
            player = new { from = playerFrom, to = playerTo },
            rival = new { driverId = rivalDriverId, from = rivalFrom, to = rivalTo },
            displaced = displacedDriverId is null
                ? null
                : new { driverId = displacedDriverId, from = displacedFrom, to = displacedTo },
        }),
        Cause = cause,
    };

    // ---------- the authored ladder + seat occupancy ----------

    /// <summary>One team's car on the ladder, in the pack's authored team order (the design
    /// doc's tier listing — the LAST entry is the floor team). One-driver teams per the mode;
    /// a team's seat is its first authored entry.</summary>
    internal sealed record LadderSeat(string TeamId, char Tier, string Livery, string DefaultDriverId);

    internal static List<LadderSeat> Ladder(SeasonPack pack)
    {
        var ladder = new List<LadderSeat>(pack.Teams.Count);
        foreach (var team in pack.Teams)
        {
            var entry = pack.Entries.FirstOrDefault(e =>
                string.Equals(e.TeamId, team.Id, StringComparison.Ordinal));
            if (entry is not null)
                ladder.Add(new LadderSeat(team.Id, SmgpRules.Tier(team.Prestige), entry.Ams2LiveryName, entry.DriverId));
        }
        return ladder;
    }

    internal static char? TierOf(IReadOnlyList<LadderSeat> ladder, string livery) =>
        ladder.FirstOrDefault(s => string.Equals(s.Livery, livery, StringComparison.Ordinal))?.Tier;

    /// <summary>The first authored seat at <paramref name="tier"/> not already involved in the
    /// swap — the deterministic stand-in for the game's "the team one tier below yours".</summary>
    internal static string? FirstSeatAt(
        IReadOnlyList<LadderSeat> ladder, char tier, string excludeSeat1, string excludeSeat2) =>
        ladder.FirstOrDefault(s => s.Tier == tier &&
            !string.Equals(s.Livery, excludeSeat1, StringComparison.Ordinal) &&
            !string.Equals(s.Livery, excludeSeat2, StringComparison.Ordinal))?.Livery;

    /// <summary>The next authored seat below the player's WITHIN the floor tier (Rigel → Comet →
    /// Orchis → Zeroforce): the one honest reading of "demoted one tier" when no tier is left.</summary>
    private static string? NextSeatDownWithin(
        IReadOnlyList<LadderSeat> ladder, char tier, string playerSeat, string rivalSeat)
    {
        for (int i = 0; i < ladder.Count; i++)
        {
            if (!string.Equals(ladder[i].Livery, playerSeat, StringComparison.Ordinal))
                continue;
            for (int j = i + 1; j < ladder.Count; j++)
            {
                if (ladder[j].Tier == tier &&
                    !string.Equals(ladder[j].Livery, rivalSeat, StringComparison.Ordinal))
                    return ladder[j].Livery;
            }
            return null;
        }
        return null;
    }

    /// <summary>The car <paramref name="driverId"/> currently drives: his seat override when a
    /// swap moved him, else his authored pack seat. Null = he does not race (no authored entry).</summary>
    internal static string? CurrentSeatOf(SeasonPack pack, SmgpState state, string driverId)
    {
        if (state.AiSeatOverrides.TryGetValue(driverId, out var overridden))
            return overridden;
        return pack.Entries.FirstOrDefault(e =>
            string.Equals(e.DriverId, driverId, StringComparison.Ordinal))?.Ams2LiveryName;
    }

    /// <summary>Who currently occupies <paramref name="livery"/>: the player (null — callers
    /// never displace the player), a swapped-in driver, or the seat's authored default when he
    /// has not been moved elsewhere. Swap chains are closed, so every seat stays occupied.</summary>
    internal static string? OccupantOf(SeasonPack pack, SmgpState state, string livery)
    {
        if (string.Equals(livery, state.CurrentSeatLivery, StringComparison.Ordinal))
            return null;
        foreach (var pair in state.AiSeatOverrides)
        {
            if (string.Equals(pair.Value, livery, StringComparison.Ordinal))
                return pair.Key;
        }
        var entry = pack.Entries.FirstOrDefault(e =>
            string.Equals(e.Ams2LiveryName, livery, StringComparison.Ordinal));
        if (entry is null || state.AiSeatOverrides.ContainsKey(entry.DriverId))
            return null;
        return entry.DriverId;
    }
}

/// <summary>The battle fold's inputs — pinned pack + carried state + the round's stored rival
/// call and both finishing positions (1-based classified positions; null = DNF/unclassified).</summary>
public sealed record SmgpBattleFoldContext
{
    public required SeasonPack Pack { get; init; }

    public required SmgpState State { get; init; }

    public required string RivalDriverId { get; init; }

    /// <summary>The 1-based round this battle happened in — the title defense keys off rounds
    /// 1 and 2. Default 0 (callers that never see a defense season) never matches a defense
    /// round, so the ordinary ladder runs exactly as before.</summary>
    public int Round { get; init; }

    public bool Forced { get; init; }

    /// <summary>The player's stored answer to a seat-swap offer this round's battle triggered
    /// (null = no offer arose / legacy).</summary>
    public bool? SeatSwapAccepted { get; init; }

    public required int? PlayerFinish { get; init; }

    public required int? RivalFinish { get; init; }
}

public sealed record SmgpBattleFoldResult
{
    public required SmgpState State { get; init; }

    public required IReadOnlyList<JournalEvent> Events { get; init; }
}
