using Companion.ViewModels.Briefing;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// BriefingViewModel behavior over a fake session: settings exposure, the clipboard-free
/// copy event, the stage banner (success with backup path / failure), and the injected
/// file-watcher driving the "modified outside the app" flag.
/// </summary>
public class BriefingViewModelTests
{
    private const string WrittenPath = @"C:\ams2\UserData\CustomAIDrivers\F-Vintage_Gen1.xml";
    private const string BackupPath =
        @"C:\ams2\UserData\CustomAIDrivers\_companion-backups\F-Vintage_Gen1.20260702T120000Z.xml";

    private static FakeCareerSession SessionWithRealRound3()
    {
        var pack = ViewModelTestData.RealPack();
        var round = pack.Season.Rounds.Single(r => r.Round == 3);
        return new FakeCareerSession
        {
            Briefing = BriefingComposer.Compose(pack, round, ViewModelTestData.RealLibrary.Value),
        };
    }

    private static StageOutcome SuccessOutcome(string? backupPath = null) => new()
    {
        Success = true,
        WrittenPath = WrittenPath,
        BackupPath = backupPath,
        Messages = ["Staged F-Vintage_Gen1.xml (17 drivers)."],
    };

    // ---------- content ----------

    [Fact]
    public void Load_ExposesTitleVenueAndSettings()
    {
        var vm = new BriefingViewModel(SessionWithRealRound3());

        Assert.False(vm.SeasonComplete);
        Assert.Equal("Dutch Grand Prix — placeholder: Spielberg_Vintage", vm.Title);
        Assert.Equal("Circuit Park Zandvoort", vm.VenueDisplayName);
        Assert.True(vm.IsPlaceholder);
        Assert.Equal("Track", vm.Settings[0].Label);
        Assert.Equal("Spielberg_Vintage", vm.Settings[0].Value);
        Assert.Contains("377.4 km", vm.SetupNotes);
    }

    [Fact]
    public void Load_SeasonComplete_HasNoBriefing()
    {
        var vm = new BriefingViewModel(new FakeCareerSession { Briefing = null });

        Assert.True(vm.SeasonComplete);
        Assert.Empty(vm.Settings);
        Assert.Equal("", vm.Title);
    }

    // ---------- copy ----------

    [Fact]
    public void Copy_RaisesCopyRequestedWithTheExactValue()
    {
        var vm = new BriefingViewModel(SessionWithRealRound3());
        var copied = new List<string>();
        vm.CopyRequested += (_, text) => copied.Add(text);

        vm.CopyCommand.Execute(vm.Settings[0]); // Track
        vm.CopyCommand.Execute(vm.Settings[2]); // Laps

        Assert.Equal(["Spielberg_Vintage", "64"], copied);
    }

    // ---------- staging banner ----------

    [Fact]
    public void StageGrid_Success_ShowsBackupPathAndWatchesTheFile()
    {
        var session = SessionWithRealRound3();
        session.StageOutcomes.Enqueue(SuccessOutcome(BackupPath));
        var watcher = new FakeFileWatcher();
        var vm = new BriefingViewModel(session, watcher);

        vm.StageGridCommand.Execute(null);

        Assert.True(vm.StageSucceeded);
        Assert.Contains("F-Vintage_Gen1.xml", vm.StageBanner);
        Assert.Contains(BackupPath, vm.StageBanner); // the banner shows the backup path
        Assert.Equal(WrittenPath, watcher.Watching);
        Assert.False(vm.StagedFileTouchedExternally);
    }

    [Fact]
    public void StageGrid_FirstStage_NoBackup_SaysSo()
    {
        var session = SessionWithRealRound3();
        session.StageOutcomes.Enqueue(SuccessOutcome(backupPath: null));
        var vm = new BriefingViewModel(session);

        vm.StageGridCommand.Execute(null);

        Assert.True(vm.StageSucceeded);
        Assert.Contains("nothing to back up", vm.StageBanner);
    }

    [Fact]
    public void StageGrid_Failure_ShowsTheLastMessageAndStopsWatching()
    {
        var session = SessionWithRealRound3();
        session.StageOutcomes.Enqueue(new StageOutcome
        {
            Success = false,
            Messages =
            [
                "Error: Vehicle class 'F-Vintage_Gen1' is not in the content library.",
                "Staging aborted — fix the preflight errors above and stage again.",
            ],
        });
        var watcher = new FakeFileWatcher();
        var vm = new BriefingViewModel(session, watcher);

        vm.StageGridCommand.Execute(null);

        Assert.False(vm.StageSucceeded);
        Assert.StartsWith("Staging failed", vm.StageBanner);
        Assert.Contains("Staging aborted", vm.StageBanner);
        Assert.Null(watcher.Watching);
        Assert.Equal(2, vm.StageMessages.Count);
    }

    // ---------- external-modification flag ----------

    [Fact]
    public void WatcherChange_OnTheStagedFile_FlagsExternalModification()
    {
        var session = SessionWithRealRound3();
        session.StageOutcomes.Enqueue(SuccessOutcome(BackupPath));
        var watcher = new FakeFileWatcher();
        var vm = new BriefingViewModel(session, watcher);
        vm.StageGridCommand.Execute(null);

        watcher.RaiseChanged(WrittenPath);

        Assert.True(vm.StagedFileTouchedExternally);
    }

    [Fact]
    public void WatcherChange_OnSomeOtherFile_DoesNotFlag()
    {
        var session = SessionWithRealRound3();
        session.StageOutcomes.Enqueue(SuccessOutcome(BackupPath));
        var watcher = new FakeFileWatcher();
        var vm = new BriefingViewModel(session, watcher);
        vm.StageGridCommand.Execute(null);

        watcher.RaiseChanged(@"C:\somewhere\else.xml");

        Assert.False(vm.StagedFileTouchedExternally);
    }

    [Fact]
    public void Restaging_ResetsTheExternalModificationFlag()
    {
        var session = SessionWithRealRound3();
        session.StageOutcomes.Enqueue(SuccessOutcome(BackupPath));
        session.StageOutcomes.Enqueue(SuccessOutcome(BackupPath));
        var watcher = new FakeFileWatcher();
        var vm = new BriefingViewModel(session, watcher);

        vm.StageGridCommand.Execute(null);
        watcher.RaiseChanged(WrittenPath);
        Assert.True(vm.StagedFileTouchedExternally);

        vm.StageGridCommand.Execute(null); // re-stage clears the warning

        Assert.False(vm.StagedFileTouchedExternally);
    }
}
