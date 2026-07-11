using Companion.Ams2.ContentLibrary;
using Companion.Core.Packs;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// The opt-in modded field (Mike's Iris &amp; Azalea McLaren teams): a pack that declares a
/// <see cref="PackModdedField"/> fields the extra entries ONLY when the tick is on AND the required
/// car mod is installed — otherwise the base field, byte-identically. Proves the gate on all four
/// corners (tick+installed, tick+missing, no-tick, no-mod pack) and that a modded career
/// re-simulates byte-identically (the transformed pack is pinned).
/// </summary>
public sealed class ModdedFieldGateTests : IDisposable
{
    private const string SeatA = "Stock Livery #1";
    private const string SeatB = "Stock Livery #2";
    private const string ModLivery = "Mod Car #9";
    private const long Seed = 20260710;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-modded-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The two-round pack plus a MODDED FIELD adding one entry on a mod vehicle
    /// (<c>mod_car</c>), referencing a team + driver already in the pack.</summary>
    private static SeasonPack ModdedPack()
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        return basePack with
        {
            // A DISTINCT third driver for the mod car (the base pack has brabham + hulme).
            Drivers = [.. basePack.Drivers, TestPackBuilder.Driver("driver.modrookie")],
            Manifest = basePack.Manifest with
            {
                ModdedField = new PackModdedField
                {
                    VehicleId = "mod_car",
                    ModName = "Test Mod (author)",
                    Entries =
                    [
                        new PackModdedEntry
                        {
                            TeamId = "team.brabham",
                            DriverId = "driver.modrookie",
                            Number = "9",
                            Rounds = "1-2",
                            Ams2LiveryName = ModLivery,
                        },
                    ],
                },
            },
        };
    }

    /// <summary>A content library whose vehicle set includes (or omits) the mod car, and whose
    /// livery list covers the extra mod livery so the resolver can seat it.</summary>
    private static Ams2ContentLibrary Library(bool modInstalled)
    {
        var baseLib = TestPackBuilder.Library();
        var vehicles = new Dictionary<string, Ams2Vehicle>(baseLib.Vehicles, StringComparer.Ordinal);
        if (modInstalled)
            vehicles["mod_car"] = new() { Id = "mod_car", Dir = "mod_car", VehicleClass = TestPackBuilder.VintageClass };
        return new()
        {
            ExtractedFrom = baseLib.ExtractedFrom,
            Classes = baseLib.Classes,
            Vehicles = vehicles,
            Tracks = baseLib.Tracks,
            Liveries = new Dictionary<string, Ams2LiveryClassEntry>(StringComparer.Ordinal)
            {
                [TestPackBuilder.VintageClass] = new()
                {
                    Name = TestPackBuilder.VintageClass,
                    StockLib1563 = [SeatA, SeatB, ModLivery],
                },
            },
        };
    }

    [Theory]
    [InlineData(true, true, 3)]    // tick on + mod installed -> the extra car joins (2 base + 1)
    [InlineData(true, false, 2)]   // tick on + mod MISSING   -> base field only
    [InlineData(false, true, 2)]   // tick off + mod present  -> base field only
    public void TheGate_AddsTheModCar_OnlyWhenTickedAndInstalled(bool useMod, bool modInstalled, int expectedField)
    {
        string packDirectory = Path.Combine(_root, "packs", $"{useMod}-{modInstalled}");
        TestPackBuilder.Write(ModdedPack(), packDirectory);
        // The install dir must exist AND hold the override folder for the preflight to pass.
        string installDir = Path.Combine(_root, "install", $"{useMod}-{modInstalled}");
        if (modInstalled)
            Directory.CreateDirectory(Path.Combine(installDir, "Vehicles", "Textures", "CustomLiveries", "Overrides", "mod_car"));
        else
            Directory.CreateDirectory(installDir);

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs", $"{useMod}-{modInstalled}"),
            library: Library(modInstalled),
            installDirectory: installDir);

        string careerPath = Path.Combine(_root, "careers", $"{useMod}-{modInstalled}.ams2career");
        int fieldSize;
        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "modded",
                       MasterSeed = Seed,
                       PlayerLiveryName = SeatA,
                       UseModdedField = useMod,
                   },
                   environment))
        {
            fieldSize = session.CurrentGrid().Count;
        }

        Assert.Equal(expectedField, fieldSize);
    }

    [Fact]
    public void AModdedCareer_ReplaysByteIdentically()
    {
        string packDirectory = Path.Combine(_root, "packs", "replay");
        TestPackBuilder.Write(ModdedPack(), packDirectory);
        string installDir = Path.Combine(_root, "install", "replay");
        Directory.CreateDirectory(Path.Combine(installDir, "Vehicles", "Textures", "CustomLiveries", "Overrides", "mod_car"));

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs", "replay"),
            library: Library(modInstalled: true),
            installDirectory: installDir);

        string careerPath = Path.Combine(_root, "careers", "replay.ams2career");
        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "replay",
                       MasterSeed = Seed,
                       PlayerLiveryName = SeatA,
                       UseModdedField = true,
                   },
                   environment))
        {
            Assert.Equal(3, session.CurrentGrid().Count); // the mod car is in the pinned field
            for (int round = 0; round < 2; round++)
            {
                var seats = session.CurrentGrid();
                session.Apply(new ResultDraft
                {
                    Classified = seats.Select(s => s.DriverId).ToList(),
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                });
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(careerPath);
        // The PINNED pack carries the modded field — replay reads it, no install/tick needed.
        var pack = PinnedPackEnvelope.LoadSeasonPack(
            CareerStore.ReadPinnedPack(db, "test-pack", "1.0.0").PackJson);
        Assert.Equal(3, pack.Entries.Count);

        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)Seed), new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = "driver.brabham",
            PlayerAge = 30,
            CharacterRules = rules.Character,
        });
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }

    [Fact]
    public void APackWithoutAModdedField_IsUntouched()
    {
        var pack = TestPackBuilder.TwoRoundPack();
        Assert.False(ModdedFieldTransform.HasModdedField(pack));
        Assert.Null(Companion.Ams2.Preflight.ModdedVehiclePreflight.RequiredModVehicleFor(
            pack, TestPackBuilder.Library(), _root));
    }
}
