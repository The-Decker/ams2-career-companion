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

    // ---------- copy (ux-round correction: per-row copy is gone; one summary copy remains) ----------

    [Fact]
    public void CopySummary_RaisesCopyRequestedWithTheComposedChecklist()
    {
        var vm = new BriefingViewModel(SessionWithRealRound3());
        var copied = new List<string>();
        vm.CopyRequested += (_, text) => copied.Add(text);

        vm.CopySummaryCommand.Execute(null);

        string text = Assert.Single(copied);
        Assert.Contains("Track: Spielberg_Vintage", text);
        Assert.Contains("Laps: 64", text);
    }

    // ---------- Setup Gamble: the pre-race called shot (4b) ----------

    [Fact]
    public void Gamble_OffersACallAgainstTheExpectedFinish_AndPreviewsTheStake()
    {
        var session = SessionWithRealRound3();
        session.ExpectedFinish = 10;
        var vm = new BriefingViewModel(session);

        Assert.True(vm.CanGamble);
        Assert.False(vm.HasCalledShot);
        Assert.Contains("expects you around P10", vm.CalledShotSummary);

        // "Bolder" from no call starts one place better than expected (P9) — the base stake.
        vm.CallBolderCommand.Execute(null);
        Assert.Equal(9, vm.CalledShot);
        Assert.True(vm.HasCalledShot);
        Assert.Contains("Called P9", vm.CalledShotSummary);
        Assert.Contains("staking 3", vm.CalledShotSummary);

        // Bolder again → P8, a bigger stake (4).
        vm.CallBolderCommand.Execute(null);
        Assert.Equal(8, vm.CalledShot);
        Assert.Contains("staking 4", vm.CalledShotSummary);

        // Withdraw the bet.
        vm.ClearCallCommand.Execute(null);
        Assert.Null(vm.CalledShot);
        Assert.False(vm.HasCalledShot);
    }

    [Fact]
    public void Gamble_ACallNoBolderThanExpected_IsFlaggedAsNotAGamble()
    {
        var session = SessionWithRealRound3();
        session.ExpectedFinish = 5;
        var vm = new BriefingViewModel(session) { CalledShot = 7 }; // behind the expected finish

        Assert.False(Companion.Core.Career.CalledShotMath.IsGamble(7, 5));
        Assert.Contains("isn't a gamble", vm.CalledShotSummary);
    }

    [Fact]
    public void Gamble_HiddenWhenThePlayerHasNoSeat_OrCannotCallBolder()
    {
        // No expected finish (no seat this round) → no gamble offered.
        var noSeat = SessionWithRealRound3();
        noSeat.ExpectedFinish = null;
        Assert.False(new BriefingViewModel(noSeat).CanGamble);

        // Already expected to win (P1) → nothing bolder to call.
        var poleFavourite = SessionWithRealRound3();
        poleFavourite.ExpectedFinish = 1;
        Assert.False(new BriefingViewModel(poleFavourite).CanGamble);
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
        Assert.Contains("nothing was backed up", vm.StageBanner);
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
        Assert.StartsWith("Couldn't set up", vm.StageBanner);
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
