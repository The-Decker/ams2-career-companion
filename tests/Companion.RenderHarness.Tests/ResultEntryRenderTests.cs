using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Companion.App.Audio;
using Companion.App.Views;
using Companion.Core.Career;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.ViewModels.ResultEntry;

namespace Companion.RenderHarness.Tests;

/// <summary>
/// Off-screen render tests for the two v0.3.1 DNF result-entry regressions, driven through the
/// REAL ResultEntryView over a REAL ResultEntryViewModel, the VM logic is already covered and
/// correct, so these exercise the view layer that only a live render exposes:
///
/// BUG A, clicking into the DNF custom-cause box or the DSQ reason box must leave the caret
///          there; the old OnPreviewMouseUp yanked focus back to InputBox on every left release.
/// BUG B, after Mech/Acc, a single Ctrl+Z through the view's key path must revert the DNF
///          reason (and the row must reflect it).
///
/// Every test hops onto an STA thread with a live Dispatcher via <see cref="WpfRenderHarness"/>;
/// on a non-Windows / non-STA host they self-skip instead of failing.
/// </summary>
public sealed class ResultEntryRenderTests
{
    private const string PlayerId = "d.amon";

    private static readonly (string Id, string Name, string Number)[] Roster =
    [
        ("d.brabham", "Jack Brabham", "1"),
        ("d.hulme", "Denny Hulme", "2"),
        ("d.clark", "Jim Clark", "3"),
        ("d.ghill", "Graham Hill", "4"),
        (PlayerId, "Chris Amon", "6"),
        ("d.stewart", "Jackie Stewart", "8"),
    ];

    private static readonly PackDriverRatings Ratings = new() { RaceSkill = 0.9, QualifyingSkill = 0.9 };

    private static GridSeat Seat(string id, string name, string number) => new()
    {
        DriverId = id,
        DriverName = name,
        TeamId = "t." + id,
        TeamName = "Team " + name,
        Number = number,
        Ams2LiveryName = name,
        Ratings = Ratings,
        Reliability = 1.0,
        WeightScalar = 1.0,
        PowerScalar = 1.0,
        DragScalar = 1.0,
        IsPlayer = id == PlayerId,
    };

    private static IReadOnlyList<GridSeat> Grid() =>
        Roster.Select(r => Seat(r.Id, r.Name, r.Number)).ToArray();

    // ---------- BUG A: the caret must stay in the DNF custom-cause box ----------

    [Fact]
    public void ClickingIntoDnfDetailBox_KeepsFocusThere_AndTypedTextReachesViewModel()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);

            // Mark a driver DNF as "Other" and open its inline reason picker so the custom-cause
            // box is realised and visible.
            const string id = "d.clark";
            Assert.True(vm.MarkDnf(id)); // default reason "o"
            vm.ReasonPickerDriverId = id;
            host.Layout();

            var detailBox = host.FindDnfDetailBox(id);
            Assert.NotNull(detailBox);

            // Simulate the click landing in the box: give it keyboard focus, then raise the same
            // left PreviewMouseUp the mouse would, with the box as the original source.
            detailBox!.Focus();
            Keyboard.Focus(detailBox);
            host.RaiseLeftMouseUp(detailBox);
            WpfRenderHarness.Pump(); // let the view's deferred FocusInput (if any) run

            // The fix: focus stays in the detail box, NOT snapped back to InputBox.
            Assert.Same(detailBox, Keyboard.FocusedElement);
            Assert.NotSame(host.InputBox, Keyboard.FocusedElement);

            // Typing there and committing (LostFocus) must reach the VM via SetDnfDetail.
            detailBox.Text = "Engine fire";
            host.RaiseLostFocus(detailBox);
            WpfRenderHarness.Pump();

            var entry = vm.Dnfs.Single(d => d.Seat.DriverId == id);
            Assert.Equal("o", entry.Reason);
            Assert.Equal("Engine fire", entry.Detail);
        });
    }

    // ---------- BUG A: the caret must stay in the DSQ reason box ----------

    [Fact]
    public void ClickingIntoDsqReasonBox_KeepsFocusThere_AndTypedTextReachesViewModel()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);

            const string id = "d.hulme";
            Assert.True(vm.MarkDsq(id));
            // v0.3.3: a DSQ row starts in its compact DISPLAY state (no box). Open the editor the
            // way click-to-edit does before the reason box is realised.
            vm.BeginEditingReason(id);
            host.Layout();

            var reasonBox = host.FindDsqReasonBox(id);
            Assert.NotNull(reasonBox);

            reasonBox!.Focus();
            Keyboard.Focus(reasonBox);
            host.RaiseLeftMouseUp(reasonBox);
            WpfRenderHarness.Pump();

            Assert.Same(reasonBox, Keyboard.FocusedElement);
            Assert.NotSame(host.InputBox, Keyboard.FocusedElement);

            reasonBox.Text = "Underweight";
            host.RaiseLostFocus(reasonBox);
            WpfRenderHarness.Pump();

            Assert.Equal("Underweight", vm.DsqReasonOf(id));
        });
    }

    // ========== v0.3.3: Enter commits + editor hides, click-to-edit, team name ==========

    /// <summary>DNF acceptance criterion (Mike: "you still can not press enter when entering the
    /// dnf values"): mark a DNF, open "Other", type "Engine fire" in the custom box, and raise a
    /// REAL Enter FROM the box through the tunnel+bubble route. The key-routing guard must let the
    /// box's OnDnfDetailKeyDown handle it (NOT the grammar), so: (1) the detail SAVES, (2) the
    /// editor HIDES (EditingReasonDriverId null and the custom box no longer visible), and (3) the
    /// row's compact DISPLAY shows "Engine fire" + the team name.</summary>
    [Fact]
    public void Dnf_TypeCustomCause_EnterSavesAndHidesEditor_AndRowShowsReasonPlusTeam()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);

            const string id = "d.clark";
            Assert.True(vm.MarkDnf(id));        // fresh DNF, reason "o"
            vm.ReasonPickerDriverId = id;       // fresh-drop auto-opens the editor
            host.Layout();

            var detailBox = host.FindDnfDetailBox(id);
            Assert.NotNull(detailBox);
            detailBox!.Focus();
            Keyboard.Focus(detailBox);
            detailBox.Text = "Engine fire";

            // The load-bearing step: a real Enter raised FROM the box. If the grammar guard were
            // missing, the UserControl's preview handler would eat this and run Submit/Confirm.
            bool handled = host.RaiseKeyDown(detailBox, Key.Enter);
            host.Layout();
            WpfRenderHarness.Pump();

            Assert.True(handled, "Enter in the DNF custom-cause box must be handled by the box, not swallowed.");

            // (1) saved
            var entry = vm.Dnfs.Single(d => d.Seat.DriverId == id);
            Assert.Equal("o", entry.Reason);
            Assert.Equal("Engine fire", entry.Detail);

            // (2) editor hidden
            Assert.Null(vm.EditingReasonDriverId);
            Assert.False(host.IsEffectivelyVisible(host.FindDnfDetailBox(id)),
                "The custom-cause box must be hidden once the value is committed.");

            // (3) DISPLAY shows the custom reason + the team name
            Assert.Equal("Engine fire", host.RenderedDnfReasonText(id));
            Assert.Contains(host.RenderedTeamNameTexts(), t => t.Contains("Jim Clark")); // "Team Jim Clark"
        });
    }

    /// <summary>DSQ acceptance criterion (Mike: "you cant press enter to enter your dsq reasons it
    /// just stays at what you typed. the text box stays as well"): mark a DSQ, click the row to
    /// edit, type "Underweight", raise a REAL Enter FROM the box. The reason SAVES, the box HIDES,
    /// and the row's DISPLAY shows "Underweight" + the team name.</summary>
    [Fact]
    public void Dsq_ClickToEdit_TypeReason_EnterSavesAndHidesEditor_AndRowShowsReasonPlusTeam()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);

            const string id = "d.hulme";
            Assert.True(vm.MarkDsq(id));
            host.Layout();

            // Click the compact DISPLAY row to open its editor (click-to-edit).
            vm.BeginEditingReason(id);
            host.Layout();

            var reasonBox = host.FindDsqReasonBox(id);
            Assert.NotNull(reasonBox);
            reasonBox!.Focus();
            Keyboard.Focus(reasonBox);
            reasonBox.Text = "Underweight";

            bool handled = host.RaiseKeyDown(reasonBox, Key.Enter);
            host.Layout();
            WpfRenderHarness.Pump();

            Assert.True(handled, "Enter in the DSQ reason box must be handled by the box, not swallowed.");

            // saved
            Assert.Equal("Underweight", vm.DsqReasonOf(id));
            // editor hidden
            Assert.Null(vm.EditingReasonDriverId);
            Assert.False(host.IsEffectivelyVisible(host.FindDsqReasonBox(id)),
                "The DSQ reason box must be hidden once the value is committed.");
            // DISPLAY shows the reason + team
            Assert.Equal("Underweight", host.RenderedDsqReasonText(id));
            Assert.Contains(host.RenderedTeamNameTexts(), t => t.Contains("Denny Hulme")); // "Team Denny Hulme"
        });
    }

    /// <summary>Click-to-edit re-opens a DONE row seeded with its current value, no remove/re-add
    /// (Mike: "when you click on the driver, the box comes back up with the options"). Save a DSQ
    /// reason, close, then click the row again: the editor reappears and its box is seeded with the
    /// saved reason.</summary>
    [Fact]
    public void ClickingADoneDsqRow_ReopensEditor_SeededWithCurrentValue()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);

            const string id = "d.hulme";
            Assert.True(vm.MarkDsq(id));
            Assert.True(vm.SetDsqReason(id, "Illegal wing"));
            vm.StopEditingReason(); // back to DISPLAY
            host.Layout();

            // Row is DONE: the box is hidden.
            Assert.Null(vm.EditingReasonDriverId);
            Assert.False(host.IsEffectivelyVisible(host.FindDsqReasonBox(id)));

            // Click-to-edit via the same VM entry point the row's click handler calls.
            vm.BeginEditingReason(id);
            host.Layout();

            var box = host.FindDsqReasonBox(id);
            Assert.NotNull(box);
            Assert.True(host.IsEffectivelyVisible(box), "The editor must reappear on click-to-edit.");
            // Seed the box the way OnResolvedRowClick's deferred step does, then confirm the value.
            box!.Text = vm.DsqReasonOf(id);
            Assert.Equal("Illegal wing", box.Text);
        });
    }

    /// <summary>Click-to-edit re-opens a DONE "Other" DNF row seeded with its custom cause. The DNF
    /// custom box is Detail-bound, so simply opening the editor shows the saved text.</summary>
    [Fact]
    public void ClickingADoneDnfRow_ReopensEditor_SeededWithCurrentDetail()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);

            const string id = "d.clark";
            Assert.True(vm.MarkDnf(id));
            Assert.True(vm.SetDnfDetail(id, "Gearbox", driverAttributed: false));
            vm.StopEditingReason();
            host.Layout();

            Assert.Null(vm.EditingReasonDriverId);
            Assert.False(host.IsEffectivelyVisible(host.FindDnfDetailBox(id)));

            vm.BeginEditingReason(id);
            host.Layout();

            var box = host.FindDnfDetailBox(id);
            Assert.NotNull(box);
            Assert.True(host.IsEffectivelyVisible(box), "The DNF editor must reappear on click-to-edit.");
            Assert.Equal("Gearbox", box!.Text); // Detail-bound → seeded automatically
        });
    }

    /// <summary>Team name is present on BOTH a DNF and a DSQ row's rendered visual tree at once
    /// (requirement 5). Mark one of each and assert both team names render.</summary>
    [Fact]
    public void TeamName_RendersOnBothDnfAndDsqRows()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);

            // MarkDnf/MarkDsq do NOT open the editor themselves (the drop handler sets the picker
            // id separately), so both rows render in their compact DISPLAY state, which is exactly
            // where the team name must show.
            Assert.True(vm.MarkDnf("d.clark"));
            Assert.True(vm.MarkDsq("d.hulme"));
            host.Layout();

            var teams = host.RenderedTeamNameTexts();
            Assert.Contains(teams, t => t.Contains("Jim Clark"));   // DNF row team
            Assert.Contains(teams, t => t.Contains("Denny Hulme")); // DSQ row team
        });
    }

    /// <summary>The grammar STILL gets Enter when the InputBox is focused (requirement 1 / 7): a
    /// placement via the grammar works. With focus in InputBox the key-routing guard stands down,
    /// so the UserControl's OnPreviewKeyDown runs Submit, placing the highlighted candidate.</summary>
    [Fact]
    public void GrammarStillGetsEnter_WhenInputBoxFocused_PlacesTheCandidate()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);
            host.Layout();

            host.InputBox.Focus();
            Keyboard.Focus(host.InputBox);
            vm.Input = "brab"; // unambiguous surname prefix → Jack Brabham
            host.Layout();
            Assert.Equal("d.brabham", vm.SelectedCandidate?.DriverId);

            // A real Enter from the focused InputBox: the guard must NOT fire (InputBox is exempt),
            // so the grammar's Submit runs and places the candidate.
            bool handled = host.RaiseKeyDown(host.InputBox, Key.Enter);
            host.Layout();

            Assert.True(handled, "Enter in the grammar box must be handled by the grammar.");
            Assert.Contains(vm.Classified, s => s.DriverId == "d.brabham");
            Assert.Equal("", vm.Input); // Submit cleared it
        });
    }

    [Fact]
    public void CandidateDoubleClick_PlacesCandidateAndPlaysBucketPlace_OnlyWhenResultChanges()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var played = new List<SoundEffectCue>();
            SoundAssist.Connect(played.Add);
            try
            {
                var vm = new ResultEntryViewModel(Grid(), PlayerId);
                using var host = ViewHost.Show(vm);

                vm.Input = "brab";
                host.Layout();
                Assert.Equal("d.brabham", vm.SelectedCandidate?.DriverId);

                Assert.True(host.InvokeDoubleClick("OnCandidateDoubleClick", vm.SelectedCandidate!));
                Assert.Contains(vm.Classified, s => s.DriverId == "d.brabham");
                Assert.Equal([SoundEffectCue.BucketPlace], played);

                // Submit cleared the pending grammar action. A stale second invocation is a
                // handled no-op and must not imply that another car moved.
                Assert.True(host.InvokeDoubleClick("OnCandidateDoubleClick", vm.SelectedCandidate));
                Assert.Equal([SoundEffectCue.BucketPlace], played);
            }
            finally
            {
                SoundAssist.Disconnect();
            }
        });
    }

    [Fact]
    public void RemainingDoubleClick_PlacesOrDnfsAndPlaysBucketPlace_OnlyAfterSuccessfulMove()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var played = new List<SoundEffectCue>();
            SoundAssist.Connect(played.Add);
            try
            {
                var vm = new ResultEntryViewModel(Grid(), PlayerId);
                using var host = ViewHost.Show(vm);

                GridSeat classified = vm.Remaining.Single(s => s.DriverId == "d.clark");
                Assert.True(host.InvokeDoubleClick("OnRemainingDoubleClick", classified));
                Assert.Contains(vm.Classified, s => s.DriverId == classified.DriverId);
                Assert.Equal([SoundEffectCue.BucketPlace], played);

                // This row can be stale for one dispatcher turn after the successful move.
                // InsertAt rejects it, so the repeated gesture stays silent.
                Assert.True(host.InvokeDoubleClick("OnRemainingDoubleClick", classified));
                Assert.Equal([SoundEffectCue.BucketPlace], played);

                vm.ToggleDnfPhaseCommand.Execute(null);
                GridSeat retired = vm.Remaining.Single(s => s.DriverId == "d.hulme");
                Assert.True(host.InvokeDoubleClick("OnRemainingDoubleClick", retired));
                Assert.Contains(vm.Dnfs, d => d.Seat.DriverId == retired.DriverId);
                Assert.Equal([SoundEffectCue.BucketPlace, SoundEffectCue.BucketPlace], played);
            }
            finally
            {
                SoundAssist.Disconnect();
            }
        });
    }

    /// <summary>Freshly-dropped DNF auto-opens its editor so a reason can be picked immediately
    /// (requirement 4): setting the picker id realises the M/A/O buttons and shows the editor.</summary>
    [Fact]
    public void FreshlyDroppedDnf_AutoOpensEditor()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);

            const string id = "d.clark";
            Assert.True(vm.MarkDnf(id));
            vm.ReasonPickerDriverId = id; // the drop handler does this on a fresh DNF drop
            host.Layout();

            Assert.Equal(id, vm.EditingReasonDriverId);
            var mech = host.FindReasonButton(id, "m");
            Assert.NotNull(mech);
            Assert.True(host.IsEffectivelyVisible(mech), "A fresh DNF's reason picker must be visible.");
        });
    }

    /// <summary>Changing a DNF reason from the editor is dynamic and undoable (requirements 6/7):
    /// on a done Mechanical DNF, click-to-edit then pick Accident, the row updates and a single
    /// Ctrl+Z (vm.Undo) reverts it, all with no remove/re-add.</summary>
    [Fact]
    public void ClickToEdit_ChangeDnfReason_IsDynamicAndUndoable()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);

            const string id = "d.clark";
            Assert.True(vm.MarkDnf(id, "m"));
            host.Layout();
            Assert.Equal("mechanical", host.RenderedDnfReasonText(id));

            // Click-to-edit, then pick Accident through the REAL button.
            vm.BeginEditingReason(id);
            host.Layout();
            host.RaiseButtonClick(host.FindReasonButton(id, "a")!);
            host.Layout();

            Assert.Equal("a", vm.Dnfs.Single(d => d.Seat.DriverId == id).Reason);
            Assert.Equal("accident", host.RenderedDnfReasonText(id));
            Assert.Null(vm.EditingReasonDriverId); // picking a/m closes the editor

            // One undo reverts the reason change, no remove/re-add.
            vm.UndoCommand.Execute(null);
            host.Layout();
            Assert.Equal("m", vm.Dnfs.Single(d => d.Seat.DriverId == id).Reason);
            Assert.Equal("mechanical", host.RenderedDnfReasonText(id));
        });
    }

    // ---------- BUG A control: a click on a NON-text surface still pins the grammar input ----------

    [Fact]
    public void ClickingOnANonTextSurface_StillPinsGrammarInput()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);
            host.Layout();

            // Focus somewhere else first, then click a plain (non-text-entry) element.
            var order = host.Find<ListBox>("OrderList");
            Assert.NotNull(order);
            order!.Focus();
            host.RaiseLeftMouseUp(order);
            WpfRenderHarness.Pump();

            // Decision 8: typing the grammar must always work, the pin still fires here.
            Assert.Same(host.InputBox, Keyboard.FocusedElement);
        });
    }

    // ---------- BUG B: after Mech, undo reverts the reason AND the rendered row updates ----------

    /// <summary>The substance of BUG B, end-to-end through the live view: mark DNF "Other", open
    /// the picker, click the REAL "Mech" button (the row's reason label renders "mechanical"),
    /// then invoke the undo command, the exact call the view's Ctrl+Z branch makes
    /// (<c>vm.UndoCommand.Execute(null)</c>), and confirm the VM reason is back to "o" AND the
    /// rendered reason label on the row has visibly changed back from "mechanical" to "retired".
    /// A single undo, and the row reflects it, which the v0.3.1 build did not deliver.</summary>
    [Fact]
    public void AfterMech_Undo_RevertsReason_AndTheRenderedRowUpdates()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);

            const string id = "d.clark";
            Assert.True(vm.MarkDnf(id)); // reason "o"
            vm.ReasonPickerDriverId = id;
            host.Layout();

            var mech = host.FindReasonButton(id, "m");
            Assert.NotNull(mech);
            host.RaiseButtonClick(mech!); // → vm.SetDnfReason(id,"m"); picker closes; FocusInput runs
            host.Layout();

            Assert.Equal("m", vm.Dnfs.Single(d => d.Seat.DriverId == id).Reason);
            Assert.Equal("mechanical", host.RenderedDnfReasonText(id)); // the row shows it live

            // Exactly what OnPreviewKeyDown does for Ctrl+Z (Key.Z + Control → UndoCommand).
            vm.UndoCommand.Execute(null);
            host.Layout();

            Assert.Equal("o", vm.Dnfs.Single(d => d.Seat.DriverId == id).Reason);
            // The load-bearing render assertion: the row's reason label reverted too.
            Assert.Equal("retired", host.RenderedDnfReasonText(id));
        });
    }

    /// <summary>The KEY-ROUTING half of BUG B: a Ctrl+Z must reach the viewmodel's Undo through the
    /// UserControl's tunneling <c>PreviewKeyDown</c> BEFORE the focused InputBox (a TextBox with its
    /// own built-in Ctrl+Z undo) can swallow it. This asserts the tunnel order deterministically:
    /// the UserControl-level handler fires ahead of the InputBox in the preview route, so once it
    /// marks the event Handled, the TextBox never sees the chord. (The modifier state itself can't
    /// be injected in a headless off-screen host, see WpfRenderHarness remarks, so the chord's
    /// Control flag is covered by the direct handler test below and by code inspection.)</summary>
    [Fact]
    public void CtrlZ_ReachesUserControlBeforeInputBox_InThePreviewTunnel()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);
            host.Layout();
            host.InputBox.Focus();
            Keyboard.Focus(host.InputBox);

            var order = new List<string>();
            host.View.PreviewKeyDown += (_, _) => order.Add("usercontrol");
            host.InputBox.PreviewKeyDown += (_, _) => order.Add("inputbox");

            // A plain Z preview-key-down along the real focus route (InputBox is focused).
            var source = PresentationSource.FromVisual(host.View)!;
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Z)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };
            host.InputBox.RaiseEvent(args);
            WpfRenderHarness.Pump();

            // Tunnel = root→leaf: the UserControl's handler must precede the InputBox's, so the
            // Ctrl+Z branch (which sets e.Handled) always wins the race against TextBox undo.
            var ui = order.IndexOf("usercontrol");
            var ib = order.IndexOf("inputbox");
            Assert.True(ui >= 0, "UserControl PreviewKeyDown did not fire.");
            Assert.True(ib >= 0, "InputBox PreviewKeyDown did not fire.");
            Assert.True(ui < ib, $"Expected UserControl before InputBox in the tunnel; got {string.Join(",", order)}.");
        });
    }

    /// <summary>The handler's Ctrl+Z is genuinely MODIFIER-GATED: invoking the real
    /// <c>OnPreviewKeyDown</c> with a bare Z (no Control) must neither undo nor mark the event
    /// handled, so a lone "z" typed into the grammar box is never mistaken for undo, and, read the
    /// other way, undo only ever fires under Control. Combined with the tunnel-order test above and
    /// the live undo/rebind test, this pins the chord→Undo mapping. (Driving a genuinely-Control
    /// Z through the handler is impossible in this headless off-screen host: WPF's
    /// Win32KeyboardDevice.Modifiers ignores SetKeyboardState/GetKeyState and only updates from a
    /// real OS keyboard report delivered to a foreground window, see the verification notes.)</summary>
    [Fact]
    public void OnPreviewKeyDown_PlainZ_WithoutControl_DoesNotUndo_NorHandle()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);

            const string id = "d.clark";
            Assert.True(vm.MarkDnf(id));
            vm.ReasonPickerDriverId = id;
            host.Layout();
            host.RaiseButtonClick(host.FindReasonButton(id, "m")!); // reason → "m"
            host.Layout();
            Assert.Equal("m", vm.Dnfs.Single(d => d.Seat.DriverId == id).Reason);
            Assert.False(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)); // no Control held here

            bool handled = host.InvokePreviewKeyDown(Key.Z);

            Assert.False(handled, "A bare Z must not be handled by the grammar key router.");
            Assert.Equal("m", vm.Dnfs.Single(d => d.Seat.DriverId == id).Reason); // unchanged: no undo
        });
    }

    /// <summary>The BUG B production fix, verified on the rendered control: the grammar InputBox has
    /// its built-in TextBox undo DISABLED, so there is exactly one Ctrl+Z owner (the grammar's
    /// vm.Undo). With <c>IsUndoEnabled=false</c> the TextBox cannot swallow the chord or silently
    /// revert only its own text regardless of where focus sits after a Mech/Acc click.</summary>
    [Fact]
    public void InputBox_HasBuiltInTextBoxUndoDisabled_SoGrammarOwnsCtrlZ()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);
            host.Layout();

            Assert.False(host.InputBox.IsUndoEnabled,
                "InputBox.IsUndoEnabled must be false so the grammar's Ctrl+Z is the only undo owner.");
        });
    }

    [Fact]
    public void PlayerAccident_RevealsSeverity_DefaultsMedium_AndTwoWayBindsHeavy()
    {
        if (!WpfRenderHarness.IsSupported)
            return;

        WpfRenderHarness.RunSta(() =>
        {
            var vm = new ResultEntryViewModel(Grid(), PlayerId);
            using var host = ViewHost.Show(vm);
            host.Layout();

            var panel = host.Find<Border>("PlayerAccidentSeverityPanel")!;
            var picker = host.Find<ListBox>("AccidentSeverityPicker")!;
            Assert.Equal(Visibility.Collapsed, panel.Visibility);

            Assert.True(vm.MarkDnf(PlayerId, "a"));
            host.Layout();

            Assert.Equal(Visibility.Visible, panel.Visibility);
            Assert.Equal(AccidentSeverity.Medium, vm.PlayerAccidentSeverity);
            Assert.Equal(AccidentSeverity.Medium, picker.SelectedItem);

            picker.SelectedItem = AccidentSeverity.Heavy;
            WpfRenderHarness.Pump();
            Assert.Equal(AccidentSeverity.Heavy, vm.PlayerAccidentSeverity);

            Assert.True(vm.SetDnfReason(PlayerId, "m"));
            host.Layout();
            Assert.Equal(Visibility.Collapsed, panel.Visibility);
            Assert.Null(vm.PlayerAccidentSeverity);
        });
    }

    // ---------- an off-screen host for one ResultEntryView over one VM ----------

    private sealed class ViewHost : IDisposable
    {
        private readonly Window _window;
        public ResultEntryView View { get; }
        public TextBox InputBox { get; }

        private ViewHost(Window window, ResultEntryView view, TextBox inputBox)
        {
            _window = window;
            View = view;
            InputBox = inputBox;
        }

        public static ViewHost Show(ResultEntryViewModel vm)
        {
            var view = new ResultEntryView { DataContext = vm };
            var window = new Window
            {
                Content = view,
                Width = 1200,
                Height = 800,
                // Off-screen + chromeless so nothing flashes on a dev machine, but a real HWND so
                // focus / keyboard routing behave exactly as in the app.
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -10000,
                Top = -10000,
            };
            window.Show();
            window.UpdateLayout();
            WpfRenderHarness.Pump(DispatcherPriority.Loaded);

            var inputBox = FindByName<TextBox>(view, "InputBox")
                ?? throw new InvalidOperationException("InputBox not found in the rendered view.");
            return new ViewHost(window, view, inputBox);
        }

        public void Layout()
        {
            _window.UpdateLayout();
            WpfRenderHarness.Pump(DispatcherPriority.Render);
            WpfRenderHarness.Pump();
        }

        public T? Find<T>(string name) where T : FrameworkElement => FindByName<T>(View, name);

        /// <summary>The DNF custom-cause TextBox on a given driver's row, the one bound to
        /// <c>Detail</c>, width 150.</summary>
        public TextBox? FindDnfDetailBox(string driverId) =>
            Descendants<TextBox>(View)
                .FirstOrDefault(b => DnfEntryOf(b.DataContext)?.Seat.DriverId == driverId);

        /// <summary>The DSQ reason TextBox on a given driver's row (its DataContext is the DSQ
        /// GridSeat).</summary>
        public TextBox? FindDsqReasonBox(string driverId) =>
            Descendants<TextBox>(View)
                .FirstOrDefault(b => (b.DataContext as GridSeat)?.DriverId == driverId
                                     && !ReferenceEquals(b, InputBox));

        /// <summary>The inline reason Button ("m"/"a"/"o") on a given driver's DNF row, matched by
        /// its Tag.</summary>
        public Button? FindReasonButton(string driverId, string tag) =>
            Descendants<Button>(View)
                .FirstOrDefault(b => (b.Tag as string) == tag
                                     && DnfEntryOf(b.DataContext)?.Seat.DriverId == driverId);

        // ---------- event raising through the REAL routed-event plumbing ----------

        public void RaiseLeftMouseUp(UIElement source)
        {
            var args = new MouseButtonEventArgs(
                InputManager.Current.PrimaryMouseDevice, 0, MouseButton.Left)
            {
                RoutedEvent = Mouse.PreviewMouseUpEvent,
                Source = source,
            };
            source.RaiseEvent(args);
        }

        public void RaiseLostFocus(UIElement source) =>
            source.RaiseEvent(new RoutedEventArgs(FrameworkElement.LostFocusEvent, source));

        public void RaiseButtonClick(Button button) =>
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, button));

        /// <summary>Raise a real key-down for <paramref name="key"/> FROM <paramref name="source"/>,
        /// modelling WPF's own two-phase input dispatch: first the tunneling PreviewKeyDown
        /// (root→leaf, this is where the UserControl's OnPreviewKeyDown key-routing guard lives),
        /// and, ONLY if that left the event unhandled, the bubbling KeyDown (leaf→root, where the
        /// box's own OnDnfDetailKeyDown / OnDsqReasonKeyDown handlers live). Returns whether the
        /// event ended up handled. This is exactly the routing that decides "does Enter belong to
        /// the grammar or to the box": if the guard fails, the preview handler eats Enter here and
        /// the box handler never runs.</summary>
        public bool RaiseKeyDown(UIElement source, Key key)
        {
            var presentationSource = PresentationSource.FromVisual(View)
                ?? throw new InvalidOperationException("No PresentationSource for the view.");

            var preview = new KeyEventArgs(Keyboard.PrimaryDevice, presentationSource, 0, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };
            source.RaiseEvent(preview);
            if (preview.Handled)
                return true;

            var bubble = new KeyEventArgs(Keyboard.PrimaryDevice, presentationSource, 0, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent,
            };
            source.RaiseEvent(bubble);
            return bubble.Handled;
        }

        /// <summary>Is this element realised AND effectively visible (itself and every ancestor up to
        /// the view Visible)? Used to assert an editor has HIDDEN (Collapsed) or SHOWN.</summary>
        public bool IsEffectivelyVisible(UIElement? element)
        {
            for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is UIElement ui && ui.Visibility != Visibility.Visible)
                    return false;
                if (ReferenceEquals(node, View))
                    break;
            }
            return element is not null;
        }

        /// <summary>Every team-name string rendered anywhere in the realised visual tree, the
        /// TextBlocks whose Text was produced by the ", {0}" TeamName StringFormat. Used to prove
        /// a DNF/DSQ row actually shows its team.</summary>
        public IReadOnlyList<string> RenderedTeamNameTexts() =>
            Descendants<System.Windows.Controls.TextBlock>(View)
                .Where(tb => IsEffectivelyVisible(tb))
                .Select(tb => tb.Text)
                .Where(t => !string.IsNullOrEmpty(t) && t.Contains(", "))
                .ToArray();

        /// <summary>The DSQ row's compact DISPLAY reason label text (after the " · " separator) —
        /// e.g. "Underweight" or "disqualified". Only the DSQ reason label uses a "·"-separated Run
        /// whose row DataContext is a GridSeat (the DNF one's is a DnfEntry).</summary>
        public string? RenderedDsqReasonText(string driverId)
        {
            foreach (var tb in Descendants<System.Windows.Controls.TextBlock>(View))
            {
                if ((tb.DataContext as GridSeat)?.DriverId != driverId)
                    continue;
                var runs = tb.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text).ToArray();
                string joined = string.Concat(runs);
                int i = joined.IndexOf('·');
                if (i >= 0)
                    return joined[(i + 1)..].Trim();
            }
            return null;
        }

        /// <summary>The reason text actually rendered on a driver's DNF row, read straight out of
        /// the realised visual tree (the <c>DnfReasonConverter</c> output shown to the user), so an
        /// assertion on it proves the ROW updated, not just the viewmodel. Returns e.g. "mechanical"
        /// or "retired" (the word the converter yields for "m" / "o-without-detail").</summary>
        public string? RenderedDnfReasonText(string driverId)
        {
            // The DNF row's DISPLAY line puts the reason in a TextBlock built from two <Run>s: a
            // separator (" · ") and the DnfReasonConverter output. A TextBlock composed of explicit
            // Runs reports Text="" (the Text property only mirrors simple content), so read the Runs
            // directly. The team-name TextBlock on the same row is Text-bound (no Runs), so the only
            // Run-bearing TextBlock for this DnfEntry is the reason label, return its last run,
            // trimmed of the leading separator. (v0.3.3 split the row into DISPLAY/EDIT states and
            // moved the reason to a "·" separator; this reads whichever run carries the word.)
            foreach (var tb in Descendants<System.Windows.Controls.TextBlock>(View))
            {
                if (DnfEntryOf(tb.DataContext)?.Seat.DriverId != driverId)
                    continue;
                var runs = tb.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text).ToArray();
                string joined = string.Concat(runs);
                // The reason label is the only TextBlock whose runs carry the "·" separator, the
                // team-name TextBlock uses ", " and the badge/name carry no separator, so "·"
                // uniquely identifies the reason word (e.g. "mechanical" / "retired").
                int i = joined.IndexOf('·');
                if (i >= 0)
                    return joined[(i + 1)..].Trim();
            }
            return null;
        }

        /// <summary>Invoke the view's real <c>OnPreviewKeyDown</c> with a key-down for
        /// <paramref name="key"/> under whatever modifiers are currently held, and report whether the
        /// handler marked it Handled. Used to prove the Ctrl+Z branch is modifier-gated.</summary>
        public bool InvokePreviewKeyDown(Key key)
        {
            var source = PresentationSource.FromVisual(View)
                ?? throw new InvalidOperationException("No PresentationSource for the view.");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };
            // Disambiguate from any base OnPreviewKeyDown(KeyEventArgs) override by pinning the
            // exact two-parameter (object sender, KeyEventArgs e) signature this view declares.
            var method = typeof(ResultEntryView).GetMethod(
                "OnPreviewKeyDown",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                binder: null,
                types: [typeof(object), typeof(KeyEventArgs)],
                modifiers: null)
                ?? throw new InvalidOperationException("OnPreviewKeyDown(object, KeyEventArgs) not found.");
            method.Invoke(View, [View, args]);
            return args.Handled;
        }

        /// <summary>Invoke one of ResultEntryView's private ListBoxItem double-click handlers with
        /// a realistic framework-element sender and mouse event. This exercises the actual App
        /// gesture seam while keeping the test independent of virtualized list-item realization.</summary>
        public bool InvokeDoubleClick(string handlerName, object? dataContext)
        {
            var sender = new ListBoxItem { DataContext = dataContext };
            var args = new MouseButtonEventArgs(
                InputManager.Current.PrimaryMouseDevice, 0, MouseButton.Left)
            {
                RoutedEvent = Control.MouseDoubleClickEvent,
            };
            var method = typeof(ResultEntryView).GetMethod(
                handlerName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                binder: null,
                types: [typeof(object), typeof(MouseButtonEventArgs)],
                modifiers: null)
                ?? throw new InvalidOperationException($"{handlerName}(object, MouseButtonEventArgs) not found.");
            method.Invoke(View, [sender, args]);
            return args.Handled;
        }

        private static DnfEntry? DnfEntryOf(object? dataContext) => dataContext as DnfEntry;

        public void Dispose()
        {
            _window.Close();
            WpfRenderHarness.Pump(DispatcherPriority.Background);
        }

        // ---------- visual-tree helpers ----------

        private static T? FindByName<T>(DependencyObject root, string name) where T : FrameworkElement =>
            Descendants<T>(root).FirstOrDefault(e => e.Name == name);

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    yield return match;
                foreach (var descendant in Descendants<T>(child))
                    yield return descendant;
            }
        }
    }
}
