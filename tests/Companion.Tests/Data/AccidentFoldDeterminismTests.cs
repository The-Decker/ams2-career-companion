using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Packs;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// Character death &amp; injury, Slice 3 — the DERIVED accident d500 fold + Hardcore permadeath. The roll is
/// QUADRUPLE-gated (mortality on + a character + not already dead + an accident severity captured), so an
/// Off / no-accident career draws nothing and stays byte-identical. A mortality career's accident row
/// re-simulates byte-for-byte; a fatal roll sets Deceased (terminal), and a Hardcore death physically
/// deletes the career file. Deterministic outcomes are forced with an out-of-range durability (a large
/// safety offset), so no test depends on which way the seeded d500 happens to land.
/// </summary>
public sealed class AccidentFoldDeterminismTests : IDisposable
{
    private const string PlayerId = "driver.hulme";
    private const long Seed = 20260712;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-accident-fold-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

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

    private static void ApplyNormalRound(ICareerSession session)
    {
        var seats = session.CurrentGrid();
        session.Apply(new ResultDraft
        {
            Classified = seats.Select(s => s.DriverId).ToList(),
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });
    }

    private static ReplayReport Resimulate(CareerDatabase db, SeasonPack pack)
    {
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
        return ReplayService.Resimulate(db, pack, unchecked((ulong)Seed), inputs);
    }

    private static string Diverged(ReplayReport report) =>
        $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
        $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}";

    [Fact]
    public void PerksJson_ShipsTheAccidentBands()
    {
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        Assert.NotNull(rules.Character.Accident);
        Assert.Equal(4, rules.Character.Accident!.Light.Count);
        Assert.Equal(5, rules.Character.Accident.Medium.Count);
        Assert.Equal(500, rules.Character.Accident.Heavy[^1].UpTo);
    }

    [Fact]
    public void MortalityCareer_SurvivesAnAccident_EmitsDerivedRow_AndReplaysByteIdentically()
    {
        // A hugely durable driver ALWAYS survives (offset floors the effective roll at None) — so the
        // career plays on and we exercise the accident fold + the injury-state carry-forward over 2 rounds.
        string careerPath = Path.Combine(_root, "careers", "survive.ams2career");
        SeasonPack pack;
        using (var session = Create("Survive", MortalityMode.Normal, durability: 50.0, careerPath))
        {
            pack = session.Pack;
            ApplyPlayerAccident(session, AccidentSeverity.Heavy);
            Assert.False(session.PlayerMortality().Deceased); // survived (deterministic)
            ApplyNormalRound(session);
            Assert.NotNull(session.SeasonReview());
        }

        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        var accidentRow = Assert.Single(
            JournalStore.ReadSeason(db, seasonId), r => r.Phase == JournalPhases.PlayerAccident);
        Assert.Equal("accident-none", accidentRow.Cause);

        var report = Resimulate(db, pack);
        Assert.True(report.Identical, Diverged(report));
    }

    [Fact]
    public void OffCareerWithCharacter_Accident_DrawsNothing_AndReplaysByteIdentically()
    {
        // Off mode never rolls — even a lethal durability produces no accident row and no death, so the
        // legacy gate holds (zero accident-stream draws ⇒ byte-identical to a pre-feature career).
        string careerPath = Path.Combine(_root, "careers", "off.ams2career");
        SeasonPack pack;
        using (var session = Create("Off Accident", MortalityMode.Off, durability: -50.0, careerPath))
        {
            pack = session.Pack;
            ApplyPlayerAccident(session, AccidentSeverity.Heavy);
            Assert.False(session.PlayerMortality().Deceased);
            ApplyNormalRound(session);
            Assert.NotNull(session.SeasonReview());
        }

        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        Assert.DoesNotContain(
            JournalStore.ReadSeason(db, seasonId), r => r.Phase == JournalPhases.PlayerAccident);

        var report = Resimulate(db, pack);
        Assert.True(report.Identical, Diverged(report));
    }

    [Fact]
    public void NormalAccidentDeath_SetsDeceased_KeepsTheFile_RefusesRounds_AndReplaysByteIdentically()
    {
        string careerPath = Path.Combine(_root, "careers", "normaldeath.ams2career");
        SeasonPack pack;
        using (var session = Create("Normal Death", MortalityMode.Normal, durability: -50.0, careerPath))
        {
            pack = session.Pack;
            ApplyPlayerAccident(session, AccidentSeverity.Heavy); // forced death

            var status = session.PlayerMortality();
            Assert.True(status.Deceased);
            Assert.False(status.CareerFileDeleted);
            // Terminal: a dead driver takes no more rounds (Normal keeps the save to restore instead).
            Assert.Throws<InvalidOperationException>(() => ApplyNormalRound(session));
        }

        Assert.True(File.Exists(careerPath)); // Normal never deletes
        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        Assert.Contains(
            JournalStore.ReadSeason(db, seasonId),
            r => r.Phase == JournalPhases.PlayerAccident && r.Cause == "accident-death");

        var report = Resimulate(db, pack);
        Assert.True(report.Identical, Diverged(report));
    }

    [Fact]
    public void HardcoreAccidentDeath_PhysicallyDeletesTheCareerFileAndSaves()
    {
        string careerPath = Path.Combine(_root, "careers", "hardcore.ams2career");
        using var session = Create("Hardcore Death", MortalityMode.Hardcore, durability: -50.0, careerPath);
        Assert.True(File.Exists(careerPath));

        ApplyPlayerAccident(session, AccidentSeverity.Heavy); // forced death → the one destructive op

        var status = session.PlayerMortality();
        Assert.True(status.Deceased);
        Assert.True(status.CareerFileDeleted);
        Assert.False(File.Exists(careerPath));
        Assert.False(Directory.Exists(SaveSlotStore.SavesDirectoryFor(careerPath)));

        // The spent session refuses any further work.
        Assert.Throws<InvalidOperationException>(() => ApplyNormalRound(session));
    }
}
