namespace Companion.Core.Career;

/// <summary>
/// The "Setup Gamble" pre-race bet, resolved as a CALLED SHOT (design decision 2026-07-06, Mike):
/// before a race the player may call a finishing position BETTER than the sim expects; hitting it
/// banks reputation (and XP for a character), missing it costs the same stake. It is a pure function
/// of the journaled call + the round's result + the sim's expected finish, no dice, no stream, so a
/// replay reproduces it byte-for-byte, and it deliberately sidesteps the self-balancer: the reward is
/// contingent on hitting a SELF-SET target, not on raw pace (which the difficulty anchor would just
/// re-rate away). A call that is not more ambitious than the expected finish is not a gamble at all
/// (zero stake), so a player cannot mint free reputation by calling a finish they were going to get.
/// </summary>
public static class CalledShotMath
{
    /// <summary>Reputation staked on the least-ambitious real gamble (calling exactly one place
    /// better than expected). Comparable to a good round's rep swing, felt, never dominant.</summary>
    public const double BaseStake = 3.0;

    /// <summary>Extra reputation staked per place the call is more ambitious than the expected
    /// finish, a bolder call risks (and pays) more.</summary>
    public const double PerPlaceStake = 1.0;

    /// <summary>Ceiling on the stake so a wild call cannot swing a whole career in one race.</summary>
    public const double MaxStake = 12.0;

    /// <summary>XP granted per point of reputation staked, on a hit, a bold correct call is worth
    /// growth (a full-stake hit ≈ a race win's XP). Only a character career banks it.</summary>
    public const double XpPerStake = 4.0;

    /// <summary>The reputation at stake for calling <paramref name="calledPosition"/> when the sim
    /// expects <paramref name="expectedFinish"/>. Zero unless the call is genuinely ambitious
    /// (strictly better than the expected finish); otherwise it scales with the ambition and caps.</summary>
    public static double Stake(int calledPosition, int expectedFinish)
    {
        int ambition = expectedFinish - calledPosition; // > 0 ⇒ called better than expected
        if (ambition <= 0)
            return 0.0;
        // The first ambitious place is the base stake; each place beyond it adds PerPlaceStake.
        return Math.Clamp(BaseStake + PerPlaceStake * (ambition - 1), BaseStake, MaxStake);
    }

    /// <summary>Whether the call is a real gamble (strictly more ambitious than the expected finish).
    /// A call at or behind the expected finish stakes nothing and resolves to no effect.</summary>
    public static bool IsGamble(int calledPosition, int expectedFinish) =>
        calledPosition < expectedFinish;

    /// <summary>True when the called shot was hit: the player was classified at or ahead of the
    /// called position. A DNF (no classified finish) always misses the call.</summary>
    public static bool Hit(int calledPosition, int? actualFinish) =>
        actualFinish is { } a && a <= calledPosition;

    /// <summary>The signed reputation delta from resolving the call: +stake on a hit, −stake on a
    /// miss, and exactly 0 when the call was not a real gamble (stake 0).</summary>
    public static double ReputationDelta(int calledPosition, int? actualFinish, int expectedFinish)
    {
        double stake = Stake(calledPosition, expectedFinish);
        if (stake <= 0.0)
            return 0.0;
        return (Hit(calledPosition, actualFinish) ? 1.0 : -1.0) * stake;
    }

    /// <summary>Bonus XP for a HIT call (0 on a miss or a non-gamble): a bold correct call rewards
    /// character growth, scaled by the stake. The fold banks it only for a character career.</summary>
    public static int XpBonus(int calledPosition, int? actualFinish, int expectedFinish) =>
        Hit(calledPosition, actualFinish)
            ? (int)Math.Round(Stake(calledPosition, expectedFinish) * XpPerStake, MidpointRounding.AwayFromZero)
            : 0;
}
