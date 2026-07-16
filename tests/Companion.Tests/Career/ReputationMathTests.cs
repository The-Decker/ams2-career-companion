using Companion.Core.Career;
using Companion.Core.Character;

namespace Companion.Tests.Career;

public class ReputationMathTests
{
    [Fact]
    public void GainsScaleUpAsTheCarGetsWeaker()
    {
        // Tier 5 is the richest tier in this codebase (Ferrari 1967 = 5); the underdog
        // multiplier therefore grows as the tier falls: a podium in a minnow is worth
        // double one in the class of the field.
        Assert.Equal(1.0, ReputationMath.UnderdogMultiplier(5));
        Assert.Equal(1.5, ReputationMath.UnderdogMultiplier(3));
        Assert.Equal(2.0, ReputationMath.UnderdogMultiplier(1));
    }

    [Fact]
    public void PodiumInATierOneCarBeatsTheSamePodiumInATierFiveCar()
    {
        double minnow = ReputationMath.RoundDelta(10.0, 3.0, 3, beatTeammate: false, teamTier: 1);
        double works = ReputationMath.RoundDelta(10.0, 3.0, 3, beatTeammate: false, teamTier: 5);
        Assert.True(minnow > works,
            $"Tier-1 podium ({minnow}) must out-earn a tier-5 podium ({works}).");
        Assert.Equal(2.0 * works, minnow, 12);
    }

    [Fact]
    public void BeatingTheTeammateIsAFlatBonus()
    {
        double without = ReputationMath.RoundDelta(5.0, 5.0, 5, beatTeammate: false, teamTier: 3);
        double with = ReputationMath.RoundDelta(5.0, 5.0, 5, beatTeammate: true, teamTier: 3);
        Assert.Equal(1.0, with - without, 12);
    }

    [Fact]
    public void ExpectationDeltaIsClampedToPlusMinusFive()
    {
        // Finishing 20 places up scores the same as 5 places up.
        double hugeGain = ReputationMath.RoundDelta(26.0, 6.0, 6, false, 3);
        double clampGain = ReputationMath.RoundDelta(11.0, 6.0, 6, false, 3);
        Assert.Equal(clampGain, hugeGain, 12);
    }

    [Fact]
    public void LegacyV0RoundRateRetainsShippedArithmeticAndIgnoresMasteryOnlyFields()
    {
        var v0 = new PlayerPerkModifiers
        {
            RepRoundMult = 2.0,
            RepRoundGainMult = 0.10,
            RepRoundSignedMult = 0.10,
        };

        Assert.Equal(1.0, ReputationMath.RoundDelta(7, 5, null, false, 5), 12);
        Assert.Equal(2.0, ReputationMath.RoundDelta(7, 5, null, false, 5, v0), 12);
        Assert.Equal(-2.0, ReputationMath.RoundDelta(3, 5, null, false, 5, v0), 12);
    }

    [Fact]
    public void ActiveMasteryRoundRate_AppliesGainOnlyToPositiveDeltaAndSignedRateToBothSigns()
    {
        var active = new PlayerPerkModifiers
        {
            MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
            RepRoundGainMult = 1.20,
            RepRoundSignedMult = 0.90,
        };

        Assert.Equal(1.08, ReputationMath.RoundDelta(7, 5, null, false, 5, active), 12);
        Assert.Equal(-0.90, ReputationMath.RoundDelta(3, 5, null, false, 5, active), 12);
    }

    [Fact]
    public void ActiveMasteryRoundRate_ClampsCombinedRateBeforeMarketability()
    {
        var upper = new PlayerPerkModifiers
        {
            MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
            RepRoundMult = 1.50,
            RepRoundGainMult = 1.50,
            RepRoundSignedMult = 1.50,
            Marketability = 1.0,
        };
        var lower = new PlayerPerkModifiers
        {
            MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
            RepRoundMult = 0.50,
            RepRoundGainMult = 0.50,
            RepRoundSignedMult = 0.50,
        };

        // Rate caps at 1.40 before the 1.25 marketability factor.
        Assert.Equal(1.75, ReputationMath.RoundDelta(7, 5, null, false, 5, upper), 12);
        Assert.Equal(-1.75, ReputationMath.RoundDelta(3, 5, null, false, 5, upper), 12);
        Assert.Equal(0.60, ReputationMath.RoundDelta(7, 5, null, false, 5, lower), 12);
        Assert.Equal(-0.60, ReputationMath.RoundDelta(3, 5, null, false, 5, lower), 12);
    }

    [Fact]
    public void SeasonBonusByChampionshipPosition()
    {
        Assert.Equal(10.0, ReputationMath.SeasonDelta(1, 5), 12);
        Assert.Equal(6.0, ReputationMath.SeasonDelta(2, 5), 12);
        Assert.Equal(6.0, ReputationMath.SeasonDelta(3, 5), 12);
        Assert.Equal(3.0, ReputationMath.SeasonDelta(10, 5), 12);
        Assert.Equal(0.0, ReputationMath.SeasonDelta(11, 5), 12);
        Assert.Equal(0.0, ReputationMath.SeasonDelta(null, 5), 12);

        // Era/tier scaling applies at season end too.
        Assert.Equal(20.0, ReputationMath.SeasonDelta(1, 1), 12);
    }

    [Fact]
    public void ActiveMasterySeasonRateClampsWhileLegacyV0DoesNot()
    {
        var upper = new PlayerPerkModifiers
        {
            MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
            RepSeasonMult = 2.0,
        };
        var lower = new PlayerPerkModifiers
        {
            MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
            RepSeasonMult = 0.20,
        };
        var v0 = new PlayerPerkModifiers { RepSeasonMult = 2.0 };

        Assert.Equal(14.0, ReputationMath.SeasonDelta(1, 5, upper), 12);
        Assert.Equal(6.0, ReputationMath.SeasonDelta(1, 5, lower), 12);
        Assert.Equal(20.0, ReputationMath.SeasonDelta(1, 5, v0), 12);
    }

    [Fact]
    public void ReputationClampsToZeroToHundred()
    {
        Assert.Equal(100.0, ReputationMath.Apply(98.0, 50.0));
        Assert.Equal(0.0, ReputationMath.Apply(2.0, -50.0));
        Assert.Equal(52.5, ReputationMath.Apply(50.0, 2.5), 12);
    }
}
