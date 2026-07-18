using Companion.Core.Packs;

namespace Companion.Core.Smgp;

/// <summary>
/// The SMGP mode's SCHEDULE rules (M3 slice 4, docs/dev/smgp-design.md): which round forces a
/// challenge on the player, and the Madonna title-defense seating, the champion starts the next
/// season in MADONNA and the reserved challenger (G. Ceara, who holds no season entry, "reserved
/// for the title-defense event") is introduced into his Bullets car to force-challenge at rounds
/// 1 and 2. Pure functions of pinned pack + folded state. The replica's authored ids resolve
/// first; structural fallbacks (ladder position) keep any smgp-styled pack playable.
/// </summary>
public static class SmgpSchedule
{
    public const string MadonnaTeamId = "team.madonna";
    public const string DardanTeamId = "team.dardan";
    public const string BulletsTeamId = "team.bullets";
    public const string MinaraeTeamId = "team.minarae";
    public const string CearaDriverId = "driver.gilberto_ceara";

    /// <summary>Whether this round's challenge is FORCED by the title defense (rounds 1 + 2 of
    /// a defense season, while the defense is unresolved).</summary>
    public static bool IsTitleDefenseRound(SmgpState state, int round) =>
        state is { TitleDefense: true, CareerOver: false } && round is 1 or 2;

    /// <summary>This round's forced challenger (a pack driver id), or null, the briefing shows
    /// him instead of the free pick; the fold resolves his battles under the defense rule.</summary>
    public static string? ForcedChallenger(SeasonPack pack, SmgpState state, int round) =>
        IsTitleDefenseRound(state, round) ? DefenseChallenger(pack) : null;

    /// <summary>The reserved title-defense challenger: the authored Ceara id when the pack
    /// carries him, else the strongest driver WITHOUT a season entry (the "reserved" convention),
    /// else null, no challenger, so a defense season resolves as survived.</summary>
    public static string? DefenseChallenger(SeasonPack pack)
    {
        if (pack.Drivers.Any(d => string.Equals(d.Id, CearaDriverId, StringComparison.Ordinal)))
            return CearaDriverId;

        var entered = new HashSet<string>(pack.Entries.Select(e => e.DriverId), StringComparer.Ordinal);
        return pack.Drivers
            .Where(d => !entered.Contains(d.Id))
            .OrderByDescending(d => d.Ratings.RaceSkill)
            .ThenBy(d => d.Id, StringComparer.Ordinal)
            .FirstOrDefault()?.Id;
    }

    /// <summary>The champion's seat, Madonna's car, or the FIRST authored ladder seat (the
    /// authored order is the ladder, top first).</summary>
    public static string? ChampionSeat(SeasonPack pack)
    {
        var ladder = SmgpBattleFold.Ladder(pack);
        return SeatOfTeam(ladder, MadonnaTeamId) ?? (ladder.Count > 0 ? ladder[0].Livery : null);
    }

    /// <summary>Where a lost title defense fires the player to, Dardan's car, or the first seat
    /// one tier below the player's (excluding the seats already involved).</summary>
    public static string? DemotionSeat(SeasonPack pack, string playerSeat, string? challengerSeat)
    {
        var ladder = SmgpBattleFold.Ladder(pack);
        if (SeatOfTeam(ladder, DardanTeamId) is { } dardan &&
            !string.Equals(dardan, playerSeat, StringComparison.Ordinal) &&
            !string.Equals(dardan, challengerSeat, StringComparison.Ordinal))
            return dardan;
        return SmgpBattleFold.TierOf(ladder, playerSeat) is { } tier && SmgpRules.TierBelow(tier) is { } below
            ? SmgpBattleFold.FirstSeatAt(ladder, below, playerSeat, challengerSeat ?? playerSeat)
            : null;
    }

    /// <summary>
    /// The champion's between-seasons seating (called by the season-end fold on a title win,
    /// AFTER <see cref="SmgpState.WithSeasonReset"/> and the title increment): in the CLEAN model
    /// (Mike's anti-chaos rule) the champion simply MOVES into the champion (Madonna) seat for the
    /// title defense. Its authored driver benches while the player holds the seat, the distinct-driver
    /// grid resolver does that for free, and the player's old car reverts to its authored driver. No
    /// AI seat overrides, no cascade. The reserved challenger (G. Ceara) is a real authored entry, so
    /// he needs no introduction; the schedule force-challenges him in rounds 1-2 from his own car. No
    /// champion seat (an unauthored pack) → the state is returned untouched.
    /// </summary>
    public static SmgpState ChampionRollover(SeasonPack pack, SmgpState state)
    {
        string? championSeat = ChampionSeat(pack);
        if (championSeat is null)
            return state;

        // CLEAN (Mike's anti-chaos rule): the champion simply MOVES into Madonna's car for the title
        // defense. Its authored driver benches while the player holds the seat, and the player's old car
        // reverts to its authored driver, no AI seat overrides, no cascade. The reserved challenger
        // (G. Ceara) is a real authored entry, so he needs no introduction; the schedule force-challenges
        // him in rounds 1-2 from his own car.
        return state with { CurrentSeatLivery = championSeat };
    }

    private static string? SeatOfTeam(IReadOnlyList<SmgpBattleFold.LadderSeat> ladder, string teamId) =>
        ladder.FirstOrDefault(s => string.Equals(s.TeamId, teamId, StringComparison.Ordinal))?.Livery;
}
