using Companion.Core.Career;
using Companion.Core.Packs;

namespace Companion.Core.Smgp;

/// <summary>
/// Folds ONE round's rival battle into the career's <see cref="SmgpState"/> (M3 slice 2), the
/// pure half the round fold calls when the raw envelope stored a rival call. Consumes
/// <see cref="SmgpRules"/> for the outcome/tally/trigger math and resolves the seat movements
/// against the pack's authored team LADDER (teams.json order, top tier first, LEVEL A down to
/// the floor team last, per docs/dev/smgp-design.md). Everything derives from pinned pack +
/// stored inputs + carried state, so replay re-folds byte-identically.
/// </summary>
public static class SmgpBattleFold
{
    public static SmgpBattleFoldResult Apply(SmgpBattleFoldContext context)
    {
        // The Madonna title defense OWNS its two rounds' battles against the reserved
        // challenger, the ordinary two-wins ladder never runs there (losing both must fire
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
        char? playerTier = TierOf(ladder, state.CurrentSeatLivery);

        // A two-wins offer promotes in EVERY tier (incl. D→C, the way out of the floor). A
        // two-losses forfeit relegates only ABOVE D (to a RANDOM team one class below); at the D
        // floor there is nowhere below, so it does not relegate.
        if (update.Trigger == SmgpTrigger.SeatSwapOfferToPlayer)
        {
            if (state.TwoPhasePromotion)
            {
                // TWO-PHASE (3c-2): DEFER the promotion to the post-race screen, record the offer
                // (the car the player would move into) instead of moving the seat. No seat row yet;
                // the resolution (below / from the promotion screen) emits it on accept.
                string? rivalSeat = CurrentSeatOf(context.Pack, state, context.RivalDriverId);
                if (rivalSeat is not null &&
                    !string.Equals(rivalSeat, state.CurrentSeatLivery, StringComparison.Ordinal))
                    state = state with
                    {
                        PendingSwap = new SmgpPendingOffer
                        {
                            RivalDriverId = context.RivalDriverId,
                            OfferedSeat = rivalSeat,
                        },
                    };
            }
            else if (context.SeatSwapAccepted == true)
            {
                // LEGACY (pre-3c-2 careers): the up-front answer applies inline this round.
                state = ApplyAcceptedSwap(context, ladder, state, seatEvents);
            }
        }
        else if (update.Trigger == SmgpTrigger.PlayerSeatForfeit && playerTier != 'D')
            state = ApplyForfeit(context, ladder, state, seatEvents);

        // The LEVEL D floor: every LOST battle (any rival) counts toward FloorLossLimit; reaching
        // it ends the career, kicked out of F1 SMGP (Mike's rule).
        if (playerTier == 'D' && outcome == SmgpBattleOutcome.RivalBeatPlayer)
        {
            state = state with { FloorLosses = state.FloorLosses + 1 };
            if (state.FloorLosses >= SmgpRules.FloorLossLimit)
                state = state with { CareerOver = true };
        }

        // Promoting OUT of D (a win-swap up to C) wipes the floor-loss count, a fresh start above.
        if (playerTier == 'D' && TierOf(ladder, state.CurrentSeatLivery) is { } newTier && newTier != 'D')
            state = state with { FloorLosses = 0 };

        // TWO-PHASE resolution (3c-2): when the round already carries the player's stored decision
        // (replay of a resolved round, or the skip-everything default), resolve the pending offer
        // INSIDE this fold so the state + seat row re-derive byte-identically to the live path
        // (where ResolvePendingOffer ran from the promotion screen). No decision yet (the live
        // result-entry fold) → the offer stays pending for the screen to answer.
        if (state.PendingSwap is { } pending && context.SwapDecision is { } decided)
            state = ResolvePendingOffer(context.Pack, state, pending, decided, seatEvents);

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
    /// state; round 2 resolves both via <see cref="SmgpRules.TitleDefense"/>, win at least one
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
                cause: outcome switch
                {
                    SmgpBattleOutcome.PlayerBeatRival => "defense-round-won",
                    SmgpBattleOutcome.RivalBeatPlayer => "defense-round-lost",
                    _ => "defense-round-void",
                }));
            return new SmgpBattleFoldResult { State = state, Events = events };
        }

        var verdict = SmgpRules.TitleDefense(state.DefenseRound1, outcome);
        state = state with { TitleDefense = false, DefenseRound1 = SmgpBattleOutcome.Void };

        if (verdict == SmgpTitleDefense.FiredToDardan)
        {
            string playerSeat = state.CurrentSeatLivery;
            string? challengerSeat = CurrentSeatOf(context.Pack, state, context.RivalDriverId);
            string? demotionSeat = SmgpSchedule.DemotionSeat(context.Pack, playerSeat, challengerSeat);
            if (demotionSeat is not null &&
                !string.Equals(demotionSeat, playerSeat, StringComparison.Ordinal))
            {
                // CLEAN (Mike's anti-chaos rule): the player is fired into the demotion car (Dardan);
                // Madonna reverts to its authored driver and the challenger keeps his own car. Only the
                // player moves, no cascade. (The old chain's "the challenger takes Madonna" flourish is
                // dropped in favour of a clean grid.)
                state = state with { CurrentSeatLivery = demotionSeat };
                events.Add(BattleEvent(context, outcome, state.TallyFor(context.RivalDriverId),
                    SmgpTrigger.None, state.CareerOver, cause: "defense-lost"));
                events.Add(SeatEvent("defense-lost", playerSeat, demotionSeat,
                    context.RivalDriverId, challengerSeat ?? "", challengerSeat ?? "",
                    displacedDriverId: null, displacedFrom: null, displacedTo: null));
                return new SmgpBattleFoldResult { State = state, Events = events };
            }

            // The verdict stands even when the seat chain cannot resolve (no demotion target /
            // no challenger seat), journal the LOSS honestly; only the seats stay put.
            events.Add(BattleEvent(context, outcome, state.TallyFor(context.RivalDriverId),
                SmgpTrigger.None, state.CareerOver, cause: "defense-lost"));
            return new SmgpBattleFoldResult { State = state, Events = events };
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
    /// <see cref="SmgpRules.PlayerSeatSwap"/>, player takes the rival's car, the rival drops to
    /// the first ladder team one tier below the player's OLD tier, that seat's occupant takes the
    /// player's old car. No resolvable rival seat / player seat off the ladder → no movement
    /// (the battle row still folds; a UI can only offer rivals that race).</summary>
    private static SmgpState ApplyAcceptedSwap(
        SmgpBattleFoldContext context, IReadOnlyList<LadderSeat> ladder, SmgpState state,
        List<JournalEvent> seatEvents)
    {
        string playerSeat = state.CurrentSeatLivery;
        string? rivalSeat = CurrentSeatOf(context.Pack, state, context.RivalDriverId);
        if (rivalSeat is null || string.Equals(rivalSeat, playerSeat, StringComparison.Ordinal))
            return state;

        // CLEAN swap (Mike: "the original driver should come back to that car i just left and the rival
        // i beat disappears until you switch teams again"): the player simply MOVES into the rival's car.
        // No AI seat overrides, the whole grid is a fresh function of the player's current car, so the
        // rival benches while the player holds his seat, and the car the player just left reverts to its
        // authored driver. Nobody else moves; the field never cascades. (The distinct-driver player id +
        // the grid resolver do the rest, see RoundGridResolver.ApplyPlayerSeat.)
        state = state with { CurrentSeatLivery = rivalSeat };
        seatEvents.Add(SeatEvent("seat-swap", playerSeat, rivalSeat,
            context.RivalDriverId, rivalSeat, Benched,
            displacedDriverId: null, displacedFrom: null, displacedTo: null));
        return state;
    }

    /// <summary>The rival's "to" seat when a clean swap benches him (he holds no car while the player
    /// occupies his seat, and returns to it the moment the player moves on).</summary>
    private const string Benched = "";

    /// <summary>
    /// Resolve a PENDING two-wins offer (3c-2) with the player's post-race decision, the shared
    /// half both paths run so live and replay produce the SAME state + seat rows:
    /// <list type="bullet">
    /// <item>the promotion SCREEN (live) calls it once the player answers, appending the resulting
    /// <c>smgp.seat</c> row and re-persisting the round's state;</item>
    /// <item>replay's <see cref="Apply"/> calls it inline once the stored <c>smgp.swap</c> input
    /// supplies the decision.</item>
    /// </list>
    /// ACCEPT = a CLEAN move into the offered car (the rival benches, the player's old car reverts —
    /// exactly like <see cref="ApplyAcceptedSwap"/>), and promoting OUT of the D floor wipes the
    /// floor-loss count. DECLINE (or a pending that cannot resolve) just clears the offer; the seat
    /// holds. Callers pass a state whose <see cref="SmgpState.PendingSwap"/> equals
    /// <paramref name="pending"/>.
    /// </summary>
    public static SmgpState ResolvePendingOffer(
        SeasonPack pack, SmgpState state, SmgpPendingOffer pending, bool accept,
        List<JournalEvent> seatEvents)
    {
        if (!accept)
            return state with { PendingSwap = null };

        var ladder = Ladder(pack);
        char? oldTier = TierOf(ladder, state.CurrentSeatLivery);
        string playerSeat = state.CurrentSeatLivery;

        state = state with { CurrentSeatLivery = pending.OfferedSeat, PendingSwap = null };
        seatEvents.Add(SeatEvent("seat-swap", playerSeat, pending.OfferedSeat,
            pending.RivalDriverId, pending.OfferedSeat, Benched,
            displacedDriverId: null, displacedFrom: null, displacedTo: null));

        // Promoting OUT of D wipes the floor-loss count, the same fresh-start rule the inline path
        // applies, now on the round the offer is ACCEPTED (the deferred move actually happens here).
        if (oldTier == 'D' && TierOf(ladder, state.CurrentSeatLivery) is { } newTier && newTier != 'D')
            state = state with { FloorLosses = 0 };
        return state;
    }

    /// <summary>The rival beat the player twice (above D): the player is RELEGATED to a team in
    /// the class BELOW, chosen at RANDOM (deterministically, from the master seed) among that
    /// tier's teams, the rival takes the player's old car, and the random team's displaced
    /// driver takes the rival's old car (the mirrored chain). Caller guards D out (the floor has
    /// nowhere below, the FloorLoss counter governs it there).</summary>
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

        // CLEAN demotion (Mike's anti-chaos rule): the player is dropped into a RANDOM team's car in the
        // class below (deterministic pick from the master seed + round + rival). Only the player moves —
        // that car's AI benches, the player's old car reverts to its authored driver, and the rival stays
        // in his own car. No cascade. No lower seat to drop into → the loss still journals; the seat holds.
        string? target = SmgpRules.TierBelow(tier) is { } below
            ? RandomSeatAt(ladder, below, playerSeat, rivalSeat, context)
            : null;
        if (target is null)
            return state;

        state = state with { CurrentSeatLivery = target };
        seatEvents.Add(SeatEvent("seat-forfeit", playerSeat, target,
            context.RivalDriverId, rivalSeat, rivalSeat,
            displacedDriverId: null, displacedFrom: null, displacedTo: null));
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

    /// <summary>One CAR on the ladder, in the pack's authored team order (the design doc's tier
    /// listing, the LAST team is the floor team) with each team's cars in their authored entry
    /// order (data contract: a team's LADDER/champion car is authored FIRST in its block). Every
    /// entry is a ladder seat, so two-car teams (the 32-car skinpack field) swap and demote per
    /// CAR while team-level rules (the floor check) key the TeamId.</summary>
    internal sealed record LadderSeat(string TeamId, char Tier, string Livery, string DefaultDriverId);

    internal static List<LadderSeat> Ladder(SeasonPack pack)
    {
        var ladder = new List<LadderSeat>(pack.Entries.Count);
        foreach (var team in pack.Teams)
        {
            foreach (var entry in pack.Entries)
            {
                if (string.Equals(entry.TeamId, team.Id, StringComparison.Ordinal))
                    ladder.Add(new LadderSeat(team.Id, SmgpRules.Tier(team.Prestige), entry.Ams2LiveryName, entry.DriverId));
            }
        }
        return ladder;
    }


    internal static char? TierOf(IReadOnlyList<LadderSeat> ladder, string livery) =>
        ladder.FirstOrDefault(s => string.Equals(s.Livery, livery, StringComparison.Ordinal))?.Tier;

    /// <summary>The first authored seat at <paramref name="tier"/> not already involved in the
    /// swap, the deterministic stand-in for the game's "the team one tier below yours".</summary>
    internal static string? FirstSeatAt(
        IReadOnlyList<LadderSeat> ladder, char tier, string excludeSeat1, string excludeSeat2) =>
        ladder.FirstOrDefault(s => s.Tier == tier &&
            !string.Equals(s.Livery, excludeSeat1, StringComparison.Ordinal) &&
            !string.Equals(s.Livery, excludeSeat2, StringComparison.Ordinal))?.Livery;

    /// <summary>A RANDOM team's (first) seat at <paramref name="tier"/>, the relegation target,
    /// picked deterministically from the master seed + round + rival so replay re-derives the
    /// exact same team. One seat per team (its first, in ladder order), excluding the player's and
    /// rival's seats. Null when no team at that tier is eligible.</summary>
    private static string? RandomSeatAt(
        IReadOnlyList<LadderSeat> ladder, char tier, string playerSeat, string rivalSeat,
        SmgpBattleFoldContext context)
    {
        var candidates = new List<string>();
        var seenTeams = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in ladder)
        {
            if (s.Tier != tier || !seenTeams.Add(s.TeamId))
                continue;
            if (string.Equals(s.Livery, playerSeat, StringComparison.Ordinal) ||
                string.Equals(s.Livery, rivalSeat, StringComparison.Ordinal))
                continue;
            candidates.Add(s.Livery);
        }
        if (candidates.Count == 0)
            return null;
        uint pick = DeterministicHash(context.MasterSeed, context.Round, context.RivalDriverId);
        return candidates[(int)(pick % (uint)candidates.Count)];
    }

    /// <summary>A stable FNV-1a hash over (master seed, round, rival), deterministic and
    /// build-stable, so the relegation team is identical on the live and replay paths.</summary>
    private static uint DeterministicHash(long seed, int round, string rival)
    {
        unchecked
        {
            uint h = 2166136261;
            void MixByte(byte b) { h ^= b; h *= 16777619; }
            for (int i = 0; i < 8; i++) MixByte((byte)((ulong)seed >> (i * 8)));
            for (int i = 0; i < 4; i++) MixByte((byte)((uint)round >> (i * 8)));
            foreach (char c in rival) { MixByte((byte)c); MixByte((byte)(c >> 8)); }
            return h;
        }
    }

    /// <summary>The car <paramref name="driverId"/> currently drives: his seat override when a
    /// swap moved him, else his authored pack seat, UNLESS someone else holds that car (the
    /// player took it at creation, or a swap/introduction moved another driver in), which means
    /// he lost the ride without receiving one: he does not race. Null = no current car.</summary>
    internal static string? CurrentSeatOf(SeasonPack pack, SmgpState state, string driverId)
    {
        if (state.AiSeatOverrides.TryGetValue(driverId, out var overridden))
            return overridden;
        string? authored = pack.Entries.FirstOrDefault(e =>
            string.Equals(e.DriverId, driverId, StringComparison.Ordinal))?.Ams2LiveryName;
        if (authored is null ||
            string.Equals(authored, state.CurrentSeatLivery, StringComparison.Ordinal) ||
            state.AiSeatOverrides.Values.Contains(authored, StringComparer.Ordinal))
            return null;
        return authored;
    }
}

/// <summary>The battle fold's inputs, pinned pack + carried state + the round's stored rival
/// call and both finishing positions (1-based classified positions; null = DNF/unclassified).</summary>
public sealed record SmgpBattleFoldContext
{
    public required SeasonPack Pack { get; init; }

    public required SmgpState State { get; init; }

    public required string RivalDriverId { get; init; }

    /// <summary>The 1-based round this battle happened in, the title defense keys off rounds
    /// 1 and 2. Default 0 (callers that never see a defense season) never matches a defense
    /// round, so the ordinary ladder runs exactly as before.</summary>
    public int Round { get; init; }

    /// <summary>The career's master seed, the relegation team is picked deterministically from
    /// it (+ round + rival), so the "random" team is identical on the live and replay paths.</summary>
    public long MasterSeed { get; init; }

    public bool Forced { get; init; }

    /// <summary>The player's stored answer to a seat-swap offer this round's battle triggered
    /// (null = no offer arose / legacy). For a TWO-PHASE career this is only the UP-FRONT default
    /// (the standing answer); the real decision rides <see cref="SwapDecision"/>.</summary>
    public bool? SeatSwapAccepted { get; init; }

    /// <summary>The player's POST-RACE promotion-screen decision for a two-phase career (3c-2),
    /// read back from the journaled <c>smgp.swap</c> input at re-fold. Null = undecided (the live
    /// result-entry fold, before the screen, the offer stays pending) or a non-two-phase career.
    /// When set on a round that raised an offer, <see cref="Apply"/> resolves the pending swap
    /// inline so replay re-derives the live outcome byte-identically.</summary>
    public bool? SwapDecision { get; init; }

    public required int? PlayerFinish { get; init; }

    public required int? RivalFinish { get; init; }
}

public sealed record SmgpBattleFoldResult
{
    public required SmgpState State { get; init; }

    public required IReadOnlyList<JournalEvent> Events { get; init; }
}
