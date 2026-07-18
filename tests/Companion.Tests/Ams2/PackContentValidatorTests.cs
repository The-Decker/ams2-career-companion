using Companion.Ams2.ContentLibrary;
using Companion.Ams2.Packs;
using Companion.Ams2.Preflight;
using Companion.Core.Packs;
using Companion.Core.Scoring;

namespace Companion.Tests.Ams2;

/// <summary>
/// Pass + fail coverage for each content rule in <see cref="PackContentValidator"/>, driven off
/// a minimal valid two-round pack and a small in-memory content library (records constructed
/// directly, no file I/O).
/// </summary>
public class PackContentValidatorTests
{
    private const string VintageClass = "F-Vintage_Gen1";
    private const string VintageCar = "formula_vintage_g1m2";
    private const string OtherClassCar = "copa_fusca_beetle";
    private const string BigTrack = "kyalami_historic";
    private const string FallbackTrack = "kyalami_2020";
    private const string TinyTrack = "cascavel_dirt";
    private const string InstalledLiveryName = "Brabham-Repco #1 J. Brabham";
    private const string StockLiveryName = "Stock Livery #2";

    // ---------- in-memory content library ----------

    private static Ams2ContentLibrary Library() => new()
    {
        ExtractedFrom = "in-memory test fixture",
        Classes = new Dictionary<string, Ams2Class>(StringComparer.Ordinal)
        {
            [VintageClass] = new() { XmlName = VintageClass, Vehicles = [VintageCar] },
            ["CopaFusca"] = new() { XmlName = "CopaFusca", Vehicles = [OtherClassCar] },
        },
        Vehicles = new Dictionary<string, Ams2Vehicle>(StringComparer.Ordinal)
        {
            [VintageCar] = new() { Id = VintageCar, Dir = VintageCar, VehicleClass = VintageClass },
            [OtherClassCar] = new() { Id = OtherClassCar, Dir = OtherClassCar, VehicleClass = "CopaFusca" },
        },
        Tracks = new Dictionary<string, Ams2Track>(StringComparer.Ordinal)
        {
            [BigTrack] = new() { Id = BigTrack, TrackName = "Kyalami Historic", MaxAiParticipants = 20 },
            [FallbackTrack] = new() { Id = FallbackTrack, TrackName = "Kyalami 2020", MaxAiParticipants = 26 },
            [TinyTrack] = new() { Id = TinyTrack, TrackName = "Cascavel Dirt", MaxAiParticipants = 5 },
        },
        Liveries = new Dictionary<string, Ams2LiveryClassEntry>(StringComparer.Ordinal)
        {
            [VintageClass] = new()
            {
                Name = VintageClass,
                StockLib1563 = ["Stock Livery #1", StockLiveryName],
            },
        },
    };

    private static IReadOnlyCollection<InstalledLivery> Installed() =>
    [
        new InstalledLivery
        {
            Name = InstalledLiveryName,
            VehicleFolder = VintageCar,
            SourceFile = @"C:\test\override.xml",
        },
    ];

    // ---------- pack builders ----------

    private static PackManifest Manifest() => new()
    {
        PackId = "test-pack",
        Name = "Test Pack",
        Version = "1.0.0",
        FormatVersion = 1,
        Requires = new PackRequirements
        {
            SkinPacks =
            [
                new PackSkinPackRequirement
                {
                    Name = "F1 1967 Season (Alain Fry)",
                    Url = "https://www.overtake.gg/downloads/example",
                },
            ],
        },
    };

    private static PackRound Round(int number, string date, string trackId = BigTrack, int opponents = 17) => new()
    {
        Round = number,
        Name = $"Round {number}",
        Date = date,
        Track = new PackTrackRef { Id = trackId },
        Laps = 40,
        SetupGuide = new PackSetupGuide { Session = new PackSessionSettings { Opponents = opponents } },
    };

    private static PackDriver Driver(string id) => new()
    {
        Id = id,
        Name = id,
        Ratings = new PackDriverRatings
        {
            RaceSkill = 0.8,
            QualifyingSkill = 0.8,
            Aggression = 0.5,
            Defending = 0.5,
            Stamina = 0.8,
            Consistency = 0.8,
            StartReactions = 0.8,
            WetSkill = 0.8,
            TyreManagement = 0.8,
            AvoidanceOfMistakes = 0.8,
        },
    };

    private static PackEntry Entry(string driverId, string number, string livery) => new()
    {
        TeamId = "team.brabham",
        DriverId = driverId,
        Number = number,
        Rounds = "1-2",
        Ams2LiveryName = livery,
    };

    private static SeasonPack ValidPack() => new()
    {
        Manifest = Manifest(),
        Season = new SeasonDefinition
        {
            Year = 1967,
            SeriesName = "Test Championship",
            Ams2Class = VintageClass,
            PointsSystem = new CatalogSeason { RacePoints = [new(9), new(6), new(4), new(3), new(2), new(1)] },
            Rounds = [Round(1, "1967-01-02"), Round(2, "1967-05-07")],
        },
        Teams =
        [
            new PackTeam { Id = "team.brabham", Name = "Brabham-Repco", CarVehicleIds = [VintageCar] },
        ],
        Drivers = [Driver("driver.brabham"), Driver("driver.hulme")],
        Entries =
        [
            Entry("driver.brabham", "1", InstalledLiveryName),
            Entry("driver.hulme", "2", StockLiveryName),
        ],
    };

    private static PreflightReport Validate(
        SeasonPack pack,
        Ams2ContentLibrary? library = null,
        IReadOnlyCollection<InstalledLivery>? installed = null) =>
        PackContentValidator.Validate(pack, library ?? Library(), installed ?? Installed());

    private static void AssertError(PreflightReport report, string substring) =>
        Assert.Contains(report.Issues,
            i => i.Severity == PreflightSeverity.Error && i.Message.Contains(substring));

    private static void AssertWarning(PreflightReport report, string substring) =>
        Assert.Contains(report.Issues,
            i => i.Severity == PreflightSeverity.Warning && i.Message.Contains(substring));

    private static void AssertNoErrors(PreflightReport report) =>
        Assert.False(report.HasErrors,
            "Unexpected errors:\n" + string.Join("\n", report.Issues.Select(i => $"{i.Severity}: {i.Message}")));

    // ---------- the happy path ----------

    [Fact]
    public void Validate_ValidPack_ProducesNoIssues()
    {
        var report = Validate(ValidPack());

        Assert.Empty(report.Issues);
        Assert.False(report.HasErrors);
    }

    // ---------- item 2: ams2Class ----------

    [Fact]
    public void Validate_UnknownClass_IsAnError()
    {
        var pack = ValidPack();
        pack = pack with { Season = pack.Season with { Ams2Class = "F-Ghost" } };

        AssertError(Validate(pack), "ams2Class 'F-Ghost' is not in the content library");
    }

    [Fact]
    public void Validate_ClassCasingNearMiss_IsASpecificError()
    {
        var pack = ValidPack();
        pack = pack with { Season = pack.Season with { Ams2Class = "f-vintage_gen1" } };

        var report = Validate(pack);
        AssertError(report, "does not match the game's casing 'F-Vintage_Gen1'");
        // The already-reported class error must not cascade into per-car class errors.
        Assert.DoesNotContain(report.Issues, i => i.Message.Contains("Team 'team.brabham'"));
    }

    // ---------- item 3: track ids + fallbacks ----------

    [Fact]
    public void Validate_UnknownTrackId_IsAnError()
    {
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[1] = Round(2, "1967-05-07", trackId: "monsanto_park");
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        AssertError(Validate(pack),
            "Round 2 (Round 2) track id 'monsanto_park' is not in the track library");
    }

    [Fact]
    public void Validate_TrackCasingNearMiss_NamesTheLibraryId()
    {
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[0] = Round(1, "1967-01-02", trackId: "Kyalami_Historic");
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        AssertError(Validate(pack), "differs from library id 'kyalami_historic' in case");
    }

    [Fact]
    public void Validate_UnknownFallbackId_IsAnError()
    {
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[0] = rounds[0] with
        {
            Track = new PackTrackRef { Id = BigTrack, Fallbacks = ["kyalami_ghost"] },
        };
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        AssertError(Validate(pack),
            "Round 1 (Round 1) fallback track id 'kyalami_ghost' is not in the track library");
    }

    [Fact]
    public void Validate_KnownTrackWithKnownFallback_ProducesNoIssues()
    {
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[0] = rounds[0] with
        {
            Track = new PackTrackRef { Id = BigTrack, Fallbacks = [FallbackTrack] },
        };
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        Assert.Empty(Validate(pack).Issues);
    }

    // ---------- item 3: opponents + 1 vs venue AI cap ----------

    [Fact]
    public void Validate_GridAtExactlyTheCap_IsFine()
    {
        // Cap 20: opponents 19 + player = 20 fits exactly.
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[0] = Round(1, "1967-01-02", opponents: 19);
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        Assert.Empty(Validate(pack).Issues);
    }

    [Fact]
    public void Validate_GridOneOverTheCap_IsAnError()
    {
        // Cap 20: opponents 20 + player = 21 does not fit.
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[0] = Round(1, "1967-01-02", opponents: 20);
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        AssertError(Validate(pack),
            "grid of 21 (opponents + player) exceeds Kyalami Historic's AI cap of 20");
    }

    [Fact]
    public void Validate_FallbackVenueBelowTheGrid_IsOnlyAWarning()
    {
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[0] = rounds[0] with
        {
            Track = new PackTrackRef { Id = BigTrack, Fallbacks = [TinyTrack] },
        };
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        var report = Validate(pack);
        AssertNoErrors(report);
        AssertWarning(report, "fallback Cascavel Dirt caps AI at 5, below the setupGuide grid of 18");
    }

    [Fact]
    public void Validate_RoundWithoutSetupGuide_SkipsTheCapCheck()
    {
        // The missing guide itself is the structural validator's finding, not ours.
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[0] = rounds[0] with { SetupGuide = null };
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        Assert.Empty(Validate(pack).Issues);
    }

    // ---------- item 4: livery bindings ----------

    [Fact]
    public void Validate_InstalledAndStockLiveries_ProduceNoWarnings()
    {
        // ValidPack binds one installed override name and one stock name.
        Assert.Empty(Validate(ValidPack()).Issues);
    }

    [Fact]
    public void Validate_MissingLivery_IsAWarningNamingTheRequiredSkinPack()
    {
        var pack = ValidPack() with
        {
            Entries =
            [
                Entry("driver.brabham", "1", InstalledLiveryName),
                Entry("driver.hulme", "2", "Lotus-Ford #6 J. Clark"),
            ],
        };

        var report = Validate(pack);
        AssertNoErrors(report);
        AssertWarning(report, "Livery 'Lotus-Ford #6 J. Clark' was not found");
        AssertWarning(report,
            "'F1 1967 Season (Alain Fry)' (https://www.overtake.gg/downloads/example)");
    }

    [Fact]
    public void Validate_LiveryCaseNearMiss_WarnsWithTheExactKnownName()
    {
        var pack = ValidPack() with
        {
            Entries =
            [
                Entry("driver.brabham", "1", InstalledLiveryName.ToUpperInvariant()),
                Entry("driver.hulme", "2", StockLiveryName),
            ],
        };

        var report = Validate(pack);
        AssertNoErrors(report);
        AssertWarning(report, $"does not exactly match installed/known livery '{InstalledLiveryName}'");
    }

    [Fact]
    public void Validate_GuestEntryLivery_IsCheckedToo()
    {
        var pack = ValidPack();
        var rounds = pack.Season.Rounds.ToArray();
        rounds[1] = rounds[1] with
        {
            GuestEntries =
            [
                new PackGuestEntry
                {
                    TeamId = "team.brabham",
                    DriverId = "driver.hulme",
                    Ams2LiveryName = "Guest Ghost Livery",
                },
            ],
        };
        pack = pack with { Season = pack.Season with { Rounds = rounds } };

        var report = Validate(pack);
        AssertNoErrors(report);
        AssertWarning(report, "Livery 'Guest Ghost Livery' was not found");
    }

    [Fact]
    public void Validate_NoLiveryReferenceDataAtAll_IsOneSummaryWarning()
    {
        // Class has no stock entry and nothing is installed: bindings are unverifiable —
        // one summary warning naming the skin packs instead of a flood of per-livery ones.
        var library = Library();
        library = new Ams2ContentLibrary
        {
            ExtractedFrom = library.ExtractedFrom,
            Classes = library.Classes,
            Vehicles = library.Vehicles,
            Tracks = library.Tracks,
            Liveries = new Dictionary<string, Ams2LiveryClassEntry>(StringComparer.Ordinal),
        };

        var report = Validate(ValidPack(), library, installed: []);
        AssertNoErrors(report);
        AssertWarning(report, "livery bindings cannot be verified");
        AssertWarning(report, "'F1 1967 Season (Alain Fry)'");
        Assert.Single(report.Issues);
    }

    // ---------- item 6 (content half): team cars ----------

    [Fact]
    public void Validate_UnknownVehicleId_IsAnError()
    {
        var pack = ValidPack() with
        {
            Teams =
            [
                new PackTeam
                {
                    Id = "team.brabham",
                    Name = "Brabham-Repco",
                    CarVehicleIds = [VintageCar, "lotus_49_ghost"],
                },
            ],
        };

        AssertError(Validate(pack),
            "Team 'team.brabham' car 'lotus_49_ghost' is not in the vehicle library");
    }

    [Fact]
    public void Validate_VehicleIdCasingNearMiss_NamesTheLibraryId()
    {
        var pack = ValidPack() with
        {
            Teams =
            [
                new PackTeam
                {
                    Id = "team.brabham",
                    Name = "Brabham-Repco",
                    CarVehicleIds = ["Formula_Vintage_G1M2"],
                },
            ],
        };

        AssertError(Validate(pack), $"differs from library vehicle id '{VintageCar}' in case");
    }

    [Fact]
    public void Validate_VehicleFromAnotherClass_IsAnError()
    {
        var pack = ValidPack() with
        {
            Teams =
            [
                new PackTeam
                {
                    Id = "team.brabham",
                    Name = "Brabham-Repco",
                    CarVehicleIds = [OtherClassCar],
                },
            ],
        };

        AssertError(Validate(pack),
            $"car '{OtherClassCar}' is in class 'CopaFusca', not the pack's ams2Class '{VintageClass}'");
    }
}
