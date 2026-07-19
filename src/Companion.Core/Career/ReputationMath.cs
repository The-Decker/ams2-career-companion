using Companion.Core.Character;

namespace Companion.Core.Career;

/// <summary>
/// Reputation 0–100. Moves on finishing vs expectation, beating the teammate, and season-end
/// championship position; gains scale with how weak the player's car is (contract: a podium
/// in a weak car is worth far more than one in the class of the field, tier 5 is the
/// richest tier in this codebase, so the multiplier grows as tier falls).
///
/// A character's marketability + reputation perks patch the deltas via an optional
/// <see cref="PlayerPerkModifiers"/>; a null modifier (every non-character career) reproduces the
/// shipped formula exactly (docs/dev/character-system.md §2.2, §6.1).
/// </summary>
public static class ReputationMath
{
    /// <summary>1.0 for a tier-5 (top) car up to 2.0 for a tier-1 (minnow) car. Underdog perks can
    /// deepen the low-tier boost or lift the top-tier value; a null modifier keeps the shipped band.</summary>
    public static double UnderdogMultiplier(int budgetTier, PlayerPerkModifiers? mods = null)
    {
        int tier = Math.Clamp(budgetTier, 1, 5);
        double multiplier = 1.0 + 0.25 * (5 - tier);
        if (mods is null)
            return multiplier;
        // Low-tier bonus helps most in weak cars (scaled by how low the tier is); the top-tier
        // multiplier lifts the otherwise-neutral tier-5 case.
        multiplier += mods.UnderdogLowTierBonus * (5 - tier) / 4.0;
        return tier == 5 ? multiplier * mods.TopTierRepMult : multiplier;
    }

    /// <summary>Per-round reputation delta: expectation delta (clamped ±5, half-weight)
    /// and podium bonus both era/tier-scaled; beating the teammate is a flat +1. Marketability +
    /// per-round rep perks scale the whole delta.</summary>
    public static double RoundDelta(
        double expectedFinish,
        double effectiveFinish,
        int? classifiedPosition,
        bool beatTeammate,
        int teamTier,
        PlayerPerkModifiers? mods = null)
    {
        double multiplier = UnderdogMultiplier(teamTier, mods);
        double vsExpectation = Math.Clamp(expectedFinish - effectiveFinish, -5.0, 5.0) * 0.5;
        double podium = classifiedPosition switch
        {
            1 => 4.0,
            2 => 2.0,
            3 => 1.0,
            _ => 0.0,
        };
        double delta = (vsExpectation + podium) * multiplier + (beatTeammate ? 1.0 : 0.0);
        if (mods is null)
            return delta;
        if (mods.MasteryEffectsVersion != CharacterProfile.CurrentMasteryEffectsVersion)
            return delta * mods.RepRoundMult * Marketability(mods);

        double rate = mods.RepRoundMult * mods.RepRoundSignedMult;
        if (delta > 0.0)
            rate *= mods.RepRoundGainMult;
        rate = Math.Clamp(rate, 0.60, 1.40);
        return delta * rate * Marketability(mods);
    }

    /// <summary>Season-end bonus for championship position, tier-scaled.</summary>
    public static double SeasonDelta(int? championshipPosition, int teamTier, PlayerPerkModifiers? mods = null)
    {
        double multiplier = UnderdogMultiplier(teamTier, mods);
        double bonus = championshipPosition switch
        {
            1 => 10.0,
            2 or 3 => 6.0,
            <= 10 => 3.0,
            _ => 0.0, // null (unclassified/excluded) or outside the top 10
        };
        double delta = bonus * multiplier;
        if (mods is null)
            return delta;
        if (mods.MasteryEffectsVersion != CharacterProfile.CurrentMasteryEffectsVersion)
            return delta * mods.RepSeasonMult * Marketability(mods);

        double rate = Math.Clamp(mods.RepSeasonMult, 0.60, 1.40);
        return delta * rate * Marketability(mods);
    }

    /// <summary>The marketability pre-multiplier <c>1.0 + 0.5·(marketability − 0.5)</c>, neutral
    /// at the 0.5 default, so a null/identity modifier does not move the delta.</summary>
    private static double Marketability(PlayerPerkModifiers mods) => 1.0 + 0.5 * (mods.Marketability - 0.5);

    public static double Apply(double reputation, double delta) =>
        Math.Clamp(reputation + delta, 0.0, 100.0);
}
