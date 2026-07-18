using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Briefing;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The briefing banner always states which of the THREE staging outcomes happened
/// (m5-fix-integration contract): no-op (installed file already matches, nothing written) /
/// staged (with backup path) / aborted. Uses a local fake session so the three flavors are
/// exercised without touching shared test fixtures.
/// </summary>
public class BriefingNoOpBannerTests
{
    private sealed class StubSession : ICareerSession
    {
        public StageOutcome? NextOutcome { get; set; }

        public CareerSummary Summary => throw new NotSupportedException();

        public SeasonPack Pack => throw new NotSupportedException();

        public BriefingModel? CurrentBriefing() => null;

        public StageOutcome StageCurrentGrid() =>
            NextOutcome ?? throw new InvalidOperationException("no outcome queued");

        public IReadOnlyList<GridSeat> CurrentGrid() => [];

        public ConfirmModel Preview(ResultDraft draft) => throw new NotSupportedException();

        public void Apply(ResultDraft draft) => throw new NotSupportedException();

        public StandingsSnapshot? CurrentStandings() => null;

        public IReadOnlyList<StandingsSnapshot> AllSnapshots() => [];

        public int? CurrentSliderRecommendation() => null;

        public SeasonReviewModel? SeasonReview() => null;

        public void AcceptOffer(string teamId) => throw new NotSupportedException();
    }

    private const string InstalledPath = @"C:\fake\UserData\CustomAIDrivers\F-Vintage_Gen1.xml";

    [Fact]
    public void Banner_NoOp_SaysAlreadyMatchesNothingWritten_AndStillWatchesTheFile()
    {
        var session = new StubSession
        {
            NextOutcome = new StageOutcome
            {
                Success = true,
                NoOpAlreadyMatches = true,
                WrittenPath = InstalledPath,
                BackupPath = null,
                Messages = ["Installed F-Vintage_Gen1.xml already matches this round's grid (2 drivers), nothing written, your file stays in place."],
            },
        };
        var watcher = new FakeFileWatcher();
        var vm = new BriefingViewModel(session, watcher);

        vm.StageGridCommand.Execute(null);

        Assert.True(vm.StageSucceeded);
        Assert.Contains("already set up", vm.StageBanner);
        Assert.Contains("nothing to change", vm.StageBanner);
        Assert.Contains("✔", vm.StageBanner);
        // Distinguishable from the staged banner: no backup wording.
        Assert.DoesNotContain("backed up", vm.StageBanner);
        // The installed file that satisfies the round is still watched for outside edits.
        Assert.Equal(InstalledPath, watcher.Watching);
    }

    [Fact]
    public void Banner_Staged_NamesTheBackupPath()
    {
        var session = new StubSession
        {
            NextOutcome = new StageOutcome
            {
                Success = true,
                WrittenPath = InstalledPath,
                BackupPath = @"C:\fake\UserData\CustomAIDrivers\_companion-backups\F-Vintage_Gen1.20260702T120000Z.xml",
                Messages = ["Staged F-Vintage_Gen1.xml"],
            },
        };
        var vm = new BriefingViewModel(session, new FakeFileWatcher());

        vm.StageGridCommand.Execute(null);

        Assert.True(vm.StageSucceeded);
        Assert.StartsWith("✔ AMS2 is set up", vm.StageBanner);
        Assert.Contains("backed up to", vm.StageBanner);
        Assert.DoesNotContain("already matches", vm.StageBanner);
    }

    [Fact]
    public void Banner_Aborted_SaysStagingFailed()
    {
        var session = new StubSession
        {
            NextOutcome = new StageOutcome
            {
                Success = false,
                Messages = ["Error: livery 'X' not installed", "Staging aborted, fix the preflight errors above and stage again."],
            },
        };
        var watcher = new FakeFileWatcher();
        var vm = new BriefingViewModel(session, watcher);

        vm.StageGridCommand.Execute(null);

        Assert.False(vm.StageSucceeded);
        Assert.StartsWith("Couldn't set up", vm.StageBanner);
        Assert.Contains("aborted", vm.StageBanner);
        Assert.Null(watcher.Watching);
    }
}
