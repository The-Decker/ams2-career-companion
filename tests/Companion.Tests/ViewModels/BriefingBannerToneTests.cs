using Companion.ViewModels.Briefing;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Staging banner tones (fix round): the community-file force gate is an EXPECTED choice —
/// the banner turns informational (amber), keeps the Stage-anyway flow reachable, and never
/// reads as a failure. Red stays for real failures (preflight errors, no install); green for
/// staged and for the no-op already-matches state. Plus the collapsed-by-default details
/// behind the aggregate messages.
/// </summary>
public class BriefingBannerToneTests
{
    private const string InstalledPath = @"C:\fake\UserData\CustomAIDrivers\F-Vintage_Gen1.xml";

    private const string GateMessage =
        "Your installed F-Vintage_Gen1.xml differs from this round's grid (community NAMeS " +
        "file). 'Overwrite anyway' takes a timestamped backup first.";

    /// <summary>A force-capable session (like the real <see cref="CareerSessionService"/>):
    /// queued outcomes serve normal and forced staging alike, recording the force flag.</summary>
    private sealed class ForceCapableSession : Companion.ViewModels.Services.ICareerSession, IForceStaging
    {
        private readonly FakeCareerSession _inner = new();

        public Queue<StageOutcome> StageOutcomes => _inner.StageOutcomes;

        public List<bool> ForceFlags { get; } = [];

        public StageOutcome StageCurrentGrid(bool force)
        {
            ForceFlags.Add(force);
            return _inner.StageCurrentGrid();
        }

        public CareerSummary Summary => _inner.Summary;
        public Companion.Core.Packs.SeasonPack Pack => _inner.Pack;
        public BriefingModel? CurrentBriefing() => _inner.CurrentBriefing();
        public StageOutcome StageCurrentGrid() => StageCurrentGrid(force: false);
        public IReadOnlyList<Companion.Core.Grid.GridSeat> CurrentGrid() => _inner.CurrentGrid();
        public ConfirmModel Preview(ResultDraft draft) => _inner.Preview(draft);
        public void Apply(ResultDraft draft) => _inner.Apply(draft);
        public Companion.Core.Scoring.StandingsSnapshot? CurrentStandings() => _inner.CurrentStandings();
        public IReadOnlyList<Companion.Core.Scoring.StandingsSnapshot> AllSnapshots() => _inner.AllSnapshots();
        public int? CurrentSliderRecommendation() => _inner.CurrentSliderRecommendation();
        public SeasonReviewModel? SeasonReview() => _inner.SeasonReview();
        public void AcceptOffer(string teamId) => _inner.AcceptOffer(teamId);
    }

    private static (BriefingViewModel Vm, FakeFileWatcher Watcher) VmWith(StageOutcome outcome)
    {
        var session = new FakeCareerSession();
        session.StageOutcomes.Enqueue(outcome);
        var watcher = new FakeFileWatcher();
        var vm = new BriefingViewModel(session, watcher);
        vm.StageGridCommand.Execute(null);
        return (vm, watcher);
    }

    [Fact]
    public void BeforeAnyAttempt_ToneIsNone()
    {
        var vm = new BriefingViewModel(new FakeCareerSession());
        Assert.Equal(StageBannerTone.None, vm.BannerTone);
    }

    [Fact]
    public void ForceGate_IsInformational_NotAFailure_AndStageAnywayStillWorks()
    {
        var session = new ForceCapableSession();
        session.StageOutcomes.Enqueue(new StageOutcome
        {
            Success = false,
            BlockedByForceGate = true,
            Messages = ["Livery scan: 10 liveries from 3 files; 1 recovered leniently; 0 unreadable", GateMessage],
        });
        session.StageOutcomes.Enqueue(new StageOutcome
        {
            Success = true,
            WrittenPath = InstalledPath,
            BackupPath = @"C:\fake\backups\F-Vintage_Gen1.20260703T090000Z.xml",
            Messages = ["Staged F-Vintage_Gen1.xml"],
        });
        var watcher = new FakeFileWatcher();
        var vm = new BriefingViewModel(session, watcher);

        vm.StageGridCommand.Execute(null);

        Assert.Equal(StageBannerTone.Info, vm.BannerTone);
        // The banner is a clear, directive prompt (names the button, promises a backup), not the
        // raw gate message, and never reads as a failure.
        Assert.Contains("Overwrite anyway", vm.StageBanner);
        Assert.Contains("backup", vm.StageBanner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed", vm.StageBanner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("aborted", vm.StageBanner, StringComparison.OrdinalIgnoreCase);

        // The existing Stage-anyway escape hatch stays reachable and works unchanged.
        Assert.False(vm.StageSucceeded);
        Assert.True(vm.CanForceStage);
        Assert.Null(watcher.Watching);

        vm.ForceStageGridCommand.Execute(null);
        Assert.Equal([false, true], session.ForceFlags);
        Assert.Equal(StageBannerTone.Success, vm.BannerTone);
        Assert.True(vm.StageSucceeded);
        Assert.Equal(InstalledPath, watcher.Watching);
    }

    [Fact]
    public void Staged_IsGreen()
    {
        var (vm, watcher) = VmWith(new StageOutcome
        {
            Success = true,
            WrittenPath = InstalledPath,
            BackupPath = @"C:\fake\backups\F-Vintage_Gen1.20260703T090000Z.xml",
            Messages = ["Staged F-Vintage_Gen1.xml"],
        });

        Assert.Equal(StageBannerTone.Success, vm.BannerTone);
        Assert.StartsWith("✔ AMS2 is set up", vm.StageBanner);
        Assert.Equal(InstalledPath, watcher.Watching);
    }

    [Fact]
    public void NoOpAlreadyMatches_IsGreen_AndStillWatches()
    {
        var (vm, watcher) = VmWith(new StageOutcome
        {
            Success = true,
            NoOpAlreadyMatches = true,
            WrittenPath = InstalledPath,
            Messages = ["Installed F-Vintage_Gen1.xml already matches this round's grid."],
        });

        Assert.Equal(StageBannerTone.Success, vm.BannerTone);
        Assert.Contains("already set up", vm.StageBanner);
        Assert.Equal(InstalledPath, watcher.Watching);
    }

    [Fact]
    public void RealFailure_StaysRed_AndSaysFailed()
    {
        var (vm, watcher) = VmWith(new StageOutcome
        {
            Success = false,
            Messages =
            [
                "Error: Vehicle class 'F-Vintage_Gen1' is not in the content library.",
                "Staging aborted, fix the preflight errors above and stage again.",
            ],
        });

        Assert.Equal(StageBannerTone.Error, vm.BannerTone);
        Assert.StartsWith("Couldn't set up", vm.StageBanner);
        Assert.Null(watcher.Watching);
    }

    // ---------- aggregate details (collapsed by default) ----------

    [Fact]
    public void Details_ExposedCollapsed_ToggleExpands_NextOutcomeResets()
    {
        var session = new FakeCareerSession();
        session.StageOutcomes.Enqueue(new StageOutcome
        {
            Success = true,
            WrittenPath = InstalledPath,
            Messages = ["Livery scan: 5 liveries from 4 files; 2 recovered leniently; 1 unreadable"],
            Details = [@"C:\overrides\car\broken.xml: not readable as XML."],
        });
        session.StageOutcomes.Enqueue(new StageOutcome
        {
            Success = true,
            WrittenPath = InstalledPath,
            Messages = ["Staged."],
        });
        var vm = new BriefingViewModel(session, new FakeFileWatcher());

        vm.StageGridCommand.Execute(null);
        Assert.True(vm.HasStageDetails);
        Assert.False(vm.StageDetailsExpanded); // collapsed by default
        var detail = Assert.Single(vm.StageDetails);
        Assert.Contains("broken.xml", detail);

        vm.ToggleStageDetailsCommand.Execute(null);
        Assert.True(vm.StageDetailsExpanded);

        // A new outcome collapses and replaces the details.
        vm.StageGridCommand.Execute(null);
        Assert.False(vm.StageDetailsExpanded);
        Assert.False(vm.HasStageDetails);
    }
}
