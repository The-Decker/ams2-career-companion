using Companion.Core.Career;
using Companion.Core.Character;

namespace Companion.Tests.Career;

public sealed class CharacterProgressionV2MathTests
{
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string HashC = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    private static PinnedCampaignSeason Season(string packId, int year, int rounds, string hash) => new()
    {
        PackId = packId,
        PackVersion = "1.0.0",
        Sha256 = hash,
        Year = year,
        ChampionshipRoundCount = rounds,
    };

    private static CampaignProgressionPlan FractionalPlan() => CampaignProgressionPlan.Create(
        CareerExperienceModes.GrandPrixDynasty,
        startYear: 1967,
        endYear: 2020,
        [
            Season("f1-1967", 1967, 11, HashA),
            Season("f1-1968", 1968, 12, HashB),
            Season("f1-1970", 1970, 13, HashC),
        ]);

    private static CampaignProgressionPlan IdentityPlan() => CampaignProgressionPlan.CreateSmgp(
        Season("smgp-1", 1990, 16, HashA));

    [Fact]
    public void NormalizeXpAward_IdentityScaleAppliesTheWholeEligibleAward()
    {
        var result = CharacterProgressionV2Math.NormalizeXpAward(
            signedRawXp: 40,
            isEligible: true,
            xpScaleRemainder: 0,
            IdentityPlan());

        Assert.Equal(new CharacterXpAwardNormalization(40, 40, 40, 0, 0), result);
    }

    [Fact]
    public void NormalizeXpAward_CarryConservesTheExactAggregateAcrossPartitions()
    {
        var plan = FractionalPlan();
        Assert.Equal(49, plan.XpScaleNumerator);
        Assert.Equal(5, plan.XpScaleDenominator);

        long[] awards = [1, 0, -9, 2, 7, 11, 3];
        long eligibleTotal = 0;
        long appliedTotal = 0;
        long remainder = 3;

        foreach (long award in awards)
        {
            var result = CharacterProgressionV2Math.NormalizeXpAward(
                award, isEligible: true, remainder, plan);
            Assert.Equal(remainder, result.RemainderBefore);
            eligibleTotal = checked(eligibleTotal + result.EligibleRawXp);
            appliedTotal = checked(appliedTotal + result.AppliedXp);
            remainder = result.RemainderAfter;
        }

        long exactNumerator = checked(eligibleTotal * plan.XpScaleNumerator + 3);
        Assert.Equal(exactNumerator / plan.XpScaleDenominator, appliedTotal);
        Assert.Equal(exactNumerator % plan.XpScaleDenominator, remainder);

        // Replaying the identical sequence from the identical carry is byte-for-byte deterministic.
        long replayApplied = 0;
        long replayRemainder = 3;
        foreach (long award in awards)
        {
            var result = CharacterProgressionV2Math.NormalizeXpAward(
                award, isEligible: true, replayRemainder, plan);
            replayApplied += result.AppliedXp;
            replayRemainder = result.RemainderAfter;
        }
        Assert.Equal((appliedTotal, remainder), (replayApplied, replayRemainder));
    }

    [Fact]
    public void NormalizeXpAward_ExhaustiveSmallPartitionsMatchTheClosedForm()
    {
        var plan = FractionalPlan();
        for (long initialRemainder = 0; initialRemainder < plan.XpScaleDenominator; initialRemainder++)
        {
            for (long firstAward = 0; firstAward <= 50; firstAward++)
            {
                for (long secondAward = 0; secondAward <= 50; secondAward++)
                {
                    var first = CharacterProgressionV2Math.NormalizeXpAward(
                        firstAward, true, initialRemainder, plan);
                    var second = CharacterProgressionV2Math.NormalizeXpAward(
                        secondAward, true, first.RemainderAfter, plan);
                    long exactNumerator =
                        (firstAward + secondAward) * plan.XpScaleNumerator + initialRemainder;

                    Assert.Equal(
                        exactNumerator / plan.XpScaleDenominator,
                        first.AppliedXp + second.AppliedXp);
                    Assert.Equal(
                        exactNumerator % plan.XpScaleDenominator,
                        second.RemainderAfter);
                }
            }
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, true)]
    [InlineData(long.MinValue, true)]
    [InlineData(40, false)]
    public void NormalizeXpAward_ZeroNegativeOrIneligibleAwardsPreserveCarry(
        long signedRawXp,
        bool isEligible)
    {
        var result = CharacterProgressionV2Math.NormalizeXpAward(
            signedRawXp, isEligible, xpScaleRemainder: 4, FractionalPlan());

        Assert.Equal(signedRawXp, result.SignedRawXp);
        Assert.Equal(0, result.EligibleRawXp);
        Assert.Equal(0, result.AppliedXp);
        Assert.Equal(4, result.RemainderBefore);
        Assert.Equal(4, result.RemainderAfter);
    }

    [Fact]
    public void NormalizeXpAward_RejectsInvalidScaleCarryAndCheckedOverflow()
    {
        var plan = FractionalPlan();

        Assert.Throws<ArgumentNullException>(() =>
            CharacterProgressionV2Math.NormalizeXpAward(1, true, 0, null!));
        Assert.Throws<InvalidOperationException>(() =>
            CharacterProgressionV2Math.NormalizeXpAward(
                1, true, 0, plan with { XpScaleNumerator = 0 }));
        Assert.Throws<InvalidOperationException>(() =>
            CharacterProgressionV2Math.NormalizeXpAward(
                1, true, 0, plan with { XpScaleDenominator = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CharacterProgressionV2Math.NormalizeXpAward(1, true, -1, plan));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CharacterProgressionV2Math.NormalizeXpAward(
                1, true, plan.XpScaleDenominator, plan));
        Assert.Throws<OverflowException>(() =>
            CharacterProgressionV2Math.NormalizeXpAward(long.MaxValue, true, 0, plan));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(30, 48)]
    [InlineData(90, 148)]
    [InlineData(165, 273)]
    [InlineData(240, 398)]
    [InlineData(300, 499)]
    public void LevelPool_HitsTheLockedMilestones(int level, int expected)
    {
        Assert.Equal(expected, CharacterProgressionV2Math.LevelPool(level));
    }

    [Fact]
    public void LevelPool_ClampsAtBothEndsAndAdvancesOnlyOneOrTwoPointsPerLevel()
    {
        Assert.Equal(0, CharacterProgressionV2Math.LevelPool(int.MinValue));
        Assert.Equal(0, CharacterProgressionV2Math.LevelPool(0));
        Assert.Equal(499, CharacterProgressionV2Math.LevelPool(301));
        Assert.Equal(499, CharacterProgressionV2Math.LevelPool(int.MaxValue));

        int previous = CharacterProgressionV2Math.LevelPool(1);
        for (int level = 2; level <= CharacterLevelProgression.Level300Max; level++)
        {
            int current = CharacterProgressionV2Math.LevelPool(level);
            Assert.InRange(current - previous, 1, 2);
            previous = current;
        }
        Assert.Equal(CharacterProgressionV2Math.LifetimeSkillPoints, previous);
    }

    [Theory]
    [InlineData(0, 16, 0)]
    [InlineData(1, 16, 31)]
    [InlineData(5, 16, 155)]
    [InlineData(10, 16, 311)]
    [InlineData(15, 16, 467)]
    [InlineData(16, 16, 499)]
    public void SeasonPool_HitsTheLockedSmgpSchedule(
        int completedSeasons,
        int masterySeason,
        int expected)
    {
        Assert.Equal(expected,
            CharacterProgressionV2Math.SeasonPool(completedSeasons, masterySeason));
    }

    [Fact]
    public void SeasonPool_ClampsAndRejectsAZeroMasteryHorizon()
    {
        Assert.Equal(0, CharacterProgressionV2Math.SeasonPool(int.MinValue, 16));
        Assert.Equal(499, CharacterProgressionV2Math.SeasonPool(17, 16));
        Assert.Equal(499, CharacterProgressionV2Math.SeasonPool(int.MaxValue, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CharacterProgressionV2Math.SeasonPool(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CharacterProgressionV2Math.SeasonPool(0, -1));
    }

    [Fact]
    public void SkillPoints_UsesTheLowerPacingGateAndReachesExactly499AtMastery()
    {
        var phaseLimited = CharacterProgressionV2Math.SkillPoints(
            level: 300,
            completedSeasons: 10,
            masterySeason: 16,
            skillPointsSpent: 100);
        Assert.Equal(new CharacterSkillPointBalance(499, 311, 311, 100, 211), phaseLimited);

        var levelLimited = CharacterProgressionV2Math.SkillPoints(
            level: 90,
            completedSeasons: 16,
            masterySeason: 16,
            skillPointsSpent: 0);
        Assert.Equal(new CharacterSkillPointBalance(148, 499, 148, 0, 148), levelLimited);

        var mastered = CharacterProgressionV2Math.SkillPoints(
            level: 300,
            completedSeasons: 16,
            masterySeason: 16,
            skillPointsSpent: 0);
        Assert.Equal(499, mastered.Earned);
        Assert.Equal(499, mastered.Available);
    }

    [Fact]
    public void SkillPoints_ClampsOverspendingToZeroAndRejectsNegativeSpend()
    {
        var exactlySpent = CharacterProgressionV2Math.SkillPoints(90, 16, 16, 148);
        var overspent = CharacterProgressionV2Math.SkillPoints(90, 16, 16, int.MaxValue);

        Assert.Equal(0, exactlySpent.Available);
        Assert.Equal(0, overspent.Available);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CharacterProgressionV2Math.SkillPoints(90, 16, 16, -1));
    }

    [Fact]
    public void SkillPoints_OneSeasonCampaignUnlocksPhasePoolOnlyAtCompletion()
    {
        Assert.Equal(0,
            CharacterProgressionV2Math.SkillPoints(300, 0, 1, 0).Earned);
        Assert.Equal(499,
            CharacterProgressionV2Math.SkillPoints(300, 1, 1, 0).Earned);
    }
}
