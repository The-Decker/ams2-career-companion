using Companion.Core.Smgp;

namespace Companion.Tests.Career;

/// <summary>
/// The SMGP challenge-tier rule (Mike): you may challenge the ONE tier directly above you (the
/// seat you climb toward) or ANY tier below — never two tiers up, never your own tier. Pure.
/// </summary>
public sealed class SmgpChallengeRulesTests
{
    [Theory]
    // From D: only C (the tier above).
    [InlineData('D', 'C', true)]
    [InlineData('D', 'B', false)]
    [InlineData('D', 'A', false)]
    [InlineData('D', 'D', false)]
    // From C: B (above) or D (below); NOT A (two up) or C (own).
    [InlineData('C', 'B', true)]
    [InlineData('C', 'D', true)]
    [InlineData('C', 'A', false)]
    [InlineData('C', 'C', false)]
    // From B: A, C, D; NOT B (own).
    [InlineData('B', 'A', true)]
    [InlineData('B', 'C', true)]
    [InlineData('B', 'D', true)]
    [InlineData('B', 'B', false)]
    // From A: B, C, D (all below); nothing above.
    [InlineData('A', 'B', true)]
    [InlineData('A', 'C', true)]
    [InlineData('A', 'D', true)]
    [InlineData('A', 'A', false)]
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
