using Companion.Core.Packs;

namespace Companion.Tests.Packs;

public class RoundsRangeTests
{
    // ---------- happy paths ----------

    [Fact]
    public void Parse_SingleNumber()
    {
        var range = RoundsRange.Parse("4");

        Assert.Equal(new[] { 4 }, range.Rounds);
        Assert.Equal(4, range.Min);
        Assert.Equal(4, range.Max);
        Assert.True(range.Contains(4));
        Assert.False(range.Contains(3));
        Assert.False(range.Contains(5));
    }

    [Fact]
    public void Parse_SimpleSpan()
    {
        var range = RoundsRange.Parse("1-11");

        Assert.Equal(Enumerable.Range(1, 11).ToArray(), range.Rounds);
        Assert.Equal(1, range.Min);
        Assert.Equal(11, range.Max);
        Assert.True(range.Contains(1));
        Assert.True(range.Contains(11));
        Assert.False(range.Contains(0));
        Assert.False(range.Contains(12));
    }

    [Fact]
    public void Parse_MixedListAndSpans()
    {
        var range = RoundsRange.Parse("1,3,5-8");

        Assert.Equal(new[] { 1, 3, 5, 6, 7, 8 }, range.Rounds);
        Assert.False(range.Contains(2));
        Assert.False(range.Contains(4));
        Assert.True(range.Contains(6));
    }

    [Fact]
    public void Parse_ToleratesWhitespace()
    {
        var range = RoundsRange.Parse(" 1 , 3 - 5 ");

        Assert.Equal(new[] { 1, 3, 4, 5 }, range.Rounds);
    }

    [Fact]
    public void Parse_MergesOverlapsAndDuplicates()
    {
        var range = RoundsRange.Parse("1-3,2,3,2-4");

        Assert.Equal(new[] { 1, 2, 3, 4 }, range.Rounds);
    }

    [Fact]
    public void Parse_SortsUnorderedSegments()
    {
        var range = RoundsRange.Parse("5-8,1,3");

        Assert.Equal(new[] { 1, 3, 5, 6, 7, 8 }, range.Rounds);
    }

    [Fact]
    public void Parse_SingleRoundSpan_IsValid()
    {
        var range = RoundsRange.Parse("7-7");

        Assert.Equal(new[] { 7 }, range.Rounds);
    }

    // ---------- canonical ToString ----------

    [Theory]
    [InlineData("1-11", "1-11")]
    [InlineData("4", "4")]
    [InlineData("1,3,5-8", "1,3,5-8")]
    [InlineData("1,2,3", "1-3")]
    [InlineData("5-8,1,3", "1,3,5-8")]
    [InlineData("1-3,2-5", "1-5")]
    [InlineData("2,1", "1-2")]
    public void ToString_ProducesCanonicalCompactForm(string input, string expected) =>
        Assert.Equal(expected, RoundsRange.Parse(input).ToString());

    [Fact]
    public void ToString_RoundTripsThroughParse()
    {
        var range = RoundsRange.Parse("9,1-4,6");
        var reparsed = RoundsRange.Parse(range.ToString());

        Assert.Equal(range.Rounds, reparsed.Rounds);
    }

    // ---------- invalid inputs ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]            // rounds are 1-based
    [InlineData("0-3")]
    [InlineData("3-1")]          // backwards
    [InlineData("1-")]
    [InlineData("-2")]
    [InlineData("a")]
    [InlineData("1,x,3")]
    [InlineData("1,,3")]
    [InlineData(",1")]
    [InlineData("1--3")]
    [InlineData("+3")]           // no sign characters
    [InlineData("1.5")]
    [InlineData("1 2")]
    [InlineData("1-2-3")]
    public void Parse_InvalidInput_Throws(string? text)
    {
        Assert.Throws<FormatException>(() => RoundsRange.Parse(text));
        Assert.False(RoundsRange.TryParse(text, out _));
    }

    [Fact]
    public void TryParse_InvalidInput_ReportsTheOffendingSegment()
    {
        Assert.False(RoundsRange.TryParse("1,3-1,5", out _, out var error));
        Assert.Contains("3-1", error);
    }

    [Fact]
    public void Parse_AbsurdlyLargeSpan_IsRejected()
    {
        Assert.False(RoundsRange.TryParse("1-1000000", out _, out var error));
        Assert.Contains("spans", error);
    }
}
