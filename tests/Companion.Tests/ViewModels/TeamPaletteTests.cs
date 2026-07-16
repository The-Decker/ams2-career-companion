using Companion.ViewModels.Shell;

namespace Companion.Tests.ViewModels;

public sealed class TeamPaletteTests
{
    public static TheoryData<string, string, string> CanonicalSmgpColors => new()
    {
        { "team.iris", "#F4F4F2", "#7137B8" },
        { "team.azalea", "#F4F4F2", "#FF2D95" },
        { "team.bullets", "#19CFF3", "#1749C7" },
        { "team.bestowal", "#FFD500", "#73F20D" },
        { "team.millions", "#2054C7", "#FFD500" },
        { "team.firenze", "#E10600", "#E10600" },
        { "team.madonna", "#E10600", "#FFD500" },
        { "team.lares", "#111317", "#111317" },
        { "team.minarae", "#FFD500", "#F4F4F2" },
        { "team.linden", "#244BC4", "#244BC4" },
        { "team.dardan", "#F0641E", "#F0641E" },
        { "team.rigel", "#58EF24", "#58EF24" },
        { "team.serga", "#17348F", "#19CFF3" },
        { "team.joke", "#176B3A", "#176B3A" },
        { "team.losel", "#FFD500", "#FFD500" },
        { "team.may", "#19CFF3", "#19CFF3" },
        { "team.tyrant", "#1749C7", "#F4F4F2" },
        { "team.blanche", "#F4F4F2", "#1749C7" },
    };

    [Theory]
    [MemberData(nameof(CanonicalSmgpColors))]
    public void CanonicalSmgpTeamColors_AreProjectedAsAuthored(
        string teamId, string primary, string secondary)
    {
        Assert.Equal(primary, TeamPalette.For(teamId));
        Assert.Equal(secondary, TeamPalette.SecondaryFor(teamId));
        Assert.Equal(new TeamPalette.TeamColors(primary, secondary), TeamPalette.ColorsFor(teamId));
    }

    [Fact]
    public void DerivedAndEmptyTeams_KeepABranchFreeTwoColorContract()
    {
        var derived = TeamPalette.ColorsFor("team.future");
        Assert.Equal(derived.Primary, derived.Secondary);

        var empty = TeamPalette.ColorsFor(null);
        Assert.Equal("#8A929E", empty.Primary);
        Assert.Equal(empty.Primary, empty.Secondary);
    }
}
