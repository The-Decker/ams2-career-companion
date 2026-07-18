namespace Companion.Core.Smgp;

/// <summary>
/// The SUPER MONACO GP replica mode's pure rules (docs/dev/smgp-design.md, manual-verified) —
/// rival battles, the two-wins seat swap with its one-tier displacement chain, the Zeroforce
/// career-over state, the Ceara title defense and two-titles completion. PURE data + math: no
/// I/O, no fold wiring, the envelope/fold slices consume these exactly like CalledShotMath.
/// Team tiers ride the pack's authored prestige (5=LEVEL A · 4=B · 3=C · 2=D).
/// </summary>
public static class SmgpRules
{
    /// <summary>The mode key a pack declares via <c>pack.json careerStyle</c>.</summary>
    public const string CareerStyle = "smgp";

    /// <summary>LEVEL A..D from the pack's authored team prestige (A=5 … D=2).</summary>
    public static char Tier(int prestige) => prestige switch
    {
        >= 5 => 'A',
        4 => 'B',
        3 => 'C',
        _ => 'D',
    };

    /// <summary>One step down the ladder (A→B→C→D); D has nothing below (Zeroforce floor).</summary>
    public static char? TierBelow(char tier) => tier switch
    {
        'A' => 'B',
        'B' => 'C',
        'C' => 'D',
        _ => null,
    };

    /// <summary>One step UP the ladder (D→C→B→A); A has nothing above.</summary>
    public static char? TierAbove(char tier) => tier switch
    {
        'D' => 'C',
        'C' => 'B',
        'B' => 'A',
        _ => null,
    };

    /// <summary>Ladder rank, D=0 … A=3 (higher = better team), for tier comparisons.</summary>
    public static int Rank(char tier) => tier switch { 'A' => 3, 'B' => 2, 'C' => 1, _ => 0 };

    /// <summary>Whether the player (in <paramref name="playerTier"/>) may CHALLENGE a rival in
    /// <paramref name="rivalTier"/> (Mike's rule, 2026-07-12): your OWN tier, the ONE tier directly above
    /// (the seat you climb toward), and ANY tier below. So D→{D,C}; C→{B,C,D}; B→{A,B,C,D}; A→everyone.
    /// Never two tiers up. (Your own TEAMMATE is still excluded separately by the briefing, same-tier means
    /// other teams at your level, not your sister seat.) DISPLAY-ONLY: this gates the namable-rivals picker,
    /// never the battle fold, so changing it never affects a folded/replayed career.</summary>
    public static bool CanChallenge(char playerTier, char rivalTier) =>
        Rank(rivalTier) <= Rank(playerTier) + 1;

    /// <summary>The floor (LEVEL D) tolerance: lose this many rival battles while in a D team and
    /// the SMGP career is over, kicked out of F1 SMGP. (Mike's rule; the one hard-fail state now
    /// that D has nowhere to be relegated to.)</summary>
    public const int FloorLossLimit = 4;

    /// <summary>The grand campaign length: SEVENTEEN full seasons (Mike's "17 seasons total before it
    /// gives you a final final screen"). Reaching the end of season 17 unlocks the locked finale and its
    /// secret <c>special.jpg</c>; being champion in all 17 unlocks the deeper secret <c>ultimate.jpg</c>.
    /// This is the true summit, a milestone BEYOND <see cref="IsComplete"/>'s 2-title replica marker.</summary>
    public const int CampaignSeasons = 17;

    /// <summary>
    /// The round's rival-battle outcome. The game decides by finishing ahead: a classified
    /// finish beats a DNF; both out = the battle is VOID (no count moves, the manual's rule
    /// is "beat him", and neither beat anyone). Positions are 1-based finishing positions,
    /// null = did not finish/classify.
    /// </summary>
    public static SmgpBattleOutcome BattleOutcome(int? playerPosition, int? rivalPosition)
    {
        if (playerPosition is null && rivalPosition is null)
            return SmgpBattleOutcome.Void;
        if (rivalPosition is null)
            return SmgpBattleOutcome.PlayerBeatRival;
        if (playerPosition is null)
            return SmgpBattleOutcome.RivalBeatPlayer;
        return playerPosition < rivalPosition
            ? SmgpBattleOutcome.PlayerBeatRival
            : SmgpBattleOutcome.RivalBeatPlayer;
    }

    /// <summary>
    /// Folds one battle outcome into the per-rival tally and reports what it triggers. The
    /// two-wins rule is "beat the same rival TWICE WITHOUT LOSING TO HIM": a loss resets the
    /// player's streak against that rival (and vice versa, streaks are tracked per side).
    /// </summary>
    public static SmgpBattleUpdate ApplyBattle(SmgpBattleTally tally, SmgpBattleOutcome outcome)
    {
        if (outcome == SmgpBattleOutcome.Void)
            return new SmgpBattleUpdate { Tally = tally, Trigger = SmgpTrigger.None };

        var updated = outcome == SmgpBattleOutcome.PlayerBeatRival
            ? tally with { PlayerStreak = tally.PlayerStreak + 1, RivalStreak = 0 }
            : tally with { RivalStreak = tally.RivalStreak + 1, PlayerStreak = 0 };

        var trigger = updated switch
        {
            { PlayerStreak: >= 2 } => SmgpTrigger.SeatSwapOfferToPlayer,
            { RivalStreak: >= 2 } => SmgpTrigger.PlayerSeatForfeit,
            _ => SmgpTrigger.None,
        };
        // A consumed trigger resets that side's streak (the ladder restarts after each offer).
        if (trigger == SmgpTrigger.SeatSwapOfferToPlayer)
            updated = updated with { PlayerStreak = 0 };
        else if (trigger == SmgpTrigger.PlayerSeatForfeit)
            updated = updated with { RivalStreak = 0 };

        return new SmgpBattleUpdate { Tally = updated, Trigger = trigger };
    }

    /// <summary>
    /// The verified seat-swap displacement chain, player side: the player takes the RIVAL's
    /// seat; the rival drops to the team ONE TIER BELOW the player's OLD tier; that team's
    /// displaced driver takes the player's old seat. When the player's old tier is the floor
    /// (D), the rival simply takes the player's old seat (a straight swap, nothing below D).
    /// Seats are identified by livery (the pack's binding key).
    /// </summary>
    public static SmgpSeatSwap PlayerSeatSwap(
        string playerSeat, char playerTier,
        string rivalSeat,
        IReadOnlyDictionary<char, string> displacementSeatByTier)
    {
        char? below = TierBelow(playerTier);
        if (below is { } tier && displacementSeatByTier.TryGetValue(tier, out var displacementSeat) &&
            !string.Equals(displacementSeat, rivalSeat, StringComparison.Ordinal))
        {
            return new SmgpSeatSwap
            {
                PlayerNewSeat = rivalSeat,
                RivalNewSeat = displacementSeat,
                DisplacedDriverNewSeat = playerSeat,
                DisplacedSeat = displacementSeat,
            };
        }
        // Floor (or the displacement seat IS the rival's own): straight swap.
        return new SmgpSeatSwap
        {
            PlayerNewSeat = rivalSeat,
            RivalNewSeat = playerSeat,
            DisplacedDriverNewSeat = null,
            DisplacedSeat = null,
        };
    }

    /// <summary>
    /// The Madonna title defense: the reigning champion starts the next season in MADONNA and
    /// G. Ceara force-challenges in rounds 1 AND 2. Win AT LEAST ONE → keep Madonna; lose both
    /// → fired to DARDAN and Ceara takes Madonna.
    /// </summary>
    public static SmgpTitleDefense TitleDefense(SmgpBattleOutcome round1, SmgpBattleOutcome round2)
    {
        bool wonAny = round1 == SmgpBattleOutcome.PlayerBeatRival ||
                      round2 == SmgpBattleOutcome.PlayerBeatRival;
        return wonAny ? SmgpTitleDefense.MadonnaKept : SmgpTitleDefense.FiredToDardan;
    }

    /// <summary>Two championships won = the game is beaten (the mode keeps running like normal
    /// carryover afterward; this only marks completion). A MILESTONE, not the summit, the
    /// 17-season campaign (<see cref="CampaignSeasons"/>) is the real end.</summary>
    public static bool IsComplete(int titles) => titles >= 2;

    /// <summary>The campaign is COMPLETED, the player went the distance through all
    /// <see cref="CampaignSeasons"/> seasons without the career ending. "Beat all 17" = play through
    /// seventeen (Mike, 2026-07-12); losing titles along the way does not fail it, only
    /// <paramref name="careerOver"/> (the Level-D floor kicking you out) does. Unlocks the locked
    /// finale + <c>special.jpg</c>. A pure read over folded state (season count + CareerOver).</summary>
    public static bool CampaignComplete(int seasonOrdinal, bool careerOver) =>
        seasonOrdinal >= CampaignSeasons && !careerOver;

    /// <summary>The FLAWLESS campaign, champion in EVERY one of the <see cref="CampaignSeasons"/>
    /// seasons (the emperor run). Unlocks the deeper secret <c>ultimate.jpg</c>. At the 17-season
    /// summit a title count of 17 can only mean 17-from-17 (you can never hold more titles than
    /// seasons raced), so this is the flawless-record predicate.</summary>
    public static bool CampaignFlawless(int seasonOrdinal, int titles, bool careerOver) =>
        CampaignComplete(seasonOrdinal, careerOver) && titles >= CampaignSeasons;
}

public enum SmgpBattleOutcome
{
    Void,
    PlayerBeatRival,
    RivalBeatPlayer,
}

public enum SmgpTrigger
{
    None,

    /// <summary>Two wins without a loss over this rival, "you may get an offer to join his
    /// team!" (the player chooses to accept or decline).</summary>
    SeatSwapOfferToPlayer,

    /// <summary>The rival beat the player twice without losing, he is offered YOUR seat; the
    /// player is demoted one tier (or, at Zeroforce, the career ends).</summary>
    PlayerSeatForfeit,
}

/// <summary>Per-rival battle streaks (both directions; a win on either side resets the other).</summary>
public sealed record SmgpBattleTally
{
    public static readonly SmgpBattleTally Empty = new();

    public int PlayerStreak { get; init; }
    public int RivalStreak { get; init; }
}

public sealed record SmgpBattleUpdate
{
    public required SmgpBattleTally Tally { get; init; }
    public required SmgpTrigger Trigger { get; init; }
}

/// <summary>The three-way (or two-way at the floor) seat reassignment of a completed swap.</summary>
public sealed record SmgpSeatSwap
{
    public required string PlayerNewSeat { get; init; }
    public required string RivalNewSeat { get; init; }

    /// <summary>The displaced third driver's destination (the player's old seat), or null when
    /// the swap was a straight two-way exchange.</summary>
    public string? DisplacedDriverNewSeat { get; init; }

    /// <summary>The seat whose driver was displaced (one tier below the player's old tier), or
    /// null for a straight swap.</summary>
    public string? DisplacedSeat { get; init; }
}

public enum SmgpTitleDefense
{
    MadonnaKept,
    FiredToDardan,
}
