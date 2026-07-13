using Companion.Core.Character;

namespace Companion.Tests.Career;

public sealed class CharacterLevelProgressionTests
{
    private static CharacterRules Rules() =>
        CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

    [Fact]
    public void VersionsZeroAndOne_PreserveTheShippedGeometricCurveAndEraCap()
    {
        var rules = Rules();
        foreach (int level in Enumerable.Range(1, rules.Levels.XpCurve.MaxLevel))
        {
            long shipped = rules.Levels.XpCurve.XpForLevel(level);
            Assert.Equal(shipped, CharacterLevelProgression.XpForLevel(0, level, rules));
            Assert.Equal(shipped, CharacterLevelProgression.XpForLevel(1, level, rules));
        }

        Assert.Equal(30, CharacterLevelProgression.LevelForTotalXp(0, long.MaxValue, 1967, rules));
        Assert.Equal(26, CharacterLevelProgression.LevelForTotalXp(1, long.MaxValue, 1967, rules));
        Assert.Equal(30, CharacterLevelProgression.LevelForTotalXp(1, long.MaxValue, 2022, rules));
    }

    [Fact]
    public void VersionTwo_HasTheLockedIntegerL300Curve()
    {
        var rules = Rules();

        Assert.Equal(40, CharacterLevelProgression.XpForLevel(2, 2, rules));
        Assert.Equal(46, CharacterLevelProgression.XpForLevel(2, 100, rules));
        Assert.Equal(50, CharacterLevelProgression.XpForLevel(2, 150, rules));
        Assert.Equal(53, CharacterLevelProgression.XpForLevel(2, 200, rules));
        Assert.Equal(57, CharacterLevelProgression.XpForLevel(2, 250, rules));
        Assert.Equal(61, CharacterLevelProgression.XpForLevel(2, 300, rules));
        Assert.Equal(14_951, CharacterLevelProgression.CumulativeXpToLevel(2, 300, rules));

        long previousStep = 0;
        long previousCumulative = 0;
        for (int level = 2; level <= CharacterLevelProgression.Level300Max; level++)
        {
            long step = CharacterLevelProgression.XpForLevel(2, level, rules);
            long cumulative = CharacterLevelProgression.CumulativeXpToLevel(2, level, rules);
            Assert.True(step > 0);
            Assert.True(step >= previousStep);
            Assert.True(cumulative > previousCumulative);
            previousStep = step;
            previousCumulative = cumulative;
        }
    }

    [Theory]
    [InlineData(14_889, 298)]
    [InlineData(14_890, 299)]
    [InlineData(14_950, 299)]
    [InlineData(14_951, 300)]
    [InlineData(long.MaxValue, 300)]
    public void VersionTwo_LevelLookupHitsTheExactCapBoundaries(long xp, int expectedLevel)
    {
        var rules = Rules();
        Assert.Equal(expectedLevel, CharacterLevelProgression.LevelForTotalXp(2, xp, 1967, rules));
        Assert.Equal(expectedLevel, CharacterLevelProgression.LevelForTotalXp(2, xp, 2022, rules));
    }

    [Fact]
    public void UnsupportedProgressionVersionFailsExplicitly()
    {
        var rules = Rules();
        Assert.Throws<NotSupportedException>(() =>
            CharacterLevelProgression.LevelForTotalXp(3, 0, 1967, rules));
        Assert.Throws<NotSupportedException>(() =>
            CharacterLevelProgression.MaxLevel(-1, 1967, rules));
    }
}
