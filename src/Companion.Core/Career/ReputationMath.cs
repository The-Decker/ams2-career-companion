namespace Companion.Core.Career;

/// <summary>
/// Reputation 0–100. Moves on finishing vs expectation, beating the teammate, and season-end
/// championship position; gains scale with how weak the player's car is (contract: a podium
/// in a weak car is worth far more than one in the class of the field — tier 5 is the
/// richest tier in this codebase, so the multiplier grows as tier falls).
/// </summary>
public static class ReputationMath
{
    /// <summary>1.0 for a tier-5 (top) car up to 2.0 for a tier-1 (minnow) car.</summary>
    public static double UnderdogMultiplier(int budgetTier)
    {
        int tier = Math.Clamp(budgetTier, 1, 5);
        return 1.0 + 0.25 * (5 - tier);
    }

    /// <summary>Per-round reputation delta: expectation delta (clamped ±5, half-weight)
    /// and podium bonus both era/tier-scaled; beating the teammate is a flat +1.</summary>
    public static double RoundDelta(
        double expectedFinish,
        double effectiveFinish,
        int? classifiedPosition,
        bool beatTeammate,
        int teamTier)
    {
        double multiplier = UnderdogMultiplier(teamTier);
        double vsExpectation = Math.Clamp(expectedFinish - effectiveFinish, -5.0, 5.0) * 0.5;
        double podium = classifiedPosition switch
        {
            1 => 4.0,
            2 => 2.0,
            3 => 1.0,
            _ => 0.0,
        };
        return (vsExpectation + podium) * multiplier + (beatTeammate ? 1.0 : 0.0);
    }

    /// <summary>Season-end bonus for championship position, tier-scaled.</summary>
    public static double SeasonDelta(int? championshipPosition, int teamTier)
    {
        double multiplier = UnderdogMultiplier(teamTier);
        double bonus = championshipPosition switch
        {
            1 => 10.0,
            2 or 3 => 6.0,
            <= 10 => 3.0,
            _ => 0.0, // null (unclassified/excluded) or outside the top 10
        };
        return bonus * multiplier;
    }

    public static double Apply(double reputation, double delta) =>
        Math.Clamp(reputation + delta, 0.0, 100.0);
}
