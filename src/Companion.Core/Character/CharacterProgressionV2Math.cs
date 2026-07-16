using Companion.Core.Career;

namespace Companion.Core.Character;

/// <summary>The auditable result of scaling one signed v2 XP award through a pinned campaign plan.</summary>
public readonly record struct CharacterXpAwardNormalization(
    long SignedRawXp,
    long EligibleRawXp,
    long AppliedXp,
    long RemainderBefore,
    long RemainderAfter);

/// <summary>The two v2 pacing gates and the resulting lifetime Skill Point balance.</summary>
public readonly record struct CharacterSkillPointBalance(
    int LevelPool,
    int SeasonPool,
    int Earned,
    int Spent,
    int Available);

/// <summary>
/// Pure version-2 progression arithmetic. XP uses exact integer carry through the immutable campaign
/// scale; Skill Points use the proportional 499-point level pool constrained by campaign phase.
/// This type does not mutate career state, journal events, or legacy v0/v1 progression.
/// </summary>
public static class CharacterProgressionV2Math
{
    public const int LifetimeSkillPoints = 499;

    /// <summary>
    /// Scales one raw award with floor division and returns the carry needed by the next award.
    /// Ineligible and negative awards contribute zero without consuming an existing carry.
    /// </summary>
    public static CharacterXpAwardNormalization NormalizeXpAward(
        long signedRawXp,
        bool isEligible,
        long xpScaleRemainder,
        CampaignProgressionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        long numerator = plan.XpScaleNumerator;
        long denominator = plan.XpScaleDenominator;
        if (numerator <= 0)
            throw new InvalidOperationException("Campaign XP scale numerator must be positive.");
        if (denominator <= 0)
            throw new InvalidOperationException("Campaign XP scale denominator must be positive.");
        if (xpScaleRemainder < 0 || xpScaleRemainder >= denominator)
        {
            throw new ArgumentOutOfRangeException(
                nameof(xpScaleRemainder),
                xpScaleRemainder,
                $"XP scale remainder must be in the range 0..{denominator - 1}.");
        }

        long eligibleRawXp = isEligible ? Math.Max(0L, signedRawXp) : 0L;
        long scaledNumerator = checked(checked(eligibleRawXp * numerator) + xpScaleRemainder);
        long appliedXp = scaledNumerator / denominator;
        long nextRemainder = scaledNumerator % denominator;

        return new CharacterXpAwardNormalization(
            signedRawXp,
            eligibleRawXp,
            appliedXp,
            xpScaleRemainder,
            nextRemainder);
    }

    /// <summary>Lifetime SP unlocked by level alone, clamped to levels 1 through 300.</summary>
    public static int LevelPool(int level)
    {
        long completedLevelUps = Math.Clamp(
            (long)level - 1L,
            0L,
            CharacterLevelProgression.Level300Max - 1L);
        return (int)(LifetimeSkillPoints * completedLevelUps /
            (CharacterLevelProgression.Level300Max - 1L));
    }

    /// <summary>Lifetime SP permitted by completed campaign seasons.</summary>
    public static int SeasonPool(int completedSeasons, int masterySeason)
    {
        if (masterySeason <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(masterySeason), masterySeason, "Mastery season must be positive.");
        }

        long clampedSeasons = Math.Clamp((long)completedSeasons, 0L, masterySeason);
        return (int)(LifetimeSkillPoints * clampedSeasons / masterySeason);
    }

    /// <summary>
    /// Computes the authoritative v2 SP balance. Overspending clamps availability to zero; a
    /// negative persisted spend is invalid rather than being allowed to manufacture Skill Points.
    /// </summary>
    public static CharacterSkillPointBalance SkillPoints(
        int level,
        int completedSeasons,
        int masterySeason,
        int skillPointsSpent)
    {
        if (skillPointsSpent < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(skillPointsSpent), skillPointsSpent, "Spent Skill Points cannot be negative.");
        }

        int levelPool = LevelPool(level);
        int seasonPool = SeasonPool(completedSeasons, masterySeason);
        int earned = Math.Min(levelPool, seasonPool);
        int available = Math.Max(0, earned - skillPointsSpent);
        return new CharacterSkillPointBalance(
            levelPool,
            seasonPool,
            earned,
            skillPointsSpent,
            available);
    }
}
