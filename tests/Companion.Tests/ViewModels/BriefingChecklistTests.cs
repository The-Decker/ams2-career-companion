using Companion.ViewModels.Briefing;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The briefing checklist redesign (ux-round contract, corrected briefing section): rows in
/// in-game custom-race screen order (track → class → opponents → laps → date → start time →
/// weather → time progression → pit rules), tick toggling with "N of M set" progress, ticks
/// keyed to the round number (session-scoped: reset on a new round, restored when the same
/// round is refreshed), the single Copy-summary composition, and the compact always-on-top
/// mode flag the App-layer floating window binds to.
/// </summary>
public class BriefingChecklistTests
{
    private static BriefingViewModel RealRound3Vm(out FakeCareerSession session)
    {
        var pack = ViewModelTestData.RealPack();
        var round = pack.Season.Rounds.Single(r => r.Round == 3);
        session = new FakeCareerSession
        {
            Briefing = BriefingComposer.Compose(pack, round, ViewModelTestData.RealLibrary.Value),
        };
        return new BriefingViewModel(session);
    }

    private static (FakeCareerSession Session, BriefingModel Round1, BriefingModel Round2) TwoRoundSession()
    {
        var pack = TestPackBuilder.TwoRoundPack();
        var library = TestPackBuilder.Library();
        var b1 = BriefingComposer.Compose(pack, pack.Season.Rounds[0], library);
        var b2 = BriefingComposer.Compose(pack, pack.Season.Rounds[1], library);
        return (new FakeCareerSession { Briefing = b1 }, b1, b2);
    }

    // ---------- in-game screen order ----------

    [Fact]
    public void Checklist_IsInInGameScreenOrder_OpponentsRightAfterClass()
    {
        var vm = RealRound3Vm(out _);

        Assert.Equal(
        [
            "Track",
            "Class",
            "Opponents",
            "Laps",
            "Date",
            "Start time",
            "Weather slot 1",
            "Time progression",
            "Mandatory pit stop",
        ], vm.Settings.Select(s => s.Label));

        // The values ride along unchanged from the composer.
        Assert.Equal("Spielberg_Vintage", vm.Settings[0].Value);
        Assert.Equal("13", vm.Settings[2].Value); // Opponents = grid.size 14 - 1 (player seat)
        Assert.Equal("64", vm.Settings[3].Value); // Laps
    }

    // ---------- tick / progress ----------

    [Fact]
    public void Progress_CountsTicks_AllSetWhenEveryRowIsChecked()
    {
        var vm = RealRound3Vm(out _);
        Assert.Equal("0 of 9 set", vm.ChecklistProgressText);
        Assert.False(vm.AllSet);

        vm.Settings[0].IsChecked = true;
        vm.Settings[3].IsChecked = true;
        Assert.Equal("2 of 9 set", vm.ChecklistProgressText);

        foreach (var item in vm.Settings)
            item.IsChecked = true;
        Assert.Equal("9 of 9 set", vm.ChecklistProgressText);
        Assert.True(vm.AllSet);

        vm.Settings[4].IsChecked = false; // untick works too
        Assert.Equal("8 of 9 set", vm.ChecklistProgressText);
        Assert.False(vm.AllSet);
    }

    [Fact]
    public void ToggleItemCommand_FlipsARow()
    {
        var vm = RealRound3Vm(out _);

        vm.ToggleItemCommand.Execute(vm.Settings[1]);
        Assert.True(vm.Settings[1].IsChecked);

        vm.ToggleItemCommand.Execute(vm.Settings[1]);
        Assert.False(vm.Settings[1].IsChecked);

        vm.ToggleItemCommand.Execute(null); // no row: no throw
    }

    // ---------- per-round tick reset (ticks keyed to round number) ----------

    [Fact]
    public void Ticks_ResetOnANewRound_AndAreRestoredForTheSameRound()
    {
        var (session, round1, round2) = TwoRoundSession();
        var vm = new BriefingViewModel(session);
        Assert.Equal("0 of 6 set", vm.ChecklistProgressText);

        vm.Settings[0].IsChecked = true; // Track
        vm.Settings[2].IsChecked = true; // Opponents
        Assert.Equal("2 of 6 set", vm.ChecklistProgressText);

        // Round advances: a fresh checklist, nothing pre-ticked.
        session.Briefing = round2;
        vm.Refresh();
        Assert.Equal("0 of 6 set", vm.ChecklistProgressText);
        Assert.All(vm.Settings, i => Assert.False(i.IsChecked));

        // Navigating back to round 1 within the session restores its ticks.
        session.Briefing = round1;
        vm.Refresh();
        Assert.Equal("2 of 6 set", vm.ChecklistProgressText);
        Assert.True(vm.Settings.Single(i => i.Label == "Track").IsChecked);
        Assert.True(vm.Settings.Single(i => i.Label == "Opponents").IsChecked);
        Assert.False(vm.Settings.Single(i => i.Label == "Laps").IsChecked);
    }

    [Fact]
    public void Refresh_SameRound_KeepsTheTicks()
    {
        var (session, _, _) = TwoRoundSession();
        var vm = new BriefingViewModel(session);

        vm.Settings[0].IsChecked = true;
        vm.Refresh(); // e.g. navigating Briefing -> Standings -> Briefing

        Assert.Equal("1 of 6 set", vm.ChecklistProgressText);
        Assert.True(vm.Settings[0].IsChecked);
    }

    // ---------- copy summary ----------

    [Fact]
    public void CopySummary_ComposesTitleAndEveryRowInChecklistOrder_PlusNotes()
    {
        var vm = RealRound3Vm(out _);
        var copied = new List<string>();
        vm.CopyRequested += (_, text) => copied.Add(text);

        vm.CopySummaryCommand.Execute(null);

        string text = Assert.Single(copied);
        Assert.StartsWith("Dutch Grand Prix — placeholder: Spielberg_Vintage", text);
        Assert.Contains("Track: Spielberg_Vintage", text);
        Assert.Contains("Class: F-Vintage_Gen1", text);
        Assert.Contains("Opponents: 13", text);
        Assert.Contains("Laps: 64", text);
        Assert.Contains("Mandatory pit stop: No", text);
        Assert.Contains("377.4 km", text); // setup notes ride along
        // Checklist order is preserved: Opponents before Laps.
        Assert.True(
            text.IndexOf("Opponents:", StringComparison.Ordinal) <
            text.IndexOf("Laps:", StringComparison.Ordinal));
    }

    // ---------- compact always-on-top mode ----------

    [Fact]
    public void CompactChecklist_TogglesOpen_AndClosesWhenTheSeasonCompletes()
    {
        var vm = RealRound3Vm(out var session);
        Assert.False(vm.CompactChecklistOpen);

        vm.ToggleCompactChecklistCommand.Execute(null);
        Assert.True(vm.CompactChecklistOpen);

        vm.ToggleCompactChecklistCommand.Execute(null);
        Assert.False(vm.CompactChecklistOpen);

        // Open it, then finish the season: nothing left to tick, the overlay closes.
        vm.ToggleCompactChecklistCommand.Execute(null);
        session.Briefing = null;
        vm.Refresh();
        Assert.True(vm.SeasonComplete);
        Assert.False(vm.CompactChecklistOpen);
    }
}
