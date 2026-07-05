using Companion.Core.Numerics;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Round-trip coverage for <see cref="CareerSessionService"/> against a temp career DB and
/// the REAL packs/f1-1967 from test output: create → briefing → preview → apply →
/// standings → next round → reopen from the pinned pack, plus the staging outcomes
/// (preflight errors abort, missing install degrades, backup-first, force gate).
/// </summary>
public sealed class SessionServiceTests : IDisposable
{
    private const string PlayerLivery = "Brabham-Repco #2 D. Hulme";

    private readonly string _root = Directory.CreateTempSubdirectory("companion-session-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // SQLite WAL sidecars can outlive the connection briefly on Windows; leaking a
            // temp folder is better than failing the suite.
        }
    }

    private string CareerPath => Path.Combine(_root, "career.ams2career");

    private string DocumentsDirectory => Path.Combine(_root, "docs");

    private CareerCreationRequest Request(string? packDirectory = null) => new()
    {
        PackDirectory = packDirectory ?? ViewModelTestData.RealPackDirectory,
        CareerFilePath = CareerPath,
        CareerName = "Test 1967",
        MasterSeed = 42,
        PlayerLiveryName = PlayerLivery,
    };

    private string CopyRealPack()
    {
        string target = Path.Combine(_root, "pack");
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(ViewModelTestData.RealPackDirectory, "*.json"))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        return target;
    }

    // ---------- the full round-trip ----------

    [Fact]
    public void CreatePreviewApplyReopen_RoundTripsOnTheRealPack()
    {
        string packDirectory = CopyRealPack();
        var environment = ViewModelTestData.Environment(DocumentsDirectory);

        List<string> gridOrder;
        using (var session = CareerSessionService.CreateCareer(Request(packDirectory), environment))
        {
            var summary = session.Summary;
            Assert.Equal("Test 1967", summary.CareerName);
            Assert.Equal(1967, summary.SeasonYear);
            Assert.Equal("Formula One World Championship", summary.SeriesName);
            Assert.Equal(1, summary.CurrentRound);
            Assert.Equal(11, summary.RoundCount);
            Assert.Equal("driver.denny_hulme", summary.PlayerDriverId);
            Assert.Equal(PlayerLivery, summary.PlayerLiveryName);
            Assert.Null(summary.PlayerPosition);
            Assert.False(summary.SeasonComplete);

            var briefing = session.CurrentBriefing();
            Assert.NotNull(briefing);
            Assert.Equal("South African Grand Prix", briefing.Round.Name);
            Assert.Equal("Kyalami Racing Circuit", briefing.VenueDisplayName);
            Assert.False(briefing.IsPlaceholder);

            // Grid = the pack entries whose rounds range covers round 1 (the player
            // REPLACES one of them — replacing never grows the grid).
            var grid = session.CurrentGrid();
            int expectedSeats = session.Pack.Entries.Count(e =>
                Companion.Core.Packs.RoundsRange.TryParse(e.Rounds, out var range, out _) &&
                range.Contains(1));
            Assert.Equal(expectedSeats, grid.Count);
            Assert.True(grid.Count >= 10);
            var player = Assert.Single(grid, s => s.IsPlayer);
            Assert.Equal(PlayerLivery, player.Ams2LiveryName);
            Assert.Equal("driver.denny_hulme", player.DriverId);

            // Full classification in grid order; the last two retire.
            gridOrder = grid.Select(s => s.DriverId).ToList();
            var draft = new ResultDraft
            {
                Classified = gridOrder.Take(gridOrder.Count - 2).ToList(),
                DidNotFinish = new Dictionary<string, string>
                {
                    [gridOrder[^2]] = "m",
                    [gridOrder[^1]] = "a",
                },
                Disqualified = [],
            };

            var confirm = session.Preview(draft);

            // 1967 points: 9-6-4-3-2-1 — the confirm model must equal engine output.
            Assert.Equal(new Rational(9), confirm.RoundPoints.Single(p => p.DriverId == gridOrder[0]).Points);
            Assert.Equal(new Rational(6), confirm.RoundPoints.Single(p => p.DriverId == gridOrder[1]).Points);
            Assert.Equal(new Rational(1), confirm.RoundPoints.Single(p => p.DriverId == gridOrder[5]).Points);
            Assert.Equal(Rational.Zero, confirm.RoundPoints.Single(p => p.DriverId == gridOrder[6]).Points);
            Assert.Equal(Rational.Zero, confirm.RoundPoints.Single(p => p.DriverId == gridOrder[^1]).Points);

            // The confirm headline comes from the unified fold's RoundUpdate (the M5 news
            // engine replaced the static template) and previews deterministically.
            Assert.False(string.IsNullOrWhiteSpace(confirm.Headline));
            Assert.Equal(confirm.Headline, session.Preview(draft).Headline);

            var winnerMove = confirm.Movements.Single(m => m.DriverId == gridOrder[0]);
            Assert.Null(winnerMove.From); // no standings before round 1
            Assert.Equal(1, winnerMove.To);

            // Preview must not commit anything — not even through its fold preview.
            Assert.Null(session.CurrentStandings());
            Assert.Equal(1, session.Summary.CurrentRound);
            Assert.Null(session.CurrentSliderRecommendation());

            session.Apply(draft);

            summary = session.Summary;
            Assert.Equal(2, summary.CurrentRound);
            Assert.Equal(2, summary.PlayerPosition); // Hulme P2 on 6 points
            Assert.False(summary.SeasonComplete);

            // Apply went through the fold: the home header reads the folded player state
            // and the next round has a difficulty recommendation.
            Assert.NotNull(summary.Reputation);
            Assert.NotNull(summary.Opi);
            int? recommendation = session.CurrentSliderRecommendation();
            Assert.NotNull(recommendation);
            Assert.InRange(recommendation.Value, 70, 120);
            Assert.Equal(recommendation, session.CurrentBriefing()!.RecommendedSlider);

            var standings = session.CurrentStandings();
            Assert.NotNull(standings);
            Assert.Equal(1, standings.AfterRound);
            var leader = Assert.Single(standings.Drivers, d => d.Position == 1);
            Assert.Equal(gridOrder[0], leader.DriverId);
            Assert.Equal(new Rational(9), leader.CountedPoints);
            Assert.Single(session.AllSnapshots());
        }

        // Delete the mutable pack folder: reopening MUST rehydrate from the pinned bytes.
        Directory.Delete(packDirectory, recursive: true);

        var reopenEnvironment = ViewModelTestData.Environment(DocumentsDirectory);
        using var reopened = CareerSessionService.OpenCareer(CareerPath, reopenEnvironment);

        var reopenedSummary = reopened.Summary;
        Assert.Equal("Test 1967", reopenedSummary.CareerName);
        Assert.Equal(2, reopenedSummary.CurrentRound);
        Assert.Equal("driver.denny_hulme", reopenedSummary.PlayerDriverId);
        Assert.Equal(PlayerLivery, reopenedSummary.PlayerLiveryName);
        Assert.Equal(2, reopenedSummary.PlayerPosition);
        Assert.Equal(42, reopened.MasterSeed);

        // The folded player state persisted with the career: trend + recommendation survive.
        Assert.NotNull(reopenedSummary.Reputation);
        Assert.NotNull(reopened.CurrentSliderRecommendation());

        var nextBriefing = reopened.CurrentBriefing();
        Assert.NotNull(nextBriefing);
        Assert.Equal("Monaco Grand Prix", nextBriefing.Round.Name);

        var reopenedStandings = reopened.CurrentStandings();
        Assert.NotNull(reopenedStandings);
        Assert.Equal(gridOrder[0], Assert.Single(reopenedStandings.Drivers, d => d.Position == 1).DriverId);
    }

    [Fact]
    public void ReadFeed_ProjectsTheRoundHeadline_WithAWhyChip()
    {
        var environment = ViewModelTestData.Environment(DocumentsDirectory);
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        // No race run yet → the paddock is quiet.
        Assert.Empty(session.ReadFeed());

        var gridOrder = session.CurrentGrid().Select(s => s.DriverId).ToList();
        session.Apply(new ResultDraft
        {
            Classified = gridOrder, // full classification, nobody retires
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });

        // One race → one headline dispatch, projected read-only off the journal.
        var latest = Assert.Single(session.ReadFeed());
        Assert.False(string.IsNullOrWhiteSpace(latest.Headline));
        Assert.Equal(1967, latest.SeasonYear);
        Assert.Equal(1, latest.Round);
        Assert.Equal("race", latest.Kind);
        Assert.Contains("expected", latest.WhyText); // the Why? chip explains the number
    }

    [Fact]
    public void QualifyingOrder_IsInertToScoring_AndSingleRacePackHasNoWeekend()
    {
        var environment = ViewModelTestData.Environment(DocumentsDirectory);
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        Assert.Null(session.CurrentWeekend()); // the 1967 pack runs a single race

        var gridOrder = session.CurrentGrid().Select(s => s.DriverId).ToList();
        var draft = new ResultDraft
        {
            Classified = gridOrder,
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
            QualifyingOrder = gridOrder.AsEnumerable().Reverse().ToList(), // any qualy order
        };

        // Qualifying rides in the envelope but never enters RoundResult, so the scored round is
        // byte-identical with or without it — the standings engine (and the oracle) never see it.
        var withQualy = session.Preview(draft);
        var withoutQualy = session.Preview(draft with { QualifyingOrder = null });
        Assert.Equal(
            withoutQualy.RoundPoints.Select(p => (p.DriverId, p.Points)),
            withQualy.RoundPoints.Select(p => (p.DriverId, p.Points)));
    }

    [Fact]
    public void Preview_RejectsDriversNotInTheRoundGrid()
    {
        var environment = ViewModelTestData.Environment(DocumentsDirectory);
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        var draft = new ResultDraft
        {
            Classified = ["driver.not_in_this_grid"],
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        };

        var ex = Assert.Throws<ArgumentException>(() => session.Preview(draft));
        Assert.Contains("not in the round-1 grid", ex.Message);
    }

    [Fact]
    public void CreateCareer_RefusesAnExistingCareerFile()
    {
        var environment = ViewModelTestData.Environment(DocumentsDirectory);
        Directory.CreateDirectory(_root);
        File.WriteAllText(CareerPath, "already here");

        var ex = Assert.Throws<InvalidOperationException>(
            () => CareerSessionService.CreateCareer(Request(), environment));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public void CreateCareer_RejectsALiveryThatIsNotAPackEntry()
    {
        var environment = ViewModelTestData.Environment(DocumentsDirectory);
        var request = Request() with { PlayerLiveryName = "No Such Livery" };

        var ex = Assert.Throws<InvalidOperationException>(
            () => CareerSessionService.CreateCareer(request, environment));
        Assert.Contains("No Such Livery", ex.Message);
    }

    // ---------- staging ----------

    private string FakeInstallDirectory => Path.Combine(_root, "install");

    private string StagedFilePath =>
        Path.Combine(FakeInstallDirectory, "UserData", "CustomAIDrivers", "F-Vintage_Gen1.xml");

    [Fact]
    public void StageCurrentGrid_MissingInstall_FailsGracefully()
    {
        var environment = ViewModelTestData.Environment(DocumentsDirectory, installDirectory: null);
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        var outcome = session.StageCurrentGrid();

        Assert.False(outcome.Success);
        Assert.Null(outcome.WrittenPath);
        Assert.Contains(outcome.Messages, m => m.Contains("No AMS2 installation"));
    }

    [Fact]
    public void StageCurrentGrid_PreflightError_AbortsBeforeWriting()
    {
        // An empty library makes the vehicle class unknown — a preflight ERROR.
        var environment = ViewModelTestData.Environment(
            DocumentsDirectory, FakeInstallDirectory, ViewModelTestData.EmptyLibrary());
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        var outcome = session.StageCurrentGrid();

        Assert.False(outcome.Success);
        Assert.Null(outcome.WrittenPath);
        Assert.Contains(outcome.Messages,
            m => m.StartsWith("Error:") && m.Contains("'F-Vintage_Gen1' is not in the content library"));
        Assert.Contains(outcome.Messages, m => m.Contains("Staging aborted"));
        Assert.False(File.Exists(StagedFilePath));
    }

    [Fact]
    public void StageCurrentGrid_WarningsOnly_StagesBackupFirst()
    {
        // Real library, no installed skin packs: livery findings are warnings, which must
        // NOT abort staging.
        var environment = ViewModelTestData.Environment(DocumentsDirectory, FakeInstallDirectory);
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        var first = session.StageCurrentGrid();
        Assert.True(first.Success);
        Assert.Equal(StagedFilePath, first.WrittenPath);
        Assert.True(File.Exists(StagedFilePath));
        Assert.Null(first.BackupPath); // nothing existed before the first stage
        Assert.False(first.NoOpAlreadyMatches);

        // Re-staging the identical round is a diff-aware NO-OP (NAMeS-first, locked decision
        // #7b): the installed file already matches, so nothing is written or backed up.
        var second = session.StageCurrentGrid();
        Assert.True(second.Success);
        Assert.True(second.NoOpAlreadyMatches);
        Assert.Null(second.BackupPath);
    }

    [Fact]
    public void StageCurrentGrid_ForeignFile_RequiresForce_AndForceStillBacksUp()
    {
        var environment = ViewModelTestData.Environment(DocumentsDirectory, FakeInstallDirectory);
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        // A curated community file (no generated marker) already sits at the target.
        Directory.CreateDirectory(Path.GetDirectoryName(StagedFilePath)!);
        const string communityFile = "<custom_ai_drivers><!-- hand made --></custom_ai_drivers>";
        File.WriteAllText(StagedFilePath, communityFile);

        var refused = session.StageCurrentGrid();
        Assert.False(refused.Success);
        // The gate is an EXPECTED choice, not a failure: the outcome says so explicitly and
        // carries the calm explanation the amber banner shows.
        Assert.True(refused.BlockedByForceGate);
        Assert.Contains(refused.Messages, m =>
            m.Contains("Your installed F-Vintage_Gen1.xml differs from this round's grid") &&
            m.Contains("'Stage anyway' takes a timestamped backup first"));
        Assert.Equal(communityFile, File.ReadAllText(StagedFilePath)); // untouched

        var forced = ((IForceStaging)session).StageCurrentGrid(force: true);
        Assert.True(forced.Success);
        Assert.NotNull(forced.BackupPath);
        Assert.Equal(communityFile, File.ReadAllText(forced.BackupPath!)); // backup-first
        Assert.NotEqual(communityFile, File.ReadAllText(StagedFilePath));
    }
}
