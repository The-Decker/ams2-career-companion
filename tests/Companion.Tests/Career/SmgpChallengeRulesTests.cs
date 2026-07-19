using Companion.Core.Smgp;

namespace Companion.Tests.Career;

/// <summary>
/// The SMGP challenge-tier rule (Mike, 2026-07-12): you may challenge your OWN tier, the ONE tier
/// directly above (the seat you climb toward), and ANY tier below, never two tiers up. So D→{D,C};
/// C→{B,C,D}; B→{A,B,C,D}; A→everyone. (Your own teammate is excluded separately by the briefing.) Pure.
/// </summary>
public sealed class SmgpChallengeRulesTests
{
    [Theory]
    // From D: D (own) and C (the tier above); NOT B/A (two+ up).
    [InlineData('D', 'D', true)]
    [InlineData('D', 'C', true)]
    [InlineData('D', 'B', false)]
    [InlineData('D', 'A', false)]
    // From C: B (above), C (own), D (below); NOT A (two up).
    [InlineData('C', 'B', true)]
    [InlineData('C', 'C', true)]
    [InlineData('C', 'D', true)]
    [InlineData('C', 'A', false)]
    // From B: A (above), B (own), C, D (below), everyone at or below A.
    [InlineData('B', 'A', true)]
    [InlineData('B', 'B', true)]
    [InlineData('B', 'C', true)]
    [InlineData('B', 'D', true)]
    // From A: A (own) and all below, everyone.
    [InlineData('A', 'A', true)]
    [InlineData('A', 'B', true)]
    [InlineData('A', 'C', true)]
    [InlineData('A', 'D', true)]
    public void CanChallenge_MatchesTheTierRule(char player, char rival, bool expected) =>
        Assert.Equal(expected, SmgpRules.CanChallenge(player, rival));

    [Fact]
    public void TierAboveAndBelow_ChainCorrectly()
    {
        Assert.Equal('C', SmgpRules.TierAbove('D'));
        Assert.Equal('A', SmgpRules.TierAbove('B'));
        Assert.Null(SmgpRules.TierAbove('A'));
        Assert.Equal('D', SmgpRules.TierBelow('C'));
        Assert.Null(SmgpRules.TierBelow('D'));
    }

    [Fact]
    public void FloorLossLimit_IsFour() => Assert.Equal(4, SmgpRules.FloorLossLimit);
}
