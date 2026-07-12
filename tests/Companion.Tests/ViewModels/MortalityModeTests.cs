using Companion.Core.Career;
using Companion.Core.Packs;
using Companion.Data;
using Companion.ViewModels.Services;
using Companion.ViewModels.Wizard;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Character death &amp; injury, Slice 1: the <see cref="MortalityMode"/> creation choice + the Normal-mode
/// FILE-level save &amp; reload. Slice 1 makes NO fold change, so the determinism guarantee is that an Off
/// career is byte-identical to a pre-feature career AND a Normal/Hardcore career re-simulates identically
/// too (the mode only rides the start state + the career table; the fold never reads it yet). The save
/// surface is proven end-to-end: snapshot → play on → restore reverts; Hardcore has no saves at all.
/// </summary>
public sealed class MortalityModeTests : IDisposable
{
    private const long Seed = 20260712;
    private const string PlayerId = "driver.hulme";

    private readonly string _root = Directory.CreateTempSubdirectory("companion-mortality-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private CareerEnvironment Env() => ViewModelTestData.Environment(
        documentsDirectory: Path.Combine(_root, "docs"),
        library: TestPackBuilder.Library());

    private CareerSessionService Create(MortalityMode mortality, string careerPath)
    {
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), Path.Combine(_root, "pack"));
        return CareerSessionService.CreateCareer(
            new CareerCreationRequest
            {
                PackDirectory = Path.Combine(_root, "pack"),
                CareerFilePath = careerPath,
                CareerName = $"{mortality} Career",
                MasterSeed = Seed,
                PlayerLiveryName = TestPackBuilder.StockLivery2,
                Mortality = mortality,
            },
            Env());
    }

    private string CareerPath(string name) => Path.Combine(_root, "careers", name + ".ams2career");

    private static void ApplyOneRound(ICareerSession session)
    {
        var seats = session.CurrentGrid();
        session.Apply(new ResultDraft
        {
            Classified = seats.Select(s => s.DriverId).ToList(),
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });
    }

    private static void ApplyWholeSeason(ICareerSession session)
    {
        while (!session.Summary.SeasonComplete)
            ApplyOneRound(session);
    }

    private static string StartPlayerStateJson(string careerPath)
    {
        using var db = CareerDatabase.Open(careerPath);
        using var command = db.Connection.CreateCommand();
        command.CommandText =
            "SELECT state_json FROM player_state WHERE stage = 'start' ORDER BY season_id LIMIT 1;";
        return (string)command.ExecuteScalar()!;
    }

    private static void AssertReplaysByteIdentical(string careerPath, SeasonPack pack)
    {
        using var db = CareerDatabase.Open(careerPath);
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = PlayerId,
            PlayerAge = 30,
            CharacterRules = rules.Character,
        };
        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)Seed), inputs);
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }

    // ---- (a) legacy gate: an Off career is byte-identical to a pre-feature career ----

    [Fact]
    public void OffCareer_OmitsMortalityFromStartState_AndReplaysByteIdentically()
    {
        string careerPath = CareerPath("off");
        SeasonPack pack;
        using (var session = Create(MortalityMode.Off, careerPath))
        {
            Assert.Equal(MortalityMode.Off, session.Mortality);
            Assert.False(session.SavesEnabled);
            pack = session.Pack;
            ApplyWholeSeason(session);
            Assert.NotNull(session.SeasonReview());
        }

        // WhenWritingDefault omits Off ⇒ the start blob has no mortality key ⇒ byte-identical to a
        // career authored before the feature existed.
        Assert.DoesNotContain("mortality", StartPlayerStateJson(careerPath), StringComparison.OrdinalIgnoreCase);
        AssertReplaysByteIdentical(careerPath, pack);
    }

    // ---- (b) a Normal career round-trips the mode through the DB + start state, still byte-identical ----

    [Fact]
    public void NormalCareer_PersistsModeThroughDbAndStartState_AndReplaysByteIdentically()
    {
        string careerPath = CareerPath("normal");
        SeasonPack pack;
        using (var session = Create(MortalityMode.Normal, careerPath))
        {
            Assert.Equal(MortalityMode.Normal, session.Mortality);
            Assert.True(session.SavesEnabled);
            pack = session.Pack;
            ApplyWholeSeason(session);
        }

        // Persisted on the career table (career-wide) and mirrored into the start state (for the fold).
        Assert.Contains("mortality", StartPlayerStateJson(careerPath), StringComparison.OrdinalIgnoreCase);
        using (var db = CareerDatabase.Open(careerPath))
        {
            using var command = db.Connection.CreateCommand();
            command.CommandText = "SELECT mortality_mode FROM career WHERE id = 1;";
            Assert.Equal((long)MortalityMode.Normal, (long)command.ExecuteScalar()!);
        }

        // Reopen reads the mode back off the career table.
        using (var reopened = CareerSessionService.OpenCareer(careerPath, Env()))
        {
            Assert.Equal(MortalityMode.Normal, reopened.Mortality);
            Assert.True(reopened.SavesEnabled);
        }

        // No fold change ⇒ even a Normal career re-simulates byte-identically.
        AssertReplaysByteIdentical(careerPath, pack);
    }

    // ---- (c) save → play on → restore reverts the career (Normal) ----

    [Fact]
    public void NormalSave_ThenPlayOn_ThenRestore_RevertsToTheSnapshot()
    {
        string careerPath = CareerPath("restore");
        SaveSlotInfo slot;
        using (var session = Create(MortalityMode.Normal, careerPath))
        {
            ApplyOneRound(session);                    // round 1 done, one snapshot
            slot = session.SaveToSlot("after round 1");
            Assert.Single(session.AllSnapshots());

            ApplyOneRound(session);                    // round 2 done — season complete
            Assert.True(session.Summary.SeasonComplete);
            Assert.Equal(2, session.AllSnapshots().Count);

            // Restore reverts wholesale; THIS session's DB is closed afterwards (spent).
            session.RestoreSlot(slot.SlotId);
        }

        using var reopened = CareerSessionService.OpenCareer(careerPath, Env());
        Assert.False(reopened.Summary.SeasonComplete);
        Assert.Single(reopened.AllSnapshots());         // back to the one-round snapshot
    }

    // ---- (c2) an unknown slot id fails clean — the live session is NOT spent ----

    [Fact]
    public void RestoreUnknownSlot_ThrowsWithoutSpendingTheSession()
    {
        string careerPath = CareerPath("restore-bad");
        using var session = Create(MortalityMode.Normal, careerPath);
        ApplyOneRound(session);

        Assert.Throws<InvalidOperationException>(() => session.RestoreSlot("no-such-slot"));

        // The DB was not torn down — the session is still fully usable.
        Assert.Equal(MortalityMode.Normal, session.Mortality);
        Assert.Single(session.AllSnapshots());
        ApplyOneRound(session);                        // a live write proves the DB survived
        Assert.True(session.Summary.SeasonComplete);
    }

    // ---- (d) autosave at season start + manual slot listing (Normal) ----

    [Fact]
    public void NormalCreation_Autosaves_AndManualSaveAddsASlot()
    {
        string careerPath = CareerPath("autosave");
        using var session = Create(MortalityMode.Normal, careerPath);

        var autosaves = session.SaveSlots();
        var autosave = Assert.Single(autosaves);
        Assert.True(autosave.IsAutosave);
        Assert.Equal("autosave-season-1", autosave.SlotId);

        ApplyOneRound(session);
        var manual = session.SaveToSlot("my checkpoint");
        Assert.False(manual.IsAutosave);
        Assert.Equal("manual-001", manual.SlotId);
        Assert.Equal(2, session.SaveSlots().Count);

        session.DeleteSlot(manual.SlotId);
        Assert.Single(session.SaveSlots());
    }

    // ---- (e) Hardcore: no save surface at all ----

    [Fact]
    public void HardcoreCareer_HasNoSaves_AndSaveOrRestoreThrow()
    {
        string careerPath = CareerPath("hardcore");
        using var session = Create(MortalityMode.Hardcore, careerPath);

        Assert.Equal(MortalityMode.Hardcore, session.Mortality);
        Assert.False(session.SavesEnabled);
        Assert.Empty(session.SaveSlots());
        // Hardcore never autosaves — no snapshot files were written at all.
        Assert.Empty(SaveSlotStore.List(careerPath));

        Assert.Throws<InvalidOperationException>(() => session.SaveToSlot("nope"));
        Assert.Throws<InvalidOperationException>(() => session.RestoreSlot("nope"));
    }

    // ---- Off career: no autosave either ----

    [Fact]
    public void OffCareer_NeverAutosaves()
    {
        string careerPath = CareerPath("off-nosave");
        using var session = Create(MortalityMode.Off, careerPath);

        Assert.Empty(session.SaveSlots());
        Assert.Empty(SaveSlotStore.List(careerPath));
    }
}

/// <summary>The wizard threads the mortality choice into the creation request (default Off).</summary>
public sealed class MortalityWizardTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-mortality-wizard-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private NewCareerWizardViewModel WizardThroughSeat(out FakeCareerFactory factory)
    {
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), Path.Combine(_root, "packs", "pack"));
        factory = new FakeCareerFactory();
        var wizard = new NewCareerWizardViewModel(
            ViewModelTestData.Environment(
                documentsDirectory: Path.Combine(_root, "docs"),
                library: TestPackBuilder.Library()),
            factory,
            packSearchRoots: [Path.Combine(_root, "packs")],
            careersDirectory: Path.Combine(_root, "careers"),
            seedSource: new Random(9));

        wizard.SelectedPack = Assert.Single(wizard.Packs);
        wizard.NextCommand.Execute(null);              // -> Verification
        if (wizard.HasWarnings) wizard.ProceedAnyway = true;
        wizard.NextCommand.Execute(null);              // -> SeatPick
        wizard.SelectedSeat = wizard.Seats.First();
        return wizard;
    }

    [Fact]
    public void MortalityDefaultsToOff_AndFlowsToTheRequest()
    {
        var wizard = WizardThroughSeat(out var factory);
        Assert.Equal(MortalityMode.Off, wizard.MortalityMode);

        wizard.NextCommand.Execute(null);              // -> Character
        wizard.NextCommand.Execute(null);              // -> Grid
        wizard.NextCommand.Execute(null);              // -> Confirm
        wizard.NextCommand.Execute(null);              // Create

        Assert.Equal(MortalityMode.Off, factory.LastRequest!.Mortality);
    }

    [Fact]
    public void PickingHardcore_FlowsToTheRequest_AndWarnsInTheSummary()
    {
        var wizard = WizardThroughSeat(out var factory);

        wizard.MortalityMode = MortalityMode.Hardcore;
        Assert.Contains("DELETES", wizard.MortalityModeSummary, StringComparison.OrdinalIgnoreCase);

        wizard.NextCommand.Execute(null);              // -> Character
        wizard.NextCommand.Execute(null);              // -> Grid
        wizard.NextCommand.Execute(null);              // -> Confirm
        wizard.NextCommand.Execute(null);              // Create

        Assert.Equal(MortalityMode.Hardcore, factory.LastRequest!.Mortality);
    }
}
