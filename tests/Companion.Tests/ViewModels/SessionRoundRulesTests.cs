using Companion.Core.Numerics;
using Companion.Core.Scoring;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Pins the catalog roundOverride → pack round matching convention: catalog overrides are
/// keyed by f1db grand-prix id, pack rounds by display name. Every id used in the shipped
/// rules catalog (indianapolis, spain, austria, monaco, australia, malaysia, abu-dhabi,
/// belgium) must resolve, and near-miss country pairs must NOT cross-match.
/// </summary>
public class SessionRoundRulesTests
{
    [Theory]
    [InlineData("indianapolis", "Indianapolis 500", true)]
    [InlineData("monaco", "Monaco Grand Prix", true)]
    [InlineData("spain", "Spanish Grand Prix", true)]
    [InlineData("austria", "Austrian Grand Prix", true)]
    [InlineData("australia", "Australian Grand Prix", true)]
    [InlineData("malaysia", "Malaysian Grand Prix", true)]
    [InlineData("abu-dhabi", "Abu Dhabi Grand Prix", true)]
    [InlineData("belgium", "Belgian Grand Prix", true)]
    // The dangerous near-miss pair must stay apart in both directions.
    [InlineData("austria", "Australian Grand Prix", false)]
    [InlineData("australia", "Austrian Grand Prix", false)]
    [InlineData("monaco", "Monza Grand Prix", false)]
    public void Matches_CatalogIdsAgainstRoundNames(string grandPrixId, string roundName, bool expected) =>
        Assert.Equal(expected, RoundRuleResolver.Matches(grandPrixId, roundName));

    [Fact]
    public void Resolve_AppliesTheMatchingOverrideOnly()
    {
        var season = new CatalogSeason
        {
            RacePoints = [new(9), new(6), new(4), new(3), new(2), new(1)],
            RoundOverrides =
            [
                new RoundOverride { GrandPrix = "spain", PointsFactor = Rational.Half },
                new RoundOverride { GrandPrix = "indianapolis", CountsForConstructors = false },
            ],
        };

        var spain = RoundRuleResolver.Resolve(season, TestPackBuilder.Round(1, "1967-01-02") with
        {
            Name = "Spanish Grand Prix",
        });
        Assert.Equal(Rational.Half, spain.PointsFactor);
        Assert.True(spain.CountsForConstructors);

        var indy = RoundRuleResolver.Resolve(season, TestPackBuilder.Round(2, "1967-05-07") with
        {
            Name = "Indianapolis 500",
        });
        Assert.Equal(Rational.One, indy.PointsFactor);
        Assert.False(indy.CountsForConstructors);

        var untouched = RoundRuleResolver.Resolve(season, TestPackBuilder.Round(3, "1967-06-04") with
        {
            Name = "Monaco Grand Prix",
        });
        Assert.Equal(Rational.One, untouched.PointsFactor);
        Assert.True(untouched.CountsForConstructors);
        Assert.Null(untouched.AlternateRaceTableId);
    }
}
