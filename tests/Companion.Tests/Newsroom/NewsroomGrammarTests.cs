using Companion.Core.Determinism;
using Companion.Core.Newsroom;
using Xunit;

namespace Companion.Tests.Newsroom;

public class NewsroomGrammarTests
{
    private static readonly Dictionary<string, string> Tokens = new(StringComparer.Ordinal)
    {
        ["player"] = "J. Clark",
        ["team"] = "Lotus",
        ["venue"] = "Monza",
        ["position"] = "8",
        ["empty"] = "",
    };

    private static Pcg32 Stream() => new StreamFactory(42UL).CreateStream("test", 1988, 1, "x");

    private static string Expand(string template, Func<string, IReadOnlyList<string>?>? pools = null) =>
        NewsroomGrammar.Expand(template, Tokens, pools ?? (_ => null), Stream());

    [Fact]
    public void TokensSubstituteAndUnknownTokensThrow()
    {
        Assert.Equal("J. Clark drives for Lotus.", Expand("{player} drives for {team}."));
        var ex = Assert.Throws<InvalidOperationException>(() => Expand("{player} met {nobody}."));
        Assert.Contains("nobody", ex.Message);
    }

    [Theory]
    [InlineData("win", "a")]
    [InlineData("eighth place", "an")]
    [InlineData("8th", "an")]
    [InlineData("11th", "an")]
    [InlineData("18th", "an")]
    [InlineData("4th", "a")]
    [InlineData("one-off", "a")]
    [InlineData("European round", "a")]
    [InlineData("unique chance", "a")]
    [InlineData("upset", "an")]
    public void IndefiniteArticlesFollowTheSound(string value, string expected) =>
        Assert.Equal(expected, NewsroomGrammar.IndefiniteArticle(value));

    [Fact]
    public void ArticleAndOrdinalAndPossessiveForms()
    {
        Assert.Equal("An 8th place for J. Clark.", Expand("{a:ord:position} place for {player}."));
        Assert.Equal("J. Clark's day.", Expand("{player's} day."));
        Assert.Equal("Brabhams' garage.",
            NewsroomGrammar.Expand("{team's} garage.",
                new Dictionary<string, string> { ["team"] = "Brabhams" }, _ => null, Stream()));
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
    [InlineData(23, "23rd")]
    [InlineData(111, "111th")]
    public void OrdinalsAreCorrect(int n, string expected) =>
        Assert.Equal(expected, NewsroomGrammar.Ordinal(n));

    [Fact]
    public void OptionalSegmentsRenderOnlyWhenTheFactExists()
    {
        Assert.Equal("J. Clark wins at Monza.", Expand("{player} wins[[?venue: at {venue}]]."));
        Assert.Equal("J. Clark wins.", Expand("{player} wins[[?empty: at {venue}]]."));
        // An unknown token INSIDE a dropped segment is never demanded.
        Assert.Equal("J. Clark wins.", Expand("{player} wins[[?empty: beside {unknownToken}]]."));
    }

    [Fact]
    public void PoolsDrawFromTheStreamAndUndeclaredPoolsThrow()
    {
        var pools = new Dictionary<string, IReadOnlyList<string>>
        {
            ["close"] = ["The end.", "Full stop."],
        };
        var text = Expand("{pool:close}", name => pools.GetValueOrDefault(name));
        Assert.Contains(text, (string[])["The end.", "Full stop."]);

        Assert.Throws<InvalidOperationException>(() => Expand("{pool:missing}"));
    }

    [Fact]
    public void SelfReferencingPoolsHitTheDepthGuard()
    {
        var pools = new Dictionary<string, IReadOnlyList<string>> { ["loop"] = ["again {pool:loop}"] };
        Assert.Throws<InvalidOperationException>(
            () => Expand("{pool:loop}", name => pools.GetValueOrDefault(name)));
    }

    [Fact]
    public void TidyRepairsTheSeamsDroppedSegmentsLeave()
    {
        Assert.Equal("J. Clark wins the race.", NewsroomGrammar.Tidy("J. Clark wins  the race ."));
        Assert.Equal("A day. Then another.", NewsroomGrammar.Tidy("a day.. then another."));
        Assert.Equal("Points, then more.", NewsroomGrammar.Tidy("points ,  then more."));
        Assert.Equal("The flag falls.", NewsroomGrammar.Tidy("the flag falls,."));
        Assert.Equal("", NewsroomGrammar.Tidy("   "));
    }

    [Fact]
    public void ExpansionIsDeterministicPerStreamKey()
    {
        var pools = new Dictionary<string, IReadOnlyList<string>>
        {
            ["v"] = ["one", "two", "three", "four", "five"],
        };
        string Render() => NewsroomGrammar.Expand("{pool:v} {pool:v} {pool:v}",
            Tokens, name => pools.GetValueOrDefault(name), Stream());
        Assert.Equal(Render(), Render());
    }
}
