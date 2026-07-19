using Companion.Core.Grid;
using Companion.ViewModels.ResultEntry;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The mouse primitives of ResultEntryViewModel (ux-round contract, locked decision #8):
/// InsertAt insert-before semantics incl. edges, MoveTo == penalty reposition, mark/unmark
/// DNF/DSQ from any state, SetDnfReason, bulk DNF as ONE undo step, multi-select state, the
/// inline reason-picker flag, and, the load-bearing property, one undo stack shared with
/// the keyboard grammar: a mixed keyboard+mouse sequence of ten mutations fully unwinds.
/// The grammar tests themselves are untouched; drafts built mouse-only equal drafts built
/// grammar-only for the same result.
/// </summary>
public class ResultEntryMouseTests
{
    private const string PlayerId = "d.amon";

    /// <summary>Same ten-seat roster shape as the grammar tests (two Hills, car 1/10/12).</summary>
    private static readonly (string Id, string Name, string Number)[] Roster =
    [
        ("d.brabham", "Jack Brabham", "1"),
        ("d.hulme", "Denny Hulme", "2"),
        ("d.clark", "Jim Clark", "3"),
        ("d.ghill", "Graham Hill", "4"),
        ("d.phill", "Phil Hill", "5"),
        (PlayerId, "Chris Amon", "6"),
        ("d.merzario", "Arturo Merzario", "7"),
        ("d.stewart", "Jackie Stewart", "8"),
        ("d.gurney", "Dan Gurney", "10"),
        ("d.siffert", "Jo Siffert", "12"),
    ];

    private static IReadOnlyList<GridSeat> Grid() =>
        Roster.Select(r => ResultEntryViewModelTests.Seat(r.Id, r.Name, r.Number, r.Id == PlayerId))
            .ToArray();

    private static ResultEntryViewModel Vm(TimeProvider? clock = null) =>
        new(Grid(), PlayerId, clock);

    private static string[] Ids(IEnumerable<GridSeat> seats) => seats.Select(s => s.DriverId).ToArray();

    // ---------- InsertAt: insert-before semantics ----------

    [Fact]
    public void InsertAt_BuildsTheOrder_InsertBeforeShiftsFollowers()
    {
        var vm = Vm();

        Assert.True(vm.InsertAt("d.clark", 0));   // P1 Clark
        Assert.True(vm.InsertAt("d.hulme", 1));   // P2 Hulme
        Assert.True(vm.InsertAt("d.brabham", 1)); // dropped ON Hulme's slot: inserted BEFORE it

        Assert.Equal(new[] { "d.clark", "d.brabham", "d.hulme" }, Ids(vm.Classified));
        Assert.Equal("3/10 placed", vm.ProgressText);
    }

    [Fact]
    public void InsertAt_Edges_IndexZeroBecomesP1_BeyondCountAppends_NegativeClampsToTop()
    {
        var vm = Vm();
        vm.InsertAt("d.clark", 0);

        Assert.True(vm.InsertAt("d.brabham", 0));   // before P1
        Assert.True(vm.InsertAt("d.stewart", 99));  // beyond the end: append
        Assert.True(vm.InsertAt("d.gurney", -5));   // negative: clamp to the top

        Assert.Equal(new[] { "d.gurney", "d.brabham", "d.clark", "d.stewart" }, Ids(vm.Classified));
    }

    [Fact]
    public void InsertAt_UnknownOrAlreadyPlacedDriver_ReturnsFalseWithoutMutating()
    {
        var vm = Vm();
        vm.InsertAt("d.clark", 0);
        int undoDepth = UndoDepth(vm);

        Assert.False(vm.InsertAt("d.nobody", 0));
        Assert.False(vm.InsertAt("d.clark", 0)); // placed: reorder is MoveTo's job

        Assert.Equal(new[] { "d.clark" }, Ids(vm.Classified));
        Assert.Equal(undoDepth, UndoDepth(vm)); // nothing pushed
    }

    [Fact]
    public void InsertAt_PullsADnfOrDsqDriverBackIntoTheOrder_InOneUndoableStep()
    {
        var vm = Vm();
        vm.MarkDnf("d.stewart");
        vm.MarkDsq("d.gurney");

        Assert.True(vm.InsertAt("d.stewart", 0));
        Assert.True(vm.InsertAt("d.gurney", 0));

        Assert.Equal(new[] { "d.gurney", "d.stewart" }, Ids(vm.Classified));
        Assert.Empty(vm.Dnfs);
        Assert.Empty(vm.Disqualified);

        vm.UndoCommand.Execute(null); // one Ctrl+Z per gesture
        Assert.Equal(new[] { "d.stewart" }, Ids(vm.Classified));
        Assert.Single(vm.Disqualified);
    }

    // ---------- MoveTo == the grammar's penalty reposition ----------

    [Fact]
    public void MoveTo_MatchesTheGrammarRepositionExactly()
    {
        // Grammar: place 1..5 then "hu5" (Hulme P2 -> P5).
        var grammar = Vm();
        var k = new ResultEntryViewModelTests.Keys(grammar);
        foreach (string n in new[] { "1", "2", "3", "4", "5" })
            k.Line(n);
        k.Line("hu5");

        // Mouse: same placements, then MoveTo final index 4.
        var mouse = Vm();
        int i = 0;
        foreach (string id in new[] { "d.brabham", "d.hulme", "d.clark", "d.ghill", "d.phill" })
            mouse.InsertAt(id, i++);
        Assert.True(mouse.MoveTo("d.hulme", 4));

        Assert.Equal(Ids(grammar.Classified), Ids(mouse.Classified));
    }

    [Fact]
    public void MoveTo_ClampsBeyondTheEnds_AndSameIndexIsANoOpWithoutUndo()
    {
        var vm = Vm();
        vm.InsertAt("d.brabham", 0);
        vm.InsertAt("d.hulme", 1);
        vm.InsertAt("d.clark", 2);
        int undoDepth = UndoDepth(vm);

        Assert.True(vm.MoveTo("d.brabham", 99)); // clamp to last
        Assert.Equal(new[] { "d.hulme", "d.clark", "d.brabham" }, Ids(vm.Classified));
        Assert.Equal(undoDepth + 1, UndoDepth(vm));

        Assert.True(vm.MoveTo("d.brabham", 2)); // already there: success, nothing recorded
        Assert.Equal(undoDepth + 1, UndoDepth(vm));
    }

    [Fact]
    public void MoveTo_UnplacedDriver_ReturnsFalse()
    {
        var vm = Vm();
        Assert.False(vm.MoveTo("d.clark", 0));
        Assert.Empty(vm.Classified);
        Assert.False(vm.CanUndo);
    }

    // ---------- MarkDnf / SetDnfReason ----------

    [Fact]
    public void MarkDnf_DefaultsToOther_ExplicitReasonRecorded()
    {
        var vm = Vm();

        Assert.True(vm.MarkDnf("d.stewart"));
        Assert.True(vm.MarkDnf("d.gurney", "m"));

        Assert.Equal(
            new[] { ("d.stewart", ResultEntryViewModel.DefaultDnfReason), ("d.gurney", "m") },
            vm.Dnfs.Select(d => (d.Seat.DriverId, d.Reason)));
    }

    [Fact]
    public void MarkDnf_PlacedDriver_IsPulledOutOfTheClassification()
    {
        var vm = Vm();
        vm.InsertAt("d.brabham", 0);
        vm.InsertAt("d.hulme", 1);
        vm.InsertAt("d.clark", 2);

        Assert.True(vm.MarkDnf("d.hulme", "a"));

        Assert.Equal(new[] { "d.brabham", "d.clark" }, Ids(vm.Classified));
        Assert.Equal(new[] { "d.hulme" }, vm.Dnfs.Select(d => d.Seat.DriverId));
        Assert.Equal("3/10 placed", vm.ProgressText); // still three resolved seats
    }

    [Fact]
    public void MarkDnf_InvalidReasonOrAlreadyDnf_ReturnsFalse()
    {
        var vm = Vm();
        vm.MarkDnf("d.stewart");
        int undoDepth = UndoDepth(vm);

        Assert.False(vm.MarkDnf("d.stewart"));      // already DNF
        Assert.False(vm.MarkDnf("d.gurney", "x"));  // not m/a/o
        Assert.Equal(undoDepth, UndoDepth(vm));
    }

    [Fact]
    public void SetDnfReason_ChangesTheReason_AndIsUndoable()
    {
        var vm = Vm();
        vm.MarkDnf("d.stewart"); // "o"

        Assert.True(vm.SetDnfReason("d.stewart", "m"));
        Assert.Equal("m", vm.Dnfs.Single().Reason);

        vm.UndoCommand.Execute(null);
        Assert.Equal("o", vm.Dnfs.Single().Reason);
    }

    [Fact]
    public void SetDnfReason_SameReason_SucceedsWithoutAnUndoEntry()
    {
        var vm = Vm();
        vm.MarkDnf("d.stewart", "m");
        int undoDepth = UndoDepth(vm);

        Assert.True(vm.SetDnfReason("d.stewart", "m"));
        Assert.Equal(undoDepth, UndoDepth(vm));
    }

    [Fact]
    public void SetDnfReason_NotDnfOrInvalidReason_ReturnsFalse()
    {
        var vm = Vm();
        vm.InsertAt("d.clark", 0);

        Assert.False(vm.SetDnfReason("d.clark", "m"));   // classified, not DNF
        Assert.False(vm.SetDnfReason("d.stewart", "m")); // unresolved
        vm.MarkDnf("d.stewart");
        Assert.False(vm.SetDnfReason("d.stewart", "z")); // invalid letter
    }

    [Fact]
    public void ReasonPicker_ClearedBySetDnfReasonAndUnmark()
    {
        var vm = Vm();
        vm.MarkDnf("d.stewart");
        vm.ReasonPickerDriverId = "d.stewart"; // the drop handler shows the picker

        Assert.True(vm.SetDnfReason("d.stewart", "a"));
        Assert.Null(vm.ReasonPickerDriverId);

        vm.MarkDnf("d.gurney");
        vm.ReasonPickerDriverId = "d.gurney";
        Assert.True(vm.Unmark("d.gurney"));
        Assert.Null(vm.ReasonPickerDriverId);
    }

    // ---------- v0.3.3: the single "row being edited" state (DNF picker + DSQ box) ----------

    [Fact]
    public void BeginEditingReason_OpensOnResolvedRows_DnfAndDsq_ReplacingAnyOpenEditor()
    {
        var vm = Vm();
        vm.MarkDnf("d.stewart");
        vm.MarkDsq("d.gurney");

        // A DNF row can be edited.
        vm.BeginEditingReason("d.stewart");
        Assert.Equal("d.stewart", vm.EditingReasonDriverId);
        Assert.Equal("d.stewart", vm.ReasonPickerDriverId); // the alias tracks it

        // Editing another resolved row replaces the open editor, only one row edits at a time.
        vm.BeginEditingReason("d.gurney");
        Assert.Equal("d.gurney", vm.EditingReasonDriverId);
    }

    [Fact]
    public void BeginEditingReason_IsNoOp_ForRemainingOrClassifiedDrivers()
    {
        var vm = Vm();
        vm.InsertAt("d.clark", 0); // classified

        vm.BeginEditingReason("d.brabham"); // still Remaining
        Assert.Null(vm.EditingReasonDriverId);

        vm.BeginEditingReason("d.clark"); // classified, not DNF/DSQ, no reason to edit
        Assert.Null(vm.EditingReasonDriverId);
    }

    [Fact]
    public void StopEditingReason_ReturnsRowToDisplay()
    {
        var vm = Vm();
        vm.MarkDsq("d.gurney");
        vm.BeginEditingReason("d.gurney");
        Assert.Equal("d.gurney", vm.EditingReasonDriverId);

        vm.StopEditingReason();
        Assert.Null(vm.EditingReasonDriverId);
    }

    [Fact]
    public void EditingState_IsPureViewState_NotUndone()
    {
        // Opening/closing the editor must never push to the undo stack (it is not a draft change).
        var vm = Vm();
        vm.MarkDsq("d.gurney"); // this DOES push one undo snapshot
        bool couldUndoAfterMark = vm.CanUndo;

        vm.BeginEditingReason("d.gurney");
        vm.StopEditingReason();

        Assert.Equal(couldUndoAfterMark, vm.CanUndo); // unchanged by edit open/close
        // And a single undo still reverts the mark itself (edit state didn't consume an undo).
        vm.UndoCommand.Execute(null);
        Assert.DoesNotContain("d.gurney", Ids(vm.Disqualified));
    }

    [Fact]
    public void ReasonPickerAlias_ReflectsEditingReasonDriverId_BothWays()
    {
        var vm = Vm();
        vm.MarkDnf("d.stewart");

        vm.ReasonPickerDriverId = "d.stewart";
        Assert.Equal("d.stewart", vm.EditingReasonDriverId); // set via alias → backing set

        vm.EditingReasonDriverId = null;
        Assert.Null(vm.ReasonPickerDriverId); // read via alias → backing read
    }

    // ---------- the mistaken-DNF fix: removable BEFORE and AFTER a reason is set ----------

    [Fact]
    public void MistakenDnf_IsRemovable_BeforeAnyReasonIsChosen()
    {
        // Mike's report: dropping a driver to DNF must never trap them, even with no reason.
        var vm = Vm();
        Assert.True(vm.MarkDnf("d.stewart")); // defaults to "o", no reason picked yet

        // drag back to Remaining
        Assert.True(vm.Unmark("d.stewart"));
        Assert.Empty(vm.Dnfs);
        Assert.Contains("d.stewart", Ids(vm.Remaining));

        // and the same via Ctrl+Z on a fresh mistake
        Assert.True(vm.MarkDnf("d.gurney"));
        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.Dnfs);
        Assert.Contains("d.gurney", Ids(vm.Remaining));
    }

    [Fact]
    public void MistakenDnf_IsRemovable_AfterAReasonIsSet()
    {
        var vm = Vm();
        vm.MarkDnf("d.stewart");
        Assert.True(vm.SetDnfReason("d.stewart", "m")); // a reason was chosen

        // still fully removable, no blocking state
        Assert.True(vm.Unmark("d.stewart"));
        Assert.Empty(vm.Dnfs);
        Assert.Contains("d.stewart", Ids(vm.Remaining));
    }

    [Fact]
    public void MarkDnfAndSetReason_AreIndependent_EachSeparatelyUndoable()
    {
        var vm = Vm();
        vm.MarkDnf("d.stewart");           // step 1: mark (reason "o")
        vm.SetDnfReason("d.stewart", "a"); // step 2: reason edit

        vm.UndoCommand.Execute(null);      // undo only the reason edit
        Assert.Equal("o", vm.Dnfs.Single().Reason);
        Assert.Single(vm.Dnfs);            // still DNF

        vm.UndoCommand.Execute(null);      // undo the mark itself
        Assert.Empty(vm.Dnfs);
        Assert.Contains("d.stewart", Ids(vm.Remaining));
    }

    // ---------- custom "Other" free text ----------

    [Fact]
    public void SetDnfDetail_RoundTripsCustomOtherTextIntoTheDraft()
    {
        var vm = Vm();
        vm.MarkDnf("d.stewart"); // "o"
        Assert.True(vm.SetDnfDetail("d.stewart", "Engine fire"));

        var entry = vm.Dnfs.Single();
        Assert.Equal("o", entry.Reason);
        Assert.Equal("Engine fire", entry.Detail);
        Assert.False(entry.DriverAttributed);

        var draft = vm.BuildDraft();
        Assert.Equal("o", draft.DidNotFinish["d.stewart"]);           // letter seam intact
        Assert.Equal("Engine fire", draft.DidNotFinishDetail["d.stewart"].Text);
        Assert.False(draft.DidNotFinishDetail["d.stewart"].DriverAttributed);
    }

    [Fact]
    public void SetDnfDetail_DriverAttributedFlag_RoundTrips()
    {
        var vm = Vm();
        vm.MarkDnf("d.stewart");
        Assert.True(vm.SetDnfDetail("d.stewart", "Spun off", driverAttributed: true));

        var draft = vm.BuildDraft();
        Assert.True(draft.DidNotFinishDetail["d.stewart"].DriverAttributed);
        Assert.Equal("Spun off", draft.DidNotFinishDetail["d.stewart"].Text);
    }

    [Fact]
    public void SetDnfDetail_IsUndoable_AndClearedWhenReasonLeavesOther()
    {
        var vm = Vm();
        vm.MarkDnf("d.stewart");
        vm.SetDnfDetail("d.stewart", "Engine fire");

        // switching to mechanical drops the custom detail (m has a fixed meaning)
        Assert.True(vm.SetDnfReason("d.stewart", "m"));
        Assert.Null(vm.Dnfs.Single().Detail);
        var draft = vm.BuildDraft();
        Assert.False(draft.DidNotFinishDetail.ContainsKey("d.stewart"));

        // undo restores the "o" + custom text as one step
        vm.UndoCommand.Execute(null);
        Assert.Equal("o", vm.Dnfs.Single().Reason);
        Assert.Equal("Engine fire", vm.Dnfs.Single().Detail);
    }

    [Fact]
    public void SetDnfDetail_OnANonDnfDriver_ReturnsFalse()
    {
        var vm = Vm();
        vm.InsertAt("d.clark", 0);
        Assert.False(vm.SetDnfDetail("d.clark", "whatever")); // classified, not DNF
        Assert.False(vm.SetDnfDetail("d.stewart", "whatever")); // unresolved
    }

    [Fact]
    public void CustomOtherText_DoesNotLeakIntoUntouchedDnfs()
    {
        var vm = Vm();
        vm.MarkDnf("d.stewart", "m"); // mechanical, no detail
        vm.MarkDnf("d.gurney");       // plain "o", no detail

        var draft = vm.BuildDraft();
        Assert.Empty(draft.DidNotFinishDetail); // nothing customised → empty map
    }

    // ---------- custom DSQ reason ----------

    [Fact]
    public void SetDsqReason_RoundTripsCustomTextIntoTheDraft()
    {
        var vm = Vm();
        vm.MarkDsq("d.stewart");
        Assert.True(vm.SetDsqReason("d.stewart", "Underweight"));
        Assert.Equal("Underweight", vm.DsqReasonOf("d.stewart"));

        var draft = vm.BuildDraft();
        Assert.Contains("d.stewart", draft.Disqualified);          // list seam intact
        Assert.Equal("Underweight", draft.DisqualifiedDetail["d.stewart"]);
    }

    [Fact]
    public void SetDsqReason_IsUndoable_AndDroppedWhenTheDriverIsUnmarked()
    {
        var vm = Vm();
        vm.MarkDsq("d.stewart");
        vm.SetDsqReason("d.stewart", "Illegal wing");

        vm.UndoCommand.Execute(null); // undo the reason only
        Assert.Equal("", vm.DsqReasonOf("d.stewart"));
        Assert.Single(vm.Disqualified); // still DSQ

        // set it again, then unmark: the reason must not survive back to Remaining
        vm.SetDsqReason("d.stewart", "Illegal wing");
        vm.Unmark("d.stewart");
        Assert.Equal("", vm.DsqReasonOf("d.stewart"));
        vm.MarkDsq("d.stewart");
        Assert.Equal("", vm.DsqReasonOf("d.stewart")); // fresh DSQ, no stale reason
    }

    [Fact]
    public void SetDsqReason_OnANonDsqDriver_ReturnsFalse()
    {
        var vm = Vm();
        vm.InsertAt("d.clark", 0);
        Assert.False(vm.SetDsqReason("d.clark", "x")); // classified, not DSQ
        Assert.False(vm.SetDsqReason("d.stewart", "x")); // unresolved
    }

    [Fact]
    public void CustomDsqReason_DoesNotLeakIntoUntouchedDsqs()
    {
        var vm = Vm();
        vm.MarkDsq("d.stewart"); // no reason
        var draft = vm.BuildDraft();
        Assert.Empty(draft.DisqualifiedDetail);
    }

    // ---------- MarkDsq / Unmark ----------

    [Fact]
    public void MarkDsq_FromRemainingAndFromTheClassification()
    {
        var vm = Vm();
        vm.InsertAt("d.brabham", 0);
        vm.InsertAt("d.hulme", 1);

        Assert.True(vm.MarkDsq("d.stewart"));  // unplaced
        Assert.True(vm.MarkDsq("d.brabham"));  // placed: pulled out, follower shifts up

        Assert.Equal(new[] { "d.hulme" }, Ids(vm.Classified));
        Assert.Equal(new[] { "d.stewart", "d.brabham" }, Ids(vm.Disqualified));
        Assert.False(vm.MarkDsq("d.stewart")); // already DSQ
    }

    [Fact]
    public void Unmark_FromEveryResolvedState_BackToRemainingInGridOrder()
    {
        var vm = Vm();
        vm.InsertAt("d.clark", 0);
        vm.MarkDnf("d.stewart", "m");
        vm.MarkDsq("d.gurney");
        Assert.Equal("3/10 placed", vm.ProgressText);

        Assert.True(vm.Unmark("d.clark"));
        Assert.True(vm.Unmark("d.stewart"));
        Assert.True(vm.Unmark("d.gurney"));

        Assert.Empty(vm.Classified);
        Assert.Empty(vm.Dnfs);
        Assert.Empty(vm.Disqualified);
        Assert.Equal(Ids(Grid()), Ids(vm.Remaining)); // grid order, not unmark order
        Assert.Equal("0/10 placed", vm.ProgressText);

        Assert.False(vm.Unmark("d.clark")); // already unresolved
    }

    // ---------- bulk DNF ----------

    [Fact]
    public void MarkDnfBulk_MarksInGivenOrder_OneUndoStepRestoresAll()
    {
        var vm = Vm();
        vm.InsertAt("d.brabham", 0);
        int undoDepth = UndoDepth(vm);

        Assert.True(vm.MarkDnfBulk(["d.stewart", "d.gurney", "d.siffert"]));

        Assert.Equal(
            new[] { "d.stewart", "d.gurney", "d.siffert" },
            vm.Dnfs.Select(d => d.Seat.DriverId));
        Assert.All(vm.Dnfs, d => Assert.Equal(ResultEntryViewModel.DefaultDnfReason, d.Reason));
        Assert.Equal("4/10 placed", vm.ProgressText);
        Assert.Equal(undoDepth + 1, UndoDepth(vm)); // ONE snapshot for the whole gesture

        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.Dnfs);
        Assert.Equal("1/10 placed", vm.ProgressText);
    }

    [Fact]
    public void MarkDnfBulk_SkipsAlreadyDnf_FalseWhenNothingMarkable()
    {
        var vm = Vm();
        vm.MarkDnf("d.stewart");

        Assert.True(vm.MarkDnfBulk(["d.stewart", "d.gurney"]));
        Assert.Equal(new[] { "d.stewart", "d.gurney" }, vm.Dnfs.Select(d => d.Seat.DriverId));

        int undoDepth = UndoDepth(vm);
        Assert.False(vm.MarkDnfBulk(["d.stewart", "d.gurney"])); // all already DNF
        Assert.False(vm.MarkDnfBulk([]));
        Assert.Equal(undoDepth, UndoDepth(vm));
    }

    [Fact]
    public void MarkDnfBulk_ClearsTheMultiSelection()
    {
        var vm = Vm();
        vm.ToggleSelected("d.stewart");
        vm.ToggleSelected("d.gurney");
        Assert.Equal(2, vm.SelectedDriverIds.Count);

        vm.MarkDnfBulk(vm.SelectedDriverIds.ToArray());

        Assert.Empty(vm.SelectedDriverIds);
    }

    // ---------- multi-select state ----------

    [Fact]
    public void Selection_ToggleAddsAndRemoves_UnknownIdsIgnored_ClearEmpties()
    {
        var vm = Vm();

        vm.ToggleSelected("d.stewart");
        vm.ToggleSelected("d.gurney");
        vm.ToggleSelected("d.nobody"); // not on the grid: ignored
        Assert.True(vm.IsSelected("d.stewart"));
        Assert.True(vm.IsSelected("d.gurney"));
        Assert.Equal(2, vm.SelectedDriverIds.Count);

        vm.ToggleSelected("d.stewart"); // toggle off
        Assert.False(vm.IsSelected("d.stewart"));
        Assert.Single(vm.SelectedDriverIds);

        vm.ClearSelection();
        Assert.Empty(vm.SelectedDriverIds);
    }

    // ---------- the shared undo stack: mixed keyboard + mouse ----------

    [Fact]
    public void Undo_TenMixedKeyboardAndMouseMutations_FullyUnwound()
    {
        var vm = Vm();
        var k = new ResultEntryViewModelTests.Keys(vm);

        k.Line("3");                                              // 1 kb: assign Clark
        Assert.True(vm.InsertAt("d.brabham", 0));                 // 2 mouse: Brabham before P1
        k.Line("hu");                                             // 3 kb: assign Hulme
        Assert.True(vm.MoveTo("d.clark", 2));                     // 4 mouse: Clark to P3
        Assert.True(vm.MarkDnf("d.stewart", "a"));                // 5 mouse: DNF accident
        Assert.True(vm.SetDnfReason("d.stewart", "m"));           // 6 mouse: reason edit
        Assert.True(vm.MarkDsq("d.gurney"));                      // 7 mouse: DSQ unplaced
        k.Line("5q");                                             // 8 kb: DSQ Phil Hill
        Assert.True(vm.Unmark("d.stewart"));                      // 9 mouse: back to remaining
        Assert.True(vm.MarkDnfBulk(["d.merzario", "d.siffert"])); // 10 mouse: bulk = 1 step

        Assert.Equal(new[] { "d.brabham", "d.hulme", "d.clark" }, Ids(vm.Classified));
        Assert.Equal(new[] { "d.merzario", "d.siffert" }, vm.Dnfs.Select(d => d.Seat.DriverId));
        Assert.Equal(new[] { "d.gurney", "d.phill" }, Ids(vm.Disqualified));
        Assert.Equal("7/10 placed", vm.ProgressText);

        for (int i = 0; i < 10; i++)
        {
            Assert.True(vm.CanUndo);
            vm.UndoCommand.Execute(null);
        }

        Assert.Empty(vm.Classified);
        Assert.Empty(vm.Dnfs);
        Assert.Empty(vm.Disqualified);
        Assert.Equal(Ids(Grid()), Ids(vm.Remaining));
        Assert.Equal("0/10 placed", vm.ProgressText);
        Assert.False(vm.CanUndo);

        vm.UndoCommand.Execute(null); // 11th: no-op, no throw
        Assert.Equal("0/10 placed", vm.ProgressText);
    }

    // ---------- parity: a mouse-only draft equals the grammar-only draft ----------

    [Fact]
    public void MouseOnlyEntry_ProducesTheSameDraftAsTheGrammar()
    {
        // Grammar route.
        var grammar = Vm();
        var k = new ResultEntryViewModelTests.Keys(grammar);
        k.Line("1");
        k.Line("2");
        k.Line("3");
        k.Line("1 3");  // car 1: P1 -> P3
        k.Line("4q");   // DSQ Graham Hill
        k.F8();
        k.Line("5 m");  // Phil Hill, mechanical
        k.Enter();      // bulk: Amon (list order)
        k.Enter();      // bulk: Merzario
        k.Enter();      // bulk: Stewart
        k.Enter();      // bulk: Gurney
        k.Enter();      // bulk: Siffert
        Assert.True(grammar.IsComplete);

        // Mouse route: same result, only mouse primitives.
        var mouse = Vm();
        mouse.InsertAt("d.brabham", 0);
        mouse.InsertAt("d.hulme", 1);
        mouse.InsertAt("d.clark", 2);
        mouse.MoveTo("d.brabham", 2);
        mouse.MarkDsq("d.ghill");
        mouse.MarkDnf("d.phill", "m");
        mouse.MarkDnfBulk(Ids(mouse.Remaining)); // remaining, in list order, like ↵↵↵
        Assert.True(mouse.IsComplete);

        var expected = grammar.BuildDraft();
        var actual = mouse.BuildDraft();
        Assert.Equal(expected.Classified, actual.Classified);
        Assert.Equal(expected.Disqualified, actual.Disqualified);
        Assert.Equal(
            expected.DidNotFinish.OrderBy(p => p.Key, StringComparer.Ordinal),
            actual.DidNotFinish.OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    // ---------- timer ----------

    [Fact]
    public void MouseMutations_StartTheEntryTimer()
    {
        var clock = new ResultEntryViewModelTests.FakeClock();
        var vm = Vm(clock);

        clock.Advance(TimeSpan.FromMinutes(3)); // idle before the first interaction
        Assert.Equal(TimeSpan.Zero, vm.Elapsed);

        vm.InsertAt("d.clark", 0);
        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(30), vm.Elapsed);
    }

    // ---------- helpers ----------

    /// <summary>The undo stack is private by design (the VM only exposes CanUndo), but
    /// several contracts here are about EXACTLY how many snapshots an operation pushes —
    /// bulk = one, no-ops = zero. Reading the private field via reflection keeps those
    /// assertions direct without widening the production surface.</summary>
    private static int UndoDepth(ResultEntryViewModel vm)
    {
        var field = typeof(ResultEntryViewModel).GetField(
            "_undoStack",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var stack = (System.Collections.ICollection)field.GetValue(vm)!;
        return stack.Count;
    }
}
