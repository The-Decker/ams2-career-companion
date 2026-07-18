using Companion.ViewModels.Wizard;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The per-team PLAYER image key (Mike: a different team-coloured player image per team, named
/// <c>player.&lt;team&gt;</c>, e.g. player.minarae). The player's own ("YOU") Season's-Grid card and
/// the character screen show it in place of the AI driver's face; every other card keeps the seat
/// driver's portrait.
/// </summary>
public sealed class PlayerImageKeyTests
{
    [Theory]
    [InlineData("team.minarae", "player.minarae")]
    [InlineData("team.madonna", "player.madonna")]
    [InlineData("bullets", "player.bullets")]     // already prefix-less
    [InlineData("", "player")]                    // own entrant, no team
    public void PlayerImageKey_IsPlayerDotTeam(string teamId, string expected)
    {
        Assert.Equal(expected, GridSeatChoice.PlayerImageKey(teamId));
    }

    [Fact]
    public void ThePlayersOwnCard_UsesTheTeamPlayerImage_OthersUseTheDriverPortrait()
    {
        static GridSeatChoice Seat(bool locked) => new()
        {
            LiveryName = "Minarae #20 B. Miller",
            Liveries = ["Minarae #20 B. Miller"],
            DriverName = "Bernie Miller",
            TeamName = "Minarae",
            DriverId = "driver.bernie_miller",
            TeamId = "team.minarae",
            IsLocked = locked,
        };

        Assert.Equal("player.minarae", Seat(locked: true).PortraitKey);        // the player's own card
        Assert.Equal("driver.bernie_miller", Seat(locked: false).PortraitKey);  // any other card
    }
}
