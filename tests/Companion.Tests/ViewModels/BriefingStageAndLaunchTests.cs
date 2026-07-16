using Companion.Ams2;
using Companion.ViewModels.Briefing;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

public sealed class BriefingStageAndLaunchTests
{
    private const string WrittenPath = @"C:\ams2\UserData\CustomAIDrivers\F-Vintage_Gen1.xml";
    private const string BackupPath =
        @"C:\ams2\UserData\CustomAIDrivers\_companion-backups\F-Vintage_Gen1.20260715T120000Z.xml";

    [Fact]
    public void InitialState_IsHonestlyReadyForALaunchCapableLiveRound()
    {
        var vm = new BriefingViewModel(new StageLaunchSession(), new RecordingWatcher());

        Assert.Equal(StageLaunchState.Ready, vm.StageLaunchState);
        Assert.Equal("Ready", vm.StageLaunchStatus);
        Assert.Contains("backup-first", vm.StageLaunchMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.StageLaunchChoiceRequired);
        Assert.True(vm.StageAndLaunchCommand.CanExecute(null));
        Assert.False(vm.ReapplyAndLaunchCommand.CanExecute(null));
        Assert.False(vm.CancelReapplyCommand.CanExecute(null));
    }

    [Fact]
    public void StageAndLaunch_PausesWatcher_ThenRestartsItBeforeLaunching_EvenForNoOp()
    {
        var session = new StageLaunchSession();
        session.StageOutcomes.Enqueue(Success());
        session.StageOutcomes.Enqueue(Success(noOp: true));
        var watcher = new RecordingWatcher();
        var vm = new BriefingViewModel(session, watcher);

        // Establish an existing watch through the preserved Stage-only control.
        vm.StageGridCommand.Execute(null);
        Assert.Equal(WrittenPath, watcher.Watching);

        session.OnStage = _ => Assert.Null(watcher.Watching);
        session.OnLaunch = () => Assert.Equal(WrittenPath, watcher.Watching);

        vm.StageAndLaunchCommand.Execute(null);

        Assert.Equal([false, false], session.StageForceFlags);
        Assert.Equal(1, session.LaunchCalls);
        Assert.Equal(StageLaunchState.Ready, vm.StageLaunchState);
        Assert.Equal("Ready", vm.StageLaunchStatus);
        Assert.Contains("Launch request sent", vm.StageLaunchMessage);
        Assert.True(vm.LastStageOutcome!.NoOpAlreadyMatches);
        Assert.Equal(WrittenPath, watcher.Watching);
        Assert.True(watcher.StopCalls >= 2);
    }

    [Fact]
    public void StageAndLaunch_WhenStagingFails_DoesNotLaunch()
    {
        var session = new StageLaunchSession();
        session.StageOutcomes.Enqueue(Failed("Staging aborted — fix the preflight errors above and stage again."));
        var watcher = new RecordingWatcher();
        var vm = new BriefingViewModel(session, watcher);

        vm.StageAndLaunchCommand.Execute(null);

        Assert.Equal(0, session.LaunchCalls);
        Assert.Equal([false], session.StageForceFlags);
        Assert.Equal(StageLaunchState.StagingFailed, vm.StageLaunchState);
        Assert.Equal("Staging Failed", vm.StageLaunchStatus);
        Assert.Contains("Staging aborted", vm.StageLaunchMessage);
        Assert.Null(watcher.Watching);
    }

    [Fact]
    public void StageAndLaunch_ForceGate_RequiresExplicitChoice_AndCancelDoesNothing()
    {
        var session = new StageLaunchSession();
        session.StageOutcomes.Enqueue(new StageOutcome
        {
            Success = false,
            BlockedByForceGate = true,
            Messages = ["Community file requires force."],
        });
        var vm = new BriefingViewModel(session, new RecordingWatcher());

        vm.StageAndLaunchCommand.Execute(null);

        Assert.Equal(StageLaunchState.ReapplyRequired, vm.StageLaunchState);
        Assert.Equal("Reapply Required", vm.StageLaunchStatus);
        Assert.True(vm.StageLaunchChoiceRequired);
        Assert.Equal(0, session.LaunchCalls);
        Assert.Equal([false], session.StageForceFlags);

        vm.CancelReapplyCommand.Execute(null);

        Assert.Equal(StageLaunchState.Ready, vm.StageLaunchState);
        Assert.False(vm.StageLaunchChoiceRequired);
        Assert.Equal(0, session.LaunchCalls);
        Assert.Equal([false], session.StageForceFlags);
    }

    [Fact]
    public void ExternalChange_PrimaryNeverReapplies_CancelKeepsWarning_ExplicitReapplyStagesAndLaunches()
    {
        var session = new StageLaunchSession();
        session.StageOutcomes.Enqueue(Success());
        var watcher = new RecordingWatcher();
        var vm = new BriefingViewModel(session, watcher);

        vm.StageAndLaunchCommand.Execute(null);
        Assert.Equal(1, session.LaunchCalls);
        watcher.RaiseChanged(WrittenPath);

        Assert.Equal(StageLaunchState.ChangedExternally, vm.StageLaunchState);
        Assert.Equal("Changed Externally", vm.StageLaunchStatus);
        Assert.False(vm.StageLaunchChoiceRequired);
        Assert.Contains("Select Stage & Launch to review a backup-first reapply", vm.StageLaunchMessage);
        Assert.DoesNotContain("Choose Reapply & Launch", vm.StageLaunchMessage);

        vm.StageAndLaunchCommand.Execute(null);

        Assert.Equal(StageLaunchState.ReapplyRequired, vm.StageLaunchState);
        Assert.True(vm.StageLaunchChoiceRequired);
        Assert.Equal([false], session.StageForceFlags);
        Assert.Equal(1, session.LaunchCalls);

        vm.CancelReapplyCommand.Execute(null);

        Assert.Equal(StageLaunchState.ChangedExternally, vm.StageLaunchState);
        Assert.False(vm.StageLaunchChoiceRequired);
        Assert.Contains("Select Stage & Launch to review a backup-first reapply", vm.StageLaunchMessage);
        Assert.DoesNotContain("Choose Reapply & Launch", vm.StageLaunchMessage);
        Assert.Equal([false], session.StageForceFlags);
        Assert.Equal(1, session.LaunchCalls);

        session.StageOutcomes.Enqueue(Success(backupPath: BackupPath));
        session.OnStage = force =>
        {
            Assert.True(force);
            Assert.Null(watcher.Watching);
        };
        session.OnLaunch = () => Assert.Equal(WrittenPath, watcher.Watching);

        vm.ReapplyAndLaunchCommand.Execute(null);

        Assert.Equal([false, true], session.StageForceFlags);
        Assert.Equal(2, session.LaunchCalls);
        Assert.Equal(StageLaunchState.Ready, vm.StageLaunchState);
        Assert.False(vm.StageLaunchChoiceRequired);
        Assert.False(vm.StagedFileTouchedExternally);
        Assert.Equal(BackupPath, vm.LastStageOutcome!.BackupPath);
        Assert.Equal(WrittenPath, watcher.Watching);
    }

    [Fact]
    public void ReapplyFailure_DoesNotLaunchAgain()
    {
        var session = new StageLaunchSession();
        session.StageOutcomes.Enqueue(Success());
        var watcher = new RecordingWatcher();
        var vm = new BriefingViewModel(session, watcher);
        vm.StageAndLaunchCommand.Execute(null);
        watcher.RaiseChanged(WrittenPath);
        session.StageOutcomes.Enqueue(Failed("Access denied while staging."));

        vm.ReapplyAndLaunchCommand.Execute(null);

        Assert.Equal([false, true], session.StageForceFlags);
        Assert.Equal(1, session.LaunchCalls);
        Assert.Equal(StageLaunchState.StagingFailed, vm.StageLaunchState);
        Assert.Contains("Access denied", vm.StageLaunchMessage);
        Assert.Null(watcher.Watching);
    }

    [Fact]
    public void LaunchFailure_KeepsSuccessfulStageAndWatcher_WithActionableMessage()
    {
        var session = new StageLaunchSession();
        StageOutcome staged = Success(backupPath: BackupPath);
        session.StageOutcomes.Enqueue(staged);
        session.LaunchOutcomes.Enqueue(Ams2LaunchResult.Failed(
            "Steam could not launch AMS2. Make sure Steam is installed and running, then try again."));
        var watcher = new RecordingWatcher();
        var vm = new BriefingViewModel(session, watcher);

        vm.StageAndLaunchCommand.Execute(null);

        Assert.Equal(1, session.LaunchCalls);
        Assert.Equal(StageLaunchState.LaunchFailed, vm.StageLaunchState);
        Assert.Equal("Launch Failed", vm.StageLaunchStatus);
        Assert.Contains("Steam is installed and running", vm.StageLaunchMessage);
        Assert.Contains("has been kept", vm.StageLaunchMessage);
        Assert.Same(staged, vm.LastStageOutcome);
        Assert.True(vm.StageSucceeded);
        Assert.Equal(WrittenPath, watcher.Watching);
    }

    private static StageOutcome Success(bool noOp = false, string? backupPath = null) => new()
    {
        Success = true,
        WrittenPath = WrittenPath,
        BackupPath = backupPath,
        NoOpAlreadyMatches = noOp,
        Messages = [noOp ? "Already matches." : "Staged."],
    };

    private static StageOutcome Failed(string message) => new()
    {
        Success = false,
        Messages = [message],
    };

    private sealed class StageLaunchSession : ICareerSession, IForceStaging, IAms2GameLaunch
    {
        private readonly FakeCareerSession _inner = CreateInner();

        public Queue<StageOutcome> StageOutcomes { get; } = new();
        public Queue<Ams2LaunchResult> LaunchOutcomes { get; } = new();
        public List<bool> StageForceFlags { get; } = [];
        public int LaunchCalls { get; private set; }
        public Action<bool>? OnStage { get; set; }
        public Action? OnLaunch { get; set; }

        public CareerSummary Summary => _inner.Summary;
        public Companion.Core.Packs.SeasonPack Pack => _inner.Pack;
        public BriefingModel? CurrentBriefing() => _inner.CurrentBriefing();
        public IReadOnlyList<Companion.Core.Grid.GridSeat> CurrentGrid() => _inner.CurrentGrid();
        public ConfirmModel Preview(ResultDraft draft) => _inner.Preview(draft);
        public void Apply(ResultDraft draft) => _inner.Apply(draft);
        public Companion.Core.Scoring.StandingsSnapshot? CurrentStandings() => _inner.CurrentStandings();
        public IReadOnlyList<Companion.Core.Scoring.StandingsSnapshot> AllSnapshots() => _inner.AllSnapshots();
        public int? CurrentSliderRecommendation() => _inner.CurrentSliderRecommendation();
        public SeasonReviewModel? SeasonReview() => _inner.SeasonReview();
        public void AcceptOffer(string teamId) => _inner.AcceptOffer(teamId);

        public StageOutcome StageCurrentGrid() => StageCurrentGrid(force: false);

        public StageOutcome StageCurrentGrid(bool force)
        {
            StageForceFlags.Add(force);
            OnStage?.Invoke(force);
            return StageOutcomes.Count > 0
                ? StageOutcomes.Dequeue()
                : Failed("No staging outcome queued.");
        }

        public Ams2LaunchResult LaunchAms2()
        {
            LaunchCalls++;
            OnLaunch?.Invoke();
            return LaunchOutcomes.Count > 0
                ? LaunchOutcomes.Dequeue()
                : Ams2LaunchResult.Succeeded();
        }

        private static FakeCareerSession CreateInner()
        {
            var pack = ViewModelTestData.RealPack();
            var round = pack.Season.Rounds.Single(r => r.Round == 3);
            return new FakeCareerSession
            {
                Briefing = BriefingComposer.Compose(pack, round, ViewModelTestData.RealLibrary.Value),
            };
        }
    }

    private sealed class RecordingWatcher : IFileWatcher
    {
        public string? Watching { get; private set; }
        public int WatchCalls { get; private set; }
        public int StopCalls { get; private set; }

        public event EventHandler<string>? Changed;

        public void Watch(string filePath)
        {
            WatchCalls++;
            Watching = filePath;
        }

        public void Stop()
        {
            StopCalls++;
            Watching = null;
        }

        public void RaiseChanged(string path) => Changed?.Invoke(this, path);
    }
}
