using Companion.Core.Smgp;

namespace Companion.Tests.Career;

/// <summary>The SMGP sponsor board loader: parses the authored sponsors, indexes by id, and resolves the
/// sponsors backing a given team (the Paddock Sponsors tab + the future Tycoon mode). Display-only.</summary>
public sealed class SmgpSponsorsTests
{
    private const string Json = """
    {
      "$comment": "test",
      "sponsors": [
        {
          "id": "sponsor.helion-fuels", "name": "Helion Fuels", "industry": "Fuel & Oil", "tier": "title",
          "tagline": "POWER THAT NEVER SLEEPS", "story": ["Para one.", "Para two."],
          "brandColorHex": "#E2571E", "teams": ["team.madonna", "team.bullets"], "foundedFlavor": "Since the first green flag."
        },
        {
          "id": "sponsor.zerobucks", "name": "ZeroBucks Diner", "industry": "Local Business", "tier": "struggling",
          "tagline": "STILL OPEN. BARELY.", "story": ["A struggling backer."],
          "brandColorHex": "#5A5A5A", "teams": ["team.zeroforce"], "foundedFlavor": "A hometown diner clinging on."
        }
      ]
    }
    """;

    [Fact]
    public void Parse_ReadsEverySponsor_InOrder()
    {
        var board = SmgpSponsors.Parse(Json);
        Assert.Equal(2, board.All.Count);
        Assert.Equal("Helion Fuels", board.All[0].Name);
        Assert.Equal("title", board.All[0].Tier);
        Assert.Equal(["team.madonna", "team.bullets"], board.All[0].Teams);
        Assert.Equal("#E2571E", board.All[0].BrandColorHex);
    }

    [Fact]
    public void ForTeam_ReturnsTheSponsorsBackingIt()
    {
        var board = SmgpSponsors.Parse(Json);
        Assert.Equal("Helion Fuels", Assert.Single(board.ForTeam("team.madonna")).Name);
        Assert.Equal("ZeroBucks Diner", Assert.Single(board.ForTeam("team.zeroforce")).Name);
        Assert.Empty(board.ForTeam("team.firenze")); // nobody backs it in this fixture
    }

    [Fact]
    public void ById_And_Empty_Behave()
    {
        var board = SmgpSponsors.Parse(Json);
        Assert.Equal("Helion Fuels", board.ById("sponsor.helion-fuels")!.Name);
        Assert.Null(board.ById("sponsor.nope"));

        Assert.Empty(SmgpSponsors.Empty.All);
        Assert.Empty(SmgpSponsors.Empty.ForTeam("team.madonna"));
    }
}
