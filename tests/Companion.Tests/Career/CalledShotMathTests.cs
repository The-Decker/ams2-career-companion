using Companion.Core.Career;

namespace Companion.Tests.Career;

/// <summary>The pure "Setup Gamble" called-shot math. Deterministic (a function of the call + result
/// + expected finish), so these lock the arithmetic a replay must reproduce, and prove the
/// anti-exploit: a call no more ambitious than the expected finish stakes — and pays — nothing.</summary>
public sealed class CalledShotMathTests
{
    [Fact]
    public void Stake_IsZero_ForANonGamble()
    {
        // Expected P10. Calling P10 (break-even) or P15 (behind expectation) is not a gamble.
        Assert.Equal(0.0, CalledShotMath.Stake(calledPosition: 10, expectedFinish: 10));
        Assert.Equal(0.0, CalledShotMath.Stake(calledPosition: 15, expectedFinish: 10));
        Assert.False(CalledShotMath.IsGamble(10, 10));
        Assert.False(CalledShotMath.IsGamble(15, 10));
    }

    [Fact]
    public void Stake_ScalesWithAmbition_AndCaps()
    {
        // The first ambitious place is the base; each further place adds one.
        Assert.Equal(3.0, CalledShotMath.Stake(9, 10), 6);   // 1 better than P10 → base 3
        Assert.Equal(6.0, CalledShotMath.Stake(6, 10), 6);   // 4 better → 3 + 3 = 6
        Assert.Equal(12.0, CalledShotMath.Stake(1, 15), 6);  // 14 better → 3 + 13 = 16, capped at 12
        Assert.True(CalledShotMath.IsGamble(9, 10));
    }

    [Fact]
    public void Hit_RequiresAClassifiedFinishAtOrAheadOfTheCall()
    {
        Assert.True(CalledShotMath.Hit(calledPosition: 5, actualFinish: 3)); // beat the call
        Assert.True(CalledShotMath.Hit(5, 5));                               // met the call exactly
        Assert.False(CalledShotMath.Hit(5, 6));                              // fell short
        Assert.False(CalledShotMath.Hit(5, actualFinish: null));            // a DNF always misses
    }

    [Fact]
    public void ReputationDelta_IsPlusStakeOnAHit_MinusStakeOnAMiss()
    {
        // Expected P10, called P6 (stake 6). Finish P4 (hit) → +6; finish P8 (miss) → −6; DNF → −6.
        Assert.Equal(6.0, CalledShotMath.ReputationDelta(6, actualFinish: 4, expectedFinish: 10), 6);
        Assert.Equal(-6.0, CalledShotMath.ReputationDelta(6, actualFinish: 8, expectedFinish: 10), 6);
        Assert.Equal(-6.0, CalledShotMath.ReputationDelta(6, actualFinish: null, expectedFinish: 10), 6);

        // A non-gamble resolves to exactly 0 either way — no free reputation, no penalty.
        Assert.Equal(0.0, CalledShotMath.ReputationDelta(12, actualFinish: 11, expectedFinish: 10), 6);
        Assert.Equal(0.0, CalledShotMath.ReputationDelta(12, actualFinish: 20, expectedFinish: 10), 6);
    }

    [Fact]
    public void XpBonus_PaysOnlyOnAHit_ScaledByStake()
    {
        // Expected P10, called P6 (stake 6): a hit banks round(6 * 4) = 24 XP; a miss banks 0.
        Assert.Equal(24, CalledShotMath.XpBonus(6, actualFinish: 4, expectedFinish: 10));
        Assert.Equal(0, CalledShotMath.XpBonus(6, actualFinish: 8, expectedFinish: 10));
        Assert.Equal(0, CalledShotMath.XpBonus(6, actualFinish: null, expectedFinish: 10));
        // A non-gamble hit pays no XP (stake 0).
        Assert.Equal(0, CalledShotMath.XpBonus(12, actualFinish: 1, expectedFinish: 10));
    }
}
