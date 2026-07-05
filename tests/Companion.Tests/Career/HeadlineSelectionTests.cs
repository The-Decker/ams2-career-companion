using Companion.Core.Career;
using Companion.Core.Determinism;

namespace Companion.Tests.Career;

public class HeadlineSelectionTests
{
    private static readonly IReadOnlyDictionary<string, string> Tokens =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["player"] = "Pat Player",
            ["team"] = "Mid Team",
            ["race"] = "Test Grand Prix",
            ["position"] = "3rd",
            ["year"] = "1967",
        };

    [Fact]
    public void SelectionIsDeterministicForTheSameStreamKey()
    {
        var bank = CareerTestData.LoadHeadlines();
        var factory = new StreamFactory(42);

        string? first = HeadlineSelector.Select(
            bank, "race.result", "win", 1967, Tokens,
            factory.CreateStream(CareerStreams.Headlines, 1967, 1, "race"));
        string? second = HeadlineSelector.Select(
            bank, "race.result", "win", 1967, Tokens,
            factory.CreateStream(CareerStreams.Headlines, 1967, 1, "race"));

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentRoundsCanSelectDifferentVariants()
    {
        var bank = CareerTestData.LoadHeadlines();
        var factory = new StreamFactory(42);

        var texts = new HashSet<string>(StringComparer.Ordinal);
        for (int round = 1; round <= 12; round++)
        {
            texts.Add(HeadlineSelector.Select(
                bank, "race.result", "win", 1967, Tokens,
                factory.CreateStream(CareerStreams.Headlines, 1967, round, "race"))!);
        }
        Assert.True(texts.Count > 1, "Twelve rounds should not all pick the same variant.");
    }

    [Fact]
    public void TokensAreSubstituted()
    {
        var bank = CareerTestData.LoadHeadlines();
        string? text = HeadlineSelector.Select(
            bank, "race.result", "win", 1967, Tokens,
            new StreamFactory(7).CreateStream(CareerStreams.Headlines, 1967, 1, "race"));

        Assert.NotNull(text);
        Assert.DoesNotContain("{player}", text);
        Assert.DoesNotContain("{race}", text);
    }

    [Fact]
    public void UnknownKeyYieldsNullNotAnException()
    {
        var bank = CareerTestData.LoadHeadlines();
        Assert.Null(HeadlineSelector.Select(
            bank, "no.such.phase", "no-cause", 1967, Tokens,
            new StreamFactory(7).CreateStream(CareerStreams.Headlines, 1967, 1, "race")));
    }

    [Fact]
    public void SubstitutedValuesAreNeverReScanned()
    {
        // A player named "{team} Kid" must print literally: substitution is a single pass
        // over the TEMPLATE — values never re-enter the scan.
        var bank = CareerTestData.LoadHeadlines();
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["player"] = "{team} Kid",
            ["team"] = "Mid Team",
            ["race"] = "Test Grand Prix",
            ["position"] = "1st",
            ["year"] = "1967",
        };

        // Every race.result|win variant (both eras) carries {player}; run them all via
        // twelve stream keys to cover several selections.
        for (int round = 1; round <= 12; round++)
        {
            string? text = HeadlineSelector.Select(
                bank, "race.result", "win", 1967, tokens,
                new StreamFactory(42).CreateStream(CareerStreams.Headlines, 1967, round, "race"));
            Assert.NotNull(text);
            Assert.Contains("{team} Kid", text);
            Assert.DoesNotContain("Mid Team Kid", text);
        }
    }

    [Fact]
    public void UnknownTokenThrowsAtSelectionTime()
    {
        // Template bugs must surface in tests, not journals: a template referencing a token
        // the sim never supplies is a loud failure.
        var bank = HeadlineBank.Parse("""
            {
              "eras": [],
              "templates": {
                "race.result|win": { "default": ["{bogus} wins {race}"] }
              }
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => HeadlineSelector.Select(
            bank, "race.result", "win", 1967, Tokens,
            new StreamFactory(7).CreateStream(CareerStreams.Headlines, 1967, 1, "race")));
        Assert.Contains("{bogus}", ex.Message);
    }

    [Fact]
    public void EraResolutionFallsBackToDefaultOutsideAuthoredRanges()
    {
        var bank = CareerTestData.LoadHeadlines();
        Assert.Equal("1960s", bank.ResolveEra(1967));
        Assert.Equal(HeadlineBank.DefaultEra, bank.ResolveEra(2050));
    }

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(22, "22nd")]
    public void OrdinalsAreEnglish(int position, string expected)
    {
        Assert.Equal(expected, HeadlineSelector.Ordinal(position));
    }
}
