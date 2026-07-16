using Companion.Core.Character;

namespace Companion.Tests.Career;

/// <summary>
/// The version-2 (L300) integer curve's MID-CURVE boundaries, exact to the XP point: for each of
/// the display-significant levels 100/150/200/250, the cumulative-XP threshold is precisely where
/// <see cref="CharacterLevelProgression.LevelForTotalXp"/> flips from L-1 to L (99→100, 149→150,
/// 199→200, 249→250) — and a single award crossing four-plus levels lands on the exact level the
/// cumulative table says, proving the lookup and the cumulative sum can never disagree mid-jump.
/// Complements CharacterLevelProgressionTests, which pins the 298/299/300 cap boundaries.
/// </summary>
public sealed class CharacterLevelProgressionMidBoundaryTests
{
    private static CharacterRules Rules() =>
        CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(200)]
    [InlineData(250)]
    public void VersionTwo_MidCurveThresholdsFlipAtTheExactCumulativeXp(int level)
    {
        var rules = Rules();
        long xpAtLevel = CharacterLevelProgression.CumulativeXpToLevel(2, level, rules);

        // Exactly the cumulative threshold reaches the level; one XP less stays a level short —
        // in every era (the v2 curve is year-independent).
        Assert.Equal(level, CharacterLevelProgression.LevelForTotalXp(2, xpAtLevel, 1967, rules));
        Assert.Equal(level, CharacterLevelProgression.LevelForTotalXp(2, xpAtLevel, 2022, rules));
        Assert.Equal(level - 1, CharacterLevelProgression.LevelForTotalXp(2, xpAtLevel - 1, 1967, rules));
        Assert.Equal(level - 1, CharacterLevelProgression.LevelForTotalXp(2, xpAtLevel - 1, 2022, rules));
    }

    [Fact]
    public void VersionTwo_ASingleAwardCrossingFourLevelsLandsExactlyOnTheCumulativeTable()
    {
        var rules = Rules();

        // Just below the first threshold: still level 1.
        long justBelowLevel2 = CharacterLevelProgression.CumulativeXpToLevel(2, 2, rules) - 1;
        Assert.Equal(1, CharacterLevelProgression.LevelForTotalXp(2, justBelowLevel2, 1967, rules));

        // One award takes the total to exactly CumulativeXpToLevel(2, 6): a 1 → 6 jump
        // (crossing levels 2, 3, 4, 5 and 6 at once) lands precisely on level 6...
        long toLevel6 = CharacterLevelProgression.CumulativeXpToLevel(2, 6, rules);
        Assert.True(toLevel6 > justBelowLevel2);
        Assert.Equal(6, CharacterLevelProgression.LevelForTotalXp(2, toLevel6, 1967, rules));

        // ...and the boundary is exact: one XP short of the table reads level 5.
        Assert.Equal(5, CharacterLevelProgression.LevelForTotalXp(2, toLevel6 - 1, 1967, rules));
    }
}
