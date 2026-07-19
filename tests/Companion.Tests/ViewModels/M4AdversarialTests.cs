using Companion.Core.Grid;
using Companion.ViewModels.ResultEntry;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Adversarial M4 verification (docs/dev/app-shell.md): spec-rule edge cases that the main
/// grammar and session suites did not pin down, 2-letters-ambiguous/3-letters-unique
/// surname collisions, bulk DNF with nothing left to retire, and Preview purity (calling it
/// twice must be identical and commit nothing) plus full standings equality across a
/// close/reopen of the career file.
/// </summary>
public sealed class M4AdversarialTests : IDisposable
{
    // ---------- grammar: surname collision resolved one letter later ----------

    private const string PlayerId = "d.player";

    /// <summary>Stewart vs Stuck: "st" is ambiguous, "ste"/"stu" are unique.</summary>
    private static ResultEntryViewModel CollisionVm() => new(
        [
            ResultEntryViewModelTests.Seat("d.stewart", "Jackie Stewart", "1"),
            ResultEntryViewModelTests.Seat("d.stuck", "Hans Stuck", "2"),
            ResultEntryViewModelTests.Seat(PlayerId, "Test Player", "3", isPlayer: true),
        ],
        PlayerId);

    [Fact]
    public void SurnameCollision_TwoLettersAmbiguous_ThirdLetterUnique()
    {
        var vm = CollisionVm();

        vm.Input = "st";
        Assert.Equal(new[] { "d.stewart", "d.stuck" }, vm.Candidates.Select(s => s.DriverId));
        Assert.True(vm.IsAmbiguous);
        Assert.Equal("d.stewart", vm.SelectedCandidate!.DriverId); // first in grid order

        vm.Input = "stu"; // one more letter disambiguates
        Assert.Equal(new[] { "d.stuck" }, vm.Candidates.Select(s => s.DriverId));
        Assert.False(vm.IsAmbiguous);

        vm.SubmitCommand.Execute(null);
        Assert.Equal(new[] { "d.stuck" }, vm.Classified.Select(s => s.DriverId));

        vm.Input = "ste"; // and the other branch stays reachable
        Assert.Equal(new[] { "d.stewart" }, vm.Candidates.Select(s => s.DriverId));
        vm.SubmitCommand.Execute(null);
        Assert.Equal(new[] { "d.stuck", "d.stewart" }, vm.Classified.Select(s => s.DriverId));
    }

    [Fact]
    public void SurnameCollision_AmbiguousEnter_CommitsTheHighlightedCandidateAfterTab()
    {
        var vm = CollisionVm();

        vm.Input = "st";
        vm.CycleCandidateCommand.Execute(null); // Tab: Stewart -> Stuck
        vm.SubmitCommand.Execute(null);

        Assert.Equal(new[] { "d.stuck" }, vm.Classified.Select(s => s.DriverId));

        vm.Input = "st"; // only Stewart is left unplaced, no longer ambiguous
        Assert.Equal(new[] { "d.stewart" }, vm.Candidates.Select(s => s.DriverId));
        Assert.False(vm.IsAmbiguous);
    }

    // ---------- grammar: bulk DNF when zero drivers remain ----------

    [Fact]
    public void BulkDnf_WhenZeroRemain_EnterIsASafeNoOp()
    {
        var vm = CollisionVm();

        // Resolve every seat: two classified, one DSQ.
        vm.Input = "1";
        vm.SubmitCommand.Execute(null);
        vm.Input = "2";
        vm.SubmitCommand.Execute(null);
        vm.Input = "me q";
        vm.SubmitCommand.Execute(null);
        Assert.True(vm.IsComplete);
        Assert.Empty(vm.Remaining);

        vm.ToggleDnfPhaseCommand.Execute(null); // F8 with nothing left
        Assert.True(vm.IsDnfPhase);
        Assert.Empty(vm.Candidates);
        Assert.Null(vm.SelectedCandidate);

        int undoDepthBefore = 3;
        vm.SubmitCommand.Execute(null); // ↵
        vm.SubmitCommand.Execute(null); // ↵↵, must not throw, mutate, or push undo
        Assert.Null(vm.ErrorText);
        Assert.Empty(vm.Dnfs);
        Assert.Equal("3/3 placed", vm.ProgressText);
        Assert.True(vm.IsComplete);

        // The undo stack gained nothing from the empty Enters.
        for (int i = 0; i < undoDepthBefore; i++)
        {
            Assert.True(vm.CanUndo);
            vm.UndoCommand.Execute(null);
        }
        Assert.False(vm.CanUndo);
    }

    // ---------- grammar: placed -> DSQ -> undo -> DSQ again stays coherent ----------

    [Fact]
    public void PlacedDsqUndoDsqAgain_ClassificationStaysCoherent()
    {
        var vm = CollisionVm();
        vm.Input = "1";
        vm.SubmitCommand.Execute(null); // P1 Stewart
        vm.Input = "2";
        vm.SubmitCommand.Execute(null); // P2 Stuck

        vm.Input = "1q"; // DSQ the placed leader
        vm.SubmitCommand.Execute(null);
        Assert.Equal(new[] { "d.stuck" }, vm.Classified.Select(s => s.DriverId));
        Assert.Equal(new[] { "d.stewart" }, vm.Disqualified.Select(s => s.DriverId));

        vm.UndoCommand.Execute(null); // back to P1 Stewart, P2 Stuck
        Assert.Equal(new[] { "d.stewart", "d.stuck" }, vm.Classified.Select(s => s.DriverId));
        Assert.Empty(vm.Disqualified);

        vm.Input = "stq"; // DSQ him again by surname this time
        vm.SubmitCommand.Execute(null);
        Assert.Equal(new[] { "d.stuck" }, vm.Classified.Select(s => s.DriverId));
        Assert.Equal(new[] { "d.stewart" }, vm.Disqualified.Select(s => s.DriverId));
        Assert.Equal("2/3 placed", vm.ProgressText);
    }

    // ---------- session: Preview purity + persistence round-trip equality ----------

    private readonly string _root = Directory.CreateTempSubdirectory("companion-m4-adv-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // SQLite WAL sidecars can outlive the connection briefly on Windows.
        }
    }

    private CareerCreationRequest Request() => new()
    {
        PackDirectory = ViewModelTestData.RealPackDirectory,
        CareerFilePath = Path.Combine(_root, "career.ams2career"),
        CareerName = "Adversarial 1967",
        MasterSeed = 7,
        PlayerLiveryName = "Brabham-Repco #2 D. Hulme",
    };

    private static ResultDraft FullDraft(IReadOnlyList<GridSeat> grid) => new()
    {
        Classified = grid.Select(s => s.DriverId).Take(grid.Count - 2).ToList(),
        DidNotFinish = new Dictionary<string, string>
        {
            [grid[^2].DriverId] = "m",
            [grid[^1].DriverId] = "a",
        },
        Disqualified = [],
    };

    [Fact]
    public void Preview_CalledTwice_IsIdenticalAndCommitsNothing()
    {
        var environment = ViewModelTestData.Environment(Path.Combine(_root, "docs"));
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        var draft = FullDraft(session.CurrentGrid());

        var first = session.Preview(draft);
        var second = session.Preview(draft);

        Assert.Equal(first.Headline, second.Headline);
        Assert.Equal(first.RoundPoints, second.RoundPoints);
        Assert.Equal(first.Movements, second.Movements);

        // Nothing was committed by either call.
        Assert.Null(session.CurrentStandings());
        Assert.Empty(session.AllSnapshots());
        Assert.Equal(1, session.Summary.CurrentRound);
        Assert.Null(session.Summary.PlayerPosition);
    }

    [Fact]
    public void ApplyThenReopenFromDisk_StandingsAreIdentical()
    {
        var environment = ViewModelTestData.Environment(Path.Combine(_root, "docs"));
        var request = Request();

        List<(string DriverId, int? Position, string Gross, string Counted)> before;
        List<(string ConstructorId, int? Position, string Counted)> constructorsBefore;
        using (var session = CareerSessionService.CreateCareer(request, environment))
        {
            session.Apply(FullDraft(session.CurrentGrid()));
            var standings = session.CurrentStandings();
            Assert.NotNull(standings);
            before = standings.Drivers
                .Select(d => (d.DriverId, d.Position, d.GrossPoints.ToString(), d.CountedPoints.ToString()))
                .ToList();
            constructorsBefore = (standings.Constructors ?? [])
                .Select(c => (c.ConstructorId, c.Position, c.CountedPoints.ToString()))
                .ToList();
            Assert.NotEmpty(constructorsBefore); // 1967 has a constructors championship
        }

        using var reopened = CareerSessionService.OpenCareer(
            request.CareerFilePath, ViewModelTestData.Environment(Path.Combine(_root, "docs")));

        var after = reopened.CurrentStandings();
        Assert.NotNull(after);
        Assert.Equal(before, after.Drivers
            .Select(d => (d.DriverId, d.Position, d.GrossPoints.ToString(), d.CountedPoints.ToString()))
            .ToList());
        Assert.Equal(constructorsBefore, (after.Constructors ?? [])
            .Select(c => (c.ConstructorId, c.Position, c.CountedPoints.ToString()))
            .ToList());
        Assert.Equal(2, reopened.Summary.CurrentRound);
    }
}
