using Companion.Core.Dynasty;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>EconomyViewModel wiring against the recording fake: the tab-presence gate, each
/// command's decision payload, refresh-on-success, and the refused-decision error surface.</summary>
public sealed class EconomyViewModelTests
{
    private static DynastyEconomyDashboard Dashboard(string balance = "100,000") => new()
    {
        Balance = balance,
        InDeficit = false,
        DeficitRounds = 0,
        GraceRounds = 4,
        HardFloor = "-25,000",
        Bankrupt = false,
        DevelopmentLevel = 0,
        DevelopmentMaxLevel = 8,
        NextDevelopmentCost = "8,000",
        DevelopmentAtCap = false,
        StaffTier = 0,
        StaffOptions = [],
        SecondSeat = SecondSeatDeal.Retained,
        SecondSeatSalaryPerSeason = "32,000",
        PayDriverBackingPerSeason = "12,000",
        ActiveSponsors = [],
        SponsorBoard = [],
        Statement = [],
        PendingDecisions = [],
        NextRound = 1,
    };

    [Fact]
    public void NonEconomySession_GatesTheTabOff()
    {
        var vm = new EconomyViewModel(new FakeCareerSession());
        Assert.False(vm.HasEconomy);
        Assert.Null(vm.Dashboard);
    }

    [Fact]
    public void Commands_DeclareTheExactDecisionAndRefresh()
    {
        var session = new FakeCareerSession { Economy = Dashboard() };
        var vm = new EconomyViewModel(session);
        Assert.True(vm.HasEconomy);

        vm.SignSponsorCommand.Execute("sponsor.apex-lubricants");
        vm.BuyDevelopmentCommand.Execute(null);
        vm.SetStaffCommand.Execute(2);
        vm.SetSecondSeatCommand.Execute(SecondSeatDeal.PayDriver);
        vm.DropSponsorCommand.Execute("sponsor.apex-lubricants");

        Assert.Equal(5, session.EconomyDecisions.Count);
        Assert.Equal(DynastyEconomyDecisionKind.SignSponsor, session.EconomyDecisions[0].Kind);
        Assert.Equal("sponsor.apex-lubricants", session.EconomyDecisions[0].SponsorId);
        Assert.Equal(DynastyEconomyDecisionKind.BuyDevelopment, session.EconomyDecisions[1].Kind);
        Assert.Equal(DynastyEconomyDecisionKind.SetStaff, session.EconomyDecisions[2].Kind);
        Assert.Equal(2, session.EconomyDecisions[2].StaffTier);
        Assert.Equal(DynastyEconomyDecisionKind.SetSecondSeat, session.EconomyDecisions[3].Kind);
        Assert.Equal(SecondSeatDeal.PayDriver, session.EconomyDecisions[3].SecondSeat);
        Assert.Equal(DynastyEconomyDecisionKind.DropSponsor, session.EconomyDecisions[4].Kind);
        Assert.Equal("", vm.EconomyActionError);
    }

    [Fact]
    public void RefusedDecision_SurfacesTheReasonAndKeepsTheDashboard()
    {
        var session = new FakeCareerSession
        {
            Economy = Dashboard(),
            DeclareEconomyDecisionThrows = new InvalidOperationException("The team cannot afford this increment."),
        };
        var vm = new EconomyViewModel(session);

        vm.BuyDevelopmentCommand.Execute(null);

        Assert.Empty(session.EconomyDecisions);
        Assert.Equal("The team cannot afford this increment.", vm.EconomyActionError);
        Assert.NotNull(vm.Dashboard);

        // The next successful decision clears the error.
        session.DeclareEconomyDecisionThrows = null;
        vm.BuyDevelopmentCommand.Execute(null);
        Assert.Equal("", vm.EconomyActionError);
        Assert.Single(session.EconomyDecisions);
    }

    [Fact]
    public void Refresh_ReprojectsFromTheSession()
    {
        var session = new FakeCareerSession { Economy = Dashboard() };
        var vm = new EconomyViewModel(session);

        session.Economy = Dashboard(balance: "92,500");
        vm.Refresh();

        Assert.Equal("92,500", vm.Dashboard!.Balance);
    }
}
