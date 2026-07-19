using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Packs;
using Companion.Data;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Character death &amp; injury, Slice 5, the rich <see cref="DeathScreenModel"/> behind the death screen. A
/// pure projection: an in-world obituary, the career record, the fatal accident's cause/venue, and (Normal)
/// the restorable save slots. It reads folded state only (no fold change), so a dead career still replays
/// byte-identically. On a Hardcore death the model is captured BEFORE the file is deleted, so it renders
/// with no DB. Deaths are forced with an out-of-range durability (a large safety offset).
/// </summary>
public sealed class DeathScreenModelTests : IDisposable
{
    private const string PlayerId = "driver.hulme";
    private const long Seed = 20260712;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-death-screen-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ---------- pure Build tests (obituary / cause / restore copy) ----------

    private static CareerRecordsBook Record(int seasons, int wins, int podiums, int titles, int? best = null) =>
        new() { SeasonsRaced = seasons, Wins = wins, Podiums = podiums, Championships = titles, BestFinish = best };

    [Fact]
    public void Build_TitleWinningRecord_ReadsAsARespectfulObituary()
    {
        var model = DeathScreenModel.Build(
            MortalityMode.Normal, "Ayrton Prost", age: 34,
            AccidentSeverity.Heavy, venue: "Monaco", round: 6,
            Record(seasons: 3, wins: 5, podiums: 12, titles: 1), seasons: [], restoreSlots: []);

        Assert.Equal("Killed in a heavy accident at Monaco (round 6).", model.CauseOfDeath);
        Assert.Contains("Ayrton Prost, aged 34, was killed in a heavy accident at Monaco (round 6).", model.Obituary);
        Assert.Contains("Across 3 seasons: 5 wins, 12 podiums, and 1 title.", model.Obituary);
        Assert.Equal(AccidentSeverity.Heavy, model.Severity);
        Assert.Equal(6, model.Round);
        Assert.Equal("Monaco", model.Venue);
    }

    [Fact]
    public void Build_WinlessCareer_ReadsAsAStoryLeftUnfinished()
    {
        var model = DeathScreenModel.Build(
            MortalityMode.Hardcore, "Rookie Racer", age: null,
            AccidentSeverity.Medium, venue: null, round: null,
            Record(seasons: 1, wins: 0, podiums: 0, titles: 0, best: 8), seasons: [], restoreSlots: []);

        Assert.Equal("Killed in an accident at the race.", model.CauseOfDeath);       // no venue/round → "the race"
        Assert.DoesNotContain("aged", model.Obituary);                                 // no age clause
        Assert.Contains("a story left unfinished", model.Obituary);
        Assert.Contains("best finish of P8", model.Obituary);
    }

    [Fact]
    public void Build_PermadeathAndRestore_FlagsReflectModeAndSlots()
    {
        var slot = new SaveSlotInfo
        {
            SlotId = "autosave-season-1", Label = "Season start", SeasonYear = 1988,
            Round = 1, CreatedUtc = "2026-07-12T00:00:00Z", IsAutosave = true,
        };

        var hardcore = DeathScreenModel.Build(
            MortalityMode.Hardcore, "X", null, AccidentSeverity.Heavy, "A", 1, Record(1, 0, 0, 0), [], []);
        Assert.True(hardcore.IsPermadeath);
        Assert.False(hardcore.CanRestore);

        var normalNoSaves = DeathScreenModel.Build(
            MortalityMode.Normal, "X", null, AccidentSeverity.Heavy, "A", 1, Record(1, 0, 0, 0), [], []);
        Assert.False(normalNoSaves.IsPermadeath);
        Assert.False(normalNoSaves.CanRestore);                                        // no slots → cannot restore

        var normalWithSave = DeathScreenModel.Build(
            MortalityMode.Normal, "X", null, AccidentSeverity.Heavy, "A", 1, Record(1, 0, 0, 0), [], [slot]);
        Assert.True(normalWithSave.CanRestore);
    }

    // ---------- integration: a real forced death surfaces the model ----------

    private static CharacterProfile Character(double durability) => new()
    {
        Name = "Crash McTest",
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.55, ["oneLap"] = 0.50, ["craft"] = 0.50, ["racecraft"] = 0.50,
            ["adaptability"] = 0.50, ["marketability"] = 0.55, ["durability"] = durability,
        },
        PerkIds = [],
        CpUnspent = 0,
    };

    private CareerSessionService Create(string name, MortalityMode mode, double durability, string careerPath)
    {
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), Path.Combine(_root, "pack"));
        return CareerSessionService.CreateCareer(
            new CareerCreationRequest
            {
                PackDirectory = Path.Combine(_root, "pack"),
                CareerFilePath = careerPath,
                CareerName = name,
                MasterSeed = Seed,
                PlayerLiveryName = TestPackBuilder.StockLivery2,
                Character = Character(durability),
                Mortality = mode,
            },
            ViewModelTestData.Environment(
                documentsDirectory: Path.Combine(_root, "docs"),
                library: TestPackBuilder.Library()));
    }

    private static void ApplyPlayerAccident(ICareerSession session, AccidentSeverity severity)
    {
        var seats = session.CurrentGrid();
        session.Apply(new ResultDraft
        {
            Classified = seats.Where(s => s.DriverId != PlayerId).Select(s => s.DriverId).ToList(),
            DidNotFinish = new Dictionary<string, string> { [PlayerId] = "a" },
            Disqualified = [],
            PlayerAccidentSeverity = severity,
        });
    }

    [Fact]
    public void AliveCareer_HasNoDeathScreen()
    {
        string careerPath = Path.Combine(_root, "careers", "alive.ams2career");
        using var session = Create("Alive", MortalityMode.Normal, durability: 50.0, careerPath);
        ApplyPlayerAccident(session, AccidentSeverity.Heavy); // survives (huge safety offset)

        Assert.False(session.PlayerMortality().Deceased);
        Assert.Null(session.DeathScreen());
    }

    [Fact]
    public void NormalDeath_BuildsALiveModel_WithCauseVenueRecordAndRestore_AndReplaysByteIdentically()
    {
        string careerPath = Path.Combine(_root, "careers", "normaldeath.ams2career");
        SeasonPack pack;
        using (var session = Create("Normal Death", MortalityMode.Normal, durability: -50.0, careerPath))
        {
            pack = session.Pack;
            ApplyPlayerAccident(session, AccidentSeverity.Heavy); // forced death

            var model = session.DeathScreen();
            Assert.NotNull(model);
            Assert.Equal(MortalityMode.Normal, model!.Mode);
            Assert.False(model.IsPermadeath);
            Assert.Equal(AccidentSeverity.Heavy, model.Severity);
            Assert.Equal(1, model.Round);                                   // the accident was round 1
            Assert.Contains("heavy accident", model.CauseOfDeath);
            Assert.False(string.IsNullOrWhiteSpace(model.Venue));           // resolved from the pack round
            Assert.False(string.IsNullOrWhiteSpace(model.Obituary));
            Assert.Contains("Crash McTest", model.Obituary);
            // The restore surface mirrors the live save slots (Normal keeps the file to restore from).
            Assert.Equal(session.SaveSlots().Count, model.RestoreSlots.Count);
            Assert.Equal(model.RestoreSlots.Count > 0, model.CanRestore);
        }

        // A pure read never perturbs the fold, the dead career still re-simulates byte-for-byte.
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

    [Fact]
    public void HardcoreDeath_CapturesADbFreeModel_ThatSurvivesFileDeletion()
    {
        string careerPath = Path.Combine(_root, "careers", "hardcoredeath.ams2career");
        using var session = Create("Hardcore Death", MortalityMode.Hardcore, durability: -50.0, careerPath);

        ApplyPlayerAccident(session, AccidentSeverity.Heavy); // forced death → file physically deleted

        Assert.True(session.PlayerMortality().CareerFileDeleted);
        Assert.False(File.Exists(careerPath));

        // The model was captured before deletion; reading it now touches NO DB (the file is gone).
        var model = session.DeathScreen();
        Assert.NotNull(model);
        Assert.True(model!.IsPermadeath);
        Assert.False(model.CanRestore);
        Assert.Empty(model.RestoreSlots);                                   // Hardcore has no saves, ever
        Assert.Contains("heavy accident", model.CauseOfDeath);
        Assert.Contains("Crash McTest", model.Obituary);

        // Repeated reads keep working (idempotent, DB-free), the shell may refresh the screen freely.
        Assert.Equal(model.Obituary, session.DeathScreen()!.Obituary);
    }
}
