using Companion.Core.Career;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.ViewModels.ResultEntry;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The result-entry keyboard grammar (docs/dev/app-shell.md), rule by rule: number match,
/// surname prefixes, ambiguity + Tab cycling, unplaced-only filtering, 'me', the F8 DNF
/// phase (bulk Enter, reasons), DSQ, penalty re-positioning, unlimited undo, Esc, progress,
/// completion, and the injectable-clock timer.
/// </summary>
public class ResultEntryViewModelTests
{
    // ---------- fixture ----------

    private const string PlayerId = "d.amon";

    /// <summary>Ten seats, grid order. Deliberate traps: two Hills (always-ambiguous "hi"),
    /// Merzario (surname prefix "me" colliding with the reserved player token), car numbers
    /// 1/10/12 (exact-vs-prefix number matching).</summary>
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

    internal static readonly PackDriverRatings Ratings = new()
    {
        RaceSkill = 0.9, QualifyingSkill = 0.9, Aggression = 0.5, Defending = 0.5,
        Stamina = 0.5, Consistency = 0.5, StartReactions = 0.5, WetSkill = 0.5,
        TyreManagement = 0.5, AvoidanceOfMistakes = 0.5,
    };

    internal static GridSeat Seat(string id, string name, string number, bool isPlayer = false) => new()
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
        IsPlayer = isPlayer,
    };

    private static IReadOnlyList<GridSeat> Grid() =>
        Roster.Select(r => Seat(r.Id, r.Name, r.Number, r.Id == PlayerId)).ToArray();

    private static ResultEntryViewModel Vm(TimeProvider? clock = null) =>
        new(Grid(), PlayerId, clock);

    internal sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>Drives the VM the way the view would, counting keystrokes (a chord = 1).</summary>
    internal sealed class Keys(ResultEntryViewModel vm)
    {
        public int Count { get; private set; }

        public void Type(string text)
        {
            foreach (char c in text)
            {
                vm.Input += c;
                Count++;
            }
        }

        public void Enter() { vm.SubmitCommand.Execute(null); Count++; }
        public void Tab() { vm.CycleCandidateCommand.Execute(null); Count++; }
        public void F8() { vm.ToggleDnfPhaseCommand.Execute(null); Count++; }
        public void CtrlZ() { vm.UndoCommand.Execute(null); Count++; }
        public void Esc() { vm.ClearInputCommand.Execute(null); Count++; }

        public void Line(string text) { Type(text); Enter(); }
    }

    private static string[] Ids(IEnumerable<GridSeat> seats) => seats.Select(s => s.DriverId).ToArray();

    // ---------- construction & initial state ----------

    [Fact]
    public void Constructor_RejectsBadArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new ResultEntryViewModel(null!, PlayerId));
        Assert.Throws<ArgumentException>(() => new ResultEntryViewModel(Array.Empty<GridSeat>(), PlayerId));
        Assert.Throws<ArgumentException>(() => new ResultEntryViewModel(Grid(), ""));
    }

    [Fact]
    public void InitialState_IsPristine()
    {
        var vm = Vm();

        Assert.Equal(ResultEntryPhase.Classified, vm.Phase);
        Assert.Equal("", vm.Input);
        Assert.Empty(vm.Candidates);
        Assert.Null(vm.SelectedCandidate);
        Assert.Empty(vm.Classified);
        Assert.Empty(vm.Dnfs);
        Assert.Empty(vm.Disqualified);
        Assert.Equal(Ids(Grid()), Ids(vm.Remaining));
        Assert.Equal("0/10 placed", vm.ProgressText);
        Assert.False(vm.IsComplete);
        Assert.False(vm.CanUndo);
        Assert.Equal(TimeSpan.Zero, vm.Elapsed);
    }

    // ---------- car-number matching ----------

    [Fact]
    public void NumberExact_EnterAssignsNextOpenPosition()
    {
        var vm = Vm();
        var k = new Keys(vm);

        k.Line("3");
        k.Line("8");

        Assert.Equal(new[] { "d.clark", "d.stewart" }, Ids(vm.Classified));
        Assert.Equal("", vm.Input);
        Assert.Equal("2/10 placed", vm.ProgressText);
    }

    [Fact]
    public void NumberExact_BeatsPrefixMatches()
    {
        var vm = Vm();
        vm.Input = "1"; // exact car 1 exists alongside 10 and 12

        Assert.Equal(new[] { "d.brabham" }, Ids(vm.Candidates));
        Assert.False(vm.IsAmbiguous);
    }

    [Fact]
    public void NumberPrefix_AmbiguousWhenNoExact_TabCyclesThenEnterAssigns()
    {
        var vm = Vm();
        var k = new Keys(vm);
        k.Line("1"); // consume the exact match

        vm.Input = "1"; // now only 10 and 12 remain as prefix matches
        Assert.Equal(new[] { "d.gurney", "d.siffert" }, Ids(vm.Candidates));
        Assert.True(vm.IsAmbiguous);
        Assert.Equal("d.gurney", vm.SelectedCandidate!.DriverId);

        k.Tab();
        Assert.Equal("d.siffert", vm.SelectedCandidate!.DriverId);

        k.Enter();
        Assert.Equal(new[] { "d.brabham", "d.siffert" }, Ids(vm.Classified));
    }

    [Fact]
    public void Number_MatchesUnplacedOnly()
    {
        var vm = Vm();
        var k = new Keys(vm);
        k.Line("3");

        vm.Input = "3";
        Assert.Empty(vm.Candidates);

        k.Enter();
        Assert.NotNull(vm.ErrorText);
        Assert.Single(vm.Classified);
    }

    // ---------- surname-prefix matching ----------

    [Fact]
    public void SurnamePrefix_UnambiguousAssigns()
    {
        var vm = Vm();
        var k = new Keys(vm);

        k.Line("cla");
        k.Line("st"); // two letters are enough

        Assert.Equal(new[] { "d.clark", "d.stewart" }, Ids(vm.Classified));
    }

    [Fact]
    public void SurnamePrefix_LongerThanThreeLetters_StillMatches()
    {
        var vm = Vm();
        var k = new Keys(vm);

        k.Line("merz");

        Assert.Equal(new[] { "d.merzario" }, Ids(vm.Classified));
    }

    [Fact]
    public void SingleLetter_NeverMatches()
    {
        var vm = Vm();
        vm.Input = "c";

        Assert.Empty(vm.Candidates);
    }

    [Fact]
    public void SurnamePrefix_IsCaseInsensitive()
    {
        var vm = Vm();
        var k = new Keys(vm);

        k.Line("CLA");

        Assert.Equal(new[] { "d.clark" }, Ids(vm.Classified));
    }

    [Fact]
    public void SurnamePrefix_MatchesUnplacedOnly()
    {
        var vm = Vm();
        var k = new Keys(vm);
        k.Line("4"); // Graham Hill placed

        vm.Input = "hi"; // only Phil Hill left
        Assert.Equal(new[] { "d.phill" }, Ids(vm.Candidates));
        Assert.False(vm.IsAmbiguous);

        k.Enter();
        Assert.Equal(new[] { "d.ghill", "d.phill" }, Ids(vm.Classified));
    }

    [Fact]
    public void SurnameMatch_IgnoresAccents_TypedWithoutTheDiacritic()
    {
        // A surname with a diacritic must match when the player types the plain ASCII letters —
        // most keyboards can't type "é". (Also the reverse, so typing the accent still works.)
        var grid = new[]
        {
            Seat("d.perez", "Luis Pérez-Sala", "24"),
            Seat("d.rai", "Kimi Räikkönen", "7"),
            Seat(PlayerId, "Test Player", "3", isPlayer: true),
        };
        var vm = new ResultEntryViewModel(grid, PlayerId);

        vm.Input = "perez";                       // no accent
        Assert.Equal(new[] { "d.perez" }, Ids(vm.Candidates));

        vm.Input = "pérez";                       // with accent still works
        Assert.Equal(new[] { "d.perez" }, Ids(vm.Candidates));

        vm.Input = "raik";                        // "Räikkönen" typed plain
        Assert.Equal(new[] { "d.rai" }, Ids(vm.Candidates));
    }

    [Fact]
    public void AmbiguousPrefix_ShowsCandidatesInGridOrder_TabWraps()
    {
        var vm = Vm();
        var k = new Keys(vm);

        vm.Input = "hi";
        Assert.Equal(new[] { "d.ghill", "d.phill" }, Ids(vm.Candidates));
        Assert.Equal("d.ghill", vm.SelectedCandidate!.DriverId);

        k.Tab();
        Assert.Equal("d.phill", vm.SelectedCandidate!.DriverId);
        k.Tab(); // wraps
        Assert.Equal("d.ghill", vm.SelectedCandidate!.DriverId);
    }

    [Fact]
    public void AmbiguousPrefix_EnterWithoutTab_TakesFirstCandidate()
    {
        var vm = Vm();
        var k = new Keys(vm);

        k.Line("hi");

        Assert.Equal(new[] { "d.ghill" }, Ids(vm.Classified));
    }

    [Fact]
    public void Tab_WithNoCandidates_IsANoOp()
    {
        var vm = Vm();
        vm.CycleCandidateCommand.Execute(null);

        Assert.Equal(-1, vm.SelectedCandidateIndex);
        Assert.Null(vm.SelectedCandidate);
    }

    // ---------- 'me' = the player ----------

    [Fact]
    public void Me_AssignsThePlayer()
    {
        var vm = Vm();
        var k = new Keys(vm);

        k.Line("me");

        Assert.Equal(new[] { PlayerId }, Ids(vm.Classified));
    }

    [Fact]
    public void Me_IsReserved_NeverASurnamePrefix()
    {
        var vm = Vm();

        vm.Input = "me"; // Merzario starts with "Me" but 'me' is the player
        Assert.Equal(new[] { PlayerId }, Ids(vm.Candidates));

        vm.Input = "mer"; // Merzario stays reachable one letter later
        Assert.Equal(new[] { "d.merzario" }, Ids(vm.Candidates));
    }

    [Fact]
    public void Me_WhenPlayerAlreadyPlaced_ErrorsInsteadOfMatching()
    {
        var vm = Vm();
        var k = new Keys(vm);
        k.Line("me");

        k.Line("me");

        Assert.NotNull(vm.ErrorText);
        Assert.Single(vm.Classified);
    }

    // ---------- F8 / DNF phase ----------

    [Fact]
    public void F8_TogglesDnfPhase_RemainingAreTheCandidates()
    {
        var vm = Vm();
        var k = new Keys(vm);
        k.Line("3");

        k.F8();
        Assert.Equal(ResultEntryPhase.Dnf, vm.Phase);
        Assert.True(vm.IsDnfPhase);
        Assert.Equal(Ids(vm.Remaining), Ids(vm.Candidates)); // list order, Clark excluded
        Assert.DoesNotContain("d.clark", Ids(vm.Candidates));

        k.F8(); // toggles back
        Assert.Equal(ResultEntryPhase.Classified, vm.Phase);
        Assert.Empty(vm.Candidates);
    }

    [Fact]
    public void BulkDnf_EnterEnterEnter_MarksRemainingInListOrder()
    {
        var vm = Vm();
        var k = new Keys(vm);

        k.F8();
        k.Enter();
        k.Enter();
        k.Enter();

        Assert.Equal(new[] { "d.brabham", "d.hulme", "d.clark" }, vm.Dnfs.Select(d => d.Seat.DriverId));
        Assert.All(vm.Dnfs, d => Assert.Equal(ResultEntryViewModel.DefaultDnfReason, d.Reason));
        Assert.Equal("3/10 placed", vm.ProgressText);
    }

    [Fact]
    public void Dnf_WithTrailingReasonLetter_RecordsTheReason()
    {
        var vm = Vm();
        var k = new Keys(vm);

        k.F8();
        k.Line("hu m");
        k.Line("st a");
        k.Line("cla o");

        Assert.Equal(
            new[] { ("d.hulme", "m"), ("d.stewart", "a"), ("d.clark", "o") },
            vm.Dnfs.Select(d => (d.Seat.DriverId, d.Reason)));
    }

    // ---------- accident severity (character death & injury §3.1, Slice 2) ----------

    [Fact]
    public void PlayerAccidentDnf_RevealsSeverityPicker_DefaultsMedium_AndBuildsIntoDraft()
    {
        var vm = Vm();
        var k = new Keys(vm);

        // No accident yet → picker hidden, no severity.
        Assert.False(vm.PlayerHasAccidentDnf);
        Assert.Null(vm.PlayerAccidentSeverity);

        k.F8();
        k.Line("amo a");   // the player (Chris Amon) retires with an accident

        Assert.True(vm.PlayerHasAccidentDnf);
        Assert.Equal(AccidentSeverity.Medium, vm.PlayerAccidentSeverity); // defaulted on marking

        vm.PlayerAccidentSeverity = AccidentSeverity.Heavy;               // player bumps it up
        var draft = vm.BuildDraft();
        Assert.Equal("a", draft.DidNotFinish[PlayerId]);
        Assert.Equal(AccidentSeverity.Heavy, draft.PlayerAccidentSeverity);
    }

    [Fact]
    public void NonPlayerAccident_DoesNotRevealPicker_AndDraftSeverityIsNull()
    {
        var vm = Vm();
        var k = new Keys(vm);

        k.F8();
        k.Line("st a");    // Stewart (not the player) accidents

        Assert.False(vm.PlayerHasAccidentDnf);
        Assert.Null(vm.PlayerAccidentSeverity);
        Assert.Null(vm.BuildDraft().PlayerAccidentSeverity);
    }

    [Fact]
    public void PlayerAccident_ThenUndone_ClearsSeverityAndHidesPicker()
    {
        var vm = Vm();
        var k = new Keys(vm);

        k.F8();
        k.Line("amo a");
        Assert.Equal(AccidentSeverity.Medium, vm.PlayerAccidentSeverity);

        k.CtrlZ();         // undo the accident marking
        Assert.False(vm.PlayerHasAccidentDnf);
        Assert.Null(vm.PlayerAccidentSeverity);
    }

    [Fact]
    public void DnfReason_RequiresTheSpace()
    {
        var vm = Vm();
        var k = new Keys(vm);

        k.F8();
        k.Line("hum"); // no surname 'hum...'; must not parse as Hulme+mechanical

        Assert.Empty(vm.Dnfs);
        Assert.NotNull(vm.ErrorText);
    }

    [Fact]
    public void Dnf_PlainMatch_UsesDefaultReason()
    {
        var vm = Vm();
        var k = new Keys(vm);

        k.F8();
        k.Line("3");

        Assert.Equal(new[] { ("d.clark", "o") }, vm.Dnfs.Select(d => (d.Seat.DriverId, d.Reason)));
    }

    [Fact]
    public void Dnf_EmptyInputTab_SkipsWithinRemaining()
    {
        var vm = Vm();
        var k = new Keys(vm);

        k.F8();
        k.Tab(); // skip Brabham, select Hulme
        k.Enter();

        Assert.Equal(new[] { "d.hulme" }, vm.Dnfs.Select(d => d.Seat.DriverId));
        Assert.Contains("d.brabham", Ids(vm.Remaining));
    }

    // ---------- DSQ ('q' suffix) ----------

    [Fact]
    public void Dsq_UnplacedDriver_ByNumber()
    {
        var vm = Vm();
        var k = new Keys(vm);

        k.Line("5q");

        Assert.Equal(new[] { "d.phill" }, Ids(vm.Disqualified));
        Assert.DoesNotContain("d.phill", Ids(vm.Remaining));
        Assert.Empty(vm.Classified);
    }

    [Fact]
    public void Dsq_WithSpaceBeforeQ_AlsoWorks()
    {
        var vm = Vm();
        var k = new Keys(vm);

        k.Line("5 q");

        Assert.Equal(new[] { "d.phill" }, Ids(vm.Disqualified));
    }

    [Fact]
    public void Dsq_BySurnamePrefix()
    {
        var vm = Vm();
        var k = new Keys(vm);

        k.Line("stq");

        Assert.Equal(new[] { "d.stewart" }, Ids(vm.Disqualified));
    }

    [Fact]
    public void Dsq_PlacedDriver_RemovedFromClassification_FollowersShiftUp()
    {
        var vm = Vm();
        var k = new Keys(vm);
        k.Line("1");
        k.Line("2");
        k.Line("3");

        k.Line("1q");

        Assert.Equal(new[] { "d.hulme", "d.clark" }, Ids(vm.Classified));
        Assert.Equal(new[] { "d.brabham" }, Ids(vm.Disqualified));
        Assert.Equal("3/10 placed", vm.ProgressText); // still three resolved seats
    }

    // ---------- penalty re-position (digits after a placed match) ----------

    private static ResultEntryViewModel PlacedFive(out Keys k)
    {
        var vm = Vm();
        k = new Keys(vm);
        k.Line("1"); // P1 Brabham
        k.Line("2"); // P2 Hulme
        k.Line("3"); // P3 Clark
        k.Line("4"); // P4 G.Hill
        k.Line("5"); // P5 P.Hill
        return vm;
    }

    [Fact]
    public void Reposition_NoSpaceForm_MovesDriver_FollowersShift()
    {
        var vm = PlacedFive(out var k);

        k.Line("hu5"); // Hulme P2 -> P5

        Assert.Equal(
            new[] { "d.brabham", "d.clark", "d.ghill", "d.phill", "d.hulme" },
            Ids(vm.Classified));
    }

    [Fact]
    public void Reposition_SpaceForm_AmbiguousMatch_MovesSelectedCandidate()
    {
        var vm = PlacedFive(out var k);

        // "hi" matches both placed Hills; the first candidate (classification order) is
        // Graham Hill, so without a Tab it is Graham who moves to P1.
        k.Line("hi 1");

        Assert.Equal(
            new[] { "d.ghill", "d.brabham", "d.hulme", "d.clark", "d.phill" },
            Ids(vm.Classified));
    }

    [Fact]
    public void Reposition_PureDigitNumberWithSpace_Works()
    {
        var vm = Vm();
        var k = new Keys(vm);
        k.Line("1");
        k.Line("2");
        k.Line("3");

        k.Line("1 2"); // car 1: P1 -> P2

        Assert.Equal(new[] { "d.hulme", "d.brabham", "d.clark" }, Ids(vm.Classified));
    }

    [Fact]
    public void Reposition_PureDigitsWithoutSpace_NeverRepositions()
    {
        var vm = Vm();
        var k = new Keys(vm);
        k.Line("1");

        k.Line("19"); // no car 19: must be an error, NOT "move car 1 to P9"

        Assert.NotNull(vm.ErrorText);
        Assert.Equal(new[] { "d.brabham" }, Ids(vm.Classified));
    }

    [Fact]
    public void Reposition_BeyondClassifiedCount_ClampsToLast()
    {
        var vm = PlacedFive(out var k);

        k.Line("cla 99");

        Assert.Equal(
            new[] { "d.brabham", "d.hulme", "d.ghill", "d.phill", "d.clark" },
            Ids(vm.Classified));
    }

    [Fact]
    public void Reposition_UnplacedDriver_Errors()
    {
        var vm = Vm();
        var k = new Keys(vm);
        k.Line("1");

        k.Line("st 1"); // Stewart is not placed

        Assert.NotNull(vm.ErrorText);
        Assert.Equal(new[] { "d.brabham" }, Ids(vm.Classified));
    }

    [Fact]
    public void Reposition_PositionZero_Errors()
    {
        var vm = PlacedFive(out var k);

        k.Line("cla 0");

        Assert.NotNull(vm.ErrorText);
        Assert.Equal(
            new[] { "d.brabham", "d.hulme", "d.clark", "d.ghill", "d.phill" },
            Ids(vm.Classified));
    }

    // ---------- undo ----------

    [Fact]
    public void Undo_RestoresAnAssignment()
    {
        var vm = Vm();
        var k = new Keys(vm);
        k.Line("3");

        k.CtrlZ();

        Assert.Empty(vm.Classified);
        Assert.Contains("d.clark", Ids(vm.Remaining));
        Assert.False(vm.CanUndo);
        Assert.Equal("0/10 placed", vm.ProgressText);
    }

    [Fact]
    public void Undo_RestoresARepositionOrder()
    {
        var vm = PlacedFive(out var k);
        k.Line("hu5");

        k.CtrlZ();

        Assert.Equal(
            new[] { "d.brabham", "d.hulme", "d.clark", "d.ghill", "d.phill" },
            Ids(vm.Classified));
    }

    [Fact]
    public void Undo_RestoresADsqOfAPlacedDriver()
    {
        var vm = Vm();
        var k = new Keys(vm);
        k.Line("1");
        k.Line("2");
        k.Line("3");
        k.Line("1q");

        k.CtrlZ();

        Assert.Equal(new[] { "d.brabham", "d.hulme", "d.clark" }, Ids(vm.Classified));
        Assert.Empty(vm.Disqualified);
    }

    [Fact]
    public void Undo_TenMixedMutations_FullyUnwound()
    {
        var vm = Vm();
        var k = new Keys(vm);

        k.Line("3");      // 1: assign by number (Clark)
        k.Line("hu");     // 2: assign by prefix (Hulme)
        k.Line("me");     // 3: assign the player
        k.Line("hi");     // 4: ambiguous, first candidate (G.Hill)
        k.Line("cla4");   // 5: reposition Clark P1 -> P4
        k.Line("4q");     // 6: DSQ a placed driver (G.Hill)
        k.Line("5q");     // 7: DSQ an unplaced driver (P.Hill)
        k.F8();           //    (not a mutation)
        k.Line("st m");   // 8: DNF with reason (Stewart)
        k.Enter();        // 9: bulk DNF (first remaining: Brabham)
        k.Enter();        // 10: bulk DNF (next remaining: Merzario)

        Assert.Equal("8/10 placed", vm.ProgressText);

        for (int i = 0; i < 10; i++)
        {
            Assert.True(vm.CanUndo);
            k.CtrlZ();
        }

        Assert.Empty(vm.Classified);
        Assert.Empty(vm.Dnfs);
        Assert.Empty(vm.Disqualified);
        Assert.Equal(Ids(Grid()), Ids(vm.Remaining));
        Assert.Equal("0/10 placed", vm.ProgressText);
        Assert.False(vm.CanUndo);

        k.CtrlZ(); // 11th undo: no-op, no throw
        Assert.Equal("0/10 placed", vm.ProgressText);
    }

    // ---------- Esc, errors, empty Enter ----------

    [Fact]
    public void Esc_ClearsInputAndCandidates()
    {
        var vm = Vm();
        var k = new Keys(vm);
        k.Type("cla");
        Assert.NotEmpty(vm.Candidates);

        k.Esc();

        Assert.Equal("", vm.Input);
        Assert.Empty(vm.Candidates);
    }

    [Fact]
    public void Error_SetOnNoMatchEnter_ClearedByNextKeystroke()
    {
        var vm = Vm();
        var k = new Keys(vm);

        k.Line("zz");
        Assert.Equal("No match for 'zz'", vm.ErrorText);

        k.Type("x");
        Assert.Null(vm.ErrorText);
    }

    [Fact]
    public void EmptyEnter_InClassifiedPhase_IsANoOp()
    {
        var vm = Vm();
        vm.SubmitCommand.Execute(null);

        Assert.Null(vm.ErrorText);
        Assert.Empty(vm.Classified);
        Assert.False(vm.CanUndo);
    }

    // ---------- progress & completion ----------

    [Fact]
    public void Progress_CountsEveryResolutionKind()
    {
        var vm = Vm();
        var k = new Keys(vm);

        k.Line("1");   // classified
        k.Line("5q");  // disqualified
        k.F8();
        k.Enter();     // DNF (first remaining: Hulme)

        Assert.Equal("3/10 placed", vm.ProgressText);
        Assert.False(vm.IsComplete);
    }

    [Fact]
    public void Completion_WhenEverySeatIsClassifiedDnfOrDsq_AndDraftIsFaithful()
    {
        var vm = Vm();
        var k = new Keys(vm);

        k.Line("1");
        k.Line("2");
        k.Line("3");
        k.Line("4");
        k.Line("5");
        k.Line("6q");    // player DSQ'd, why not
        k.F8();
        k.Line("7 m");   // Merzario, mechanical
        k.Enter();       // Stewart (list order)
        k.Enter();       // Gurney
        Assert.False(vm.IsComplete);
        k.Enter();       // Siffert, the last seat

        Assert.True(vm.IsComplete);
        Assert.Equal("10/10 placed", vm.ProgressText);

        var draft = vm.BuildDraft();
        Assert.Equal(
            new[] { "d.brabham", "d.hulme", "d.clark", "d.ghill", "d.phill" },
            draft.Classified);
        Assert.Equal(new[] { PlayerId }, draft.Disqualified);
        Assert.Equal(4, draft.DidNotFinish.Count);
        Assert.Equal("m", draft.DidNotFinish["d.merzario"]);
        Assert.Equal("o", draft.DidNotFinish["d.stewart"]);
        Assert.Equal("o", draft.DidNotFinish["d.gurney"]);
        Assert.Equal("o", draft.DidNotFinish["d.siffert"]);
    }

    [Fact]
    public void BuildDraft_PositionsAreImpliedByOrder_IncludingRepositions()
    {
        var vm = PlacedFive(out var k);
        k.Line("hu5");

        var draft = vm.BuildDraft();

        Assert.Equal(
            new[] { "d.brabham", "d.clark", "d.ghill", "d.phill", "d.hulme" },
            draft.Classified);
        Assert.Empty(draft.DidNotFinish);
        Assert.Empty(draft.Disqualified);
    }

    // ---------- timer ----------

    [Fact]
    public void Timer_StartsOnFirstKeystroke_NotOnConstruction()
    {
        var clock = new FakeClock();
        var vm = Vm(clock);

        clock.Advance(TimeSpan.FromSeconds(100)); // idle time before typing must not count
        Assert.Equal(TimeSpan.Zero, vm.Elapsed);

        var k = new Keys(vm);
        k.Type("3");
        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(30), vm.Elapsed);
        Assert.Equal("0:30", vm.ElapsedText);
    }

    [Fact]
    public void Timer_DoesNotRestartOnLaterKeystrokes()
    {
        var clock = new FakeClock();
        var vm = Vm(clock);
        var k = new Keys(vm);

        k.Line("3");
        clock.Advance(TimeSpan.FromSeconds(45));
        k.Line("hu");
        clock.Advance(TimeSpan.FromSeconds(45));

        Assert.Equal(TimeSpan.FromSeconds(90), vm.Elapsed);
        Assert.Equal("1:30", vm.ElapsedText);
    }
}
