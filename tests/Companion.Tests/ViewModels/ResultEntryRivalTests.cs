using Companion.Core.Grid;
using Companion.ViewModels.ResultEntry;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The named-rival readout on the result-entry grammar (Mike: "a lot more places where you can see where
/// your rival is"). When the shell names an SMGP rival for the round, the qualifying / finishing-order
/// entry surfaces the rival's live position as cars are placed; a round with no rival named shows nothing.
/// </summary>
public sealed class ResultEntryRivalTests
{
    private static IReadOnlyList<GridSeat> Grid() =>
    [
        ResultEntryViewModelTests.Seat("driver.you", "Nova Reyes", "1", isPlayer: true),
        ResultEntryViewModelTests.Seat("driver.rival", "Gil Ceara", "17"),
        ResultEntryViewModelTests.Seat("driver.other", "Sam Blake", "3"),
    ];

    [Fact]
    public void RivalStatusLine_TracksTheRivalPosition_AndWhoIsAhead()
    {
        var vm = new ResultEntryViewModel(Grid(), "driver.you")
        {
            RivalDriverId = "driver.rival",
            RivalName = "Gil Ceara",
        };
        Assert.Contains("GIL CEARA", vm.RivalStatusLine);
        Assert.Contains("not placed yet", vm.RivalStatusLine);

        var k = new ResultEntryViewModelTests.Keys(vm);
        k.Line("Reyes"); // player P1
        k.Line("Ceara"); // rival P2

        Assert.Contains("P2", vm.RivalStatusLine);
        Assert.Contains("AHEAD", vm.RivalStatusLine);   // player P1, rival P2
        Assert.DoesNotContain("BEHIND", vm.RivalStatusLine);
    }

    [Fact]
    public void RivalStatusLine_ShowsBehind_WhenTheRivalFinishesAhead()
    {
        var vm = new ResultEntryViewModel(Grid(), "driver.you")
        {
            RivalDriverId = "driver.rival",
            RivalName = "Gil Ceara",
        };
        var k = new ResultEntryViewModelTests.Keys(vm);
        k.Line("Ceara"); // rival P1
        k.Line("Reyes"); // player P2

        Assert.Contains("P1", vm.RivalStatusLine);
        Assert.Contains("BEHIND", vm.RivalStatusLine);
    }

    [Fact]
    public void RivalStatusLine_IsEmpty_WhenNoRivalNamed()
    {
        var vm = new ResultEntryViewModel(Grid(), "driver.you"); // no rival set
        Assert.Equal("", vm.RivalStatusLine);

        var k = new ResultEntryViewModelTests.Keys(vm);
        k.Line("Reyes");
        Assert.Equal("", vm.RivalStatusLine); // stays empty for every non-SMGP round
    }

    [Fact]
    public void RivalStatusLine_UsesQualifiesVerb_OnTheQualifyingStep()
    {
        var vm = new ResultEntryViewModel(Grid(), "driver.you")
        {
            SessionLabel = "Qualifying",
            RivalDriverId = "driver.rival",
            RivalName = "Gil Ceara",
        };
        var k = new ResultEntryViewModelTests.Keys(vm);
        k.Line("Ceara");
        Assert.Contains("qualifies", vm.RivalStatusLine);
    }

    [Fact]
    public void IsRival_flags_only_the_named_rival_row()
    {
        var vm = new ResultEntryViewModel(Grid(), "driver.you")
        {
            RivalDriverId = "driver.rival", RivalName = "Gil Ceara",
        };
        Assert.True(vm.IsRival("driver.rival"));   // the named rival
        Assert.False(vm.IsRival("driver.other"));  // another driver
        Assert.False(vm.IsRival("driver.you"));    // the player
    }

    [Fact]
    public void IsRival_is_false_when_no_rival_is_named()
    {
        var vm = new ResultEntryViewModel(Grid(), "driver.you"); // no rival
        Assert.False(vm.IsRival("driver.rival"));
        Assert.False(vm.IsRival(null));
    }

    [Fact]
    public void RivalStatusLine_uses_the_rivals_pronoun_when_a_female_rival_is_out()
    {
        var vm = new ResultEntryViewModel(Grid(), "driver.you")
        {
            RivalDriverId = "driver.rival", RivalName = "Mika Larssen",
            RivalPronouns = Companion.Core.Smgp.SmgpPronouns.She,
        };
        var k = new ResultEntryViewModelTests.Keys(vm);
        k.F8();          // DNF phase
        k.Line("Ceara"); // the rival's seat retires
        Assert.Contains("beat her home", vm.RivalStatusLine);
        Assert.DoesNotContain("him", vm.RivalStatusLine);
    }
}
