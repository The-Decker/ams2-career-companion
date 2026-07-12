using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>The Paddock lens VM (SMGP driver/team preview): projects the session's paddock model into
/// selectable driver + team lists, gates the tab on having content, and cross-links a driver to their
/// team. Null model (a non-SMGP career) → no paddock, no tab.</summary>
public sealed class PaddockViewModelTests
{
    private static SmgpDriverCard Driver(string id, string name, string teamId, string teamName, int prestige) => new()
    {
        DriverId = id, Name = name, TeamId = teamId, TeamName = teamName, Number = "1",
        PortraitKey = id, CarKey = id, Epithet = "THE " + name.ToUpperInvariant(),
        Bio = ["p1", "p2", "p3"], Quotes = ["q"], Stats = null, Prestige = prestige,
    };

    private static SmgpTeamCard Team(string id, string name) => new()
    {
        TeamId = id, Name = name, Motto = "MOTTO", LogoKey = id,
        History = ["h1"], Quotes = ["q"], DriverNames = [name], Prestige = 5,
    };

    private static FakeCareerSession SessionWithPaddock() => new()
    {
        Paddock = new SmgpPaddockModel
        {
            Drivers =
            [
                Driver("driver.senna", "Senna", "team.madonna", "Madonna", 5),
                Driver("driver.ceara", "Ceara", "team.bullets", "Bullets", 3),
            ],
            Teams = [Team("team.madonna", "Madonna"), Team("team.bullets", "Bullets")],
        },
    };

    [Fact]
    public void Projects_the_paddock_and_selects_the_first_of_each()
    {
        var vm = new PaddockViewModel(SessionWithPaddock());

        Assert.True(vm.HasPaddock);
        Assert.Equal(2, vm.Drivers.Count);
        Assert.Equal(2, vm.Teams.Count);
        Assert.Equal("driver.senna", vm.SelectedDriver?.DriverId);
        Assert.Equal("team.madonna", vm.SelectedTeam?.TeamId);
        Assert.False(vm.ShowTeams); // opens on the DRIVERS view
    }

    [Fact]
    public void A_null_model_yields_no_paddock()
    {
        var vm = new PaddockViewModel(new FakeCareerSession()); // Paddock left null (non-SMGP)
        Assert.False(vm.HasPaddock);
        Assert.Empty(vm.Drivers);
        Assert.Empty(vm.Teams);
    }

    [Fact]
    public void ViewTeam_jumps_to_the_teams_view_and_selects_that_team()
    {
        var vm = new PaddockViewModel(SessionWithPaddock());

        vm.ViewTeamCommand.Execute("team.bullets");

        Assert.True(vm.ShowTeams);
        Assert.Equal("team.bullets", vm.SelectedTeam?.TeamId);
    }

    [Fact]
    public void Refresh_preserves_the_current_selection()
    {
        var session = SessionWithPaddock();
        var vm = new PaddockViewModel(session);
        vm.SelectedDriver = vm.Drivers.First(d => d.DriverId == "driver.ceara");

        vm.Refresh();

        Assert.Equal("driver.ceara", vm.SelectedDriver?.DriverId); // not yanked back to the top
    }
}
