using Companion.Ams2.ContentLibrary;
using Companion.Ams2.Preflight;
using Companion.Ams2.Skins;
using Companion.Core.Grid;
using Companion.Core.Packs;

namespace Companion.Tests.Ams2;

/// <summary>
/// The read-only skin picture (<see cref="SkinAssignmentResolver"/>): every grid seat resolved
/// to what it will actually show in AMS2 — a custom override skin, a stock livery, a NAMeS-only
/// default, or an unbound name — plus the player's-own-car crib. Mirrors the ground truth the
/// preflight validator checks: driver → livery_name(NAME) → installed skin.
/// </summary>
public class SkinAssignmentResolverTests
{
    private const string VehicleClass = "F-Vintage_Gen2";
    private const string VehicleDir = "brabham_bt26";

    [Fact]
    public void SeatWithMatchingOverride_IsCustomSkin_WithVehicleFolder()
    {
        var plan = PlanWith(Seat("Brabham #3", "J. Brabham"));
        var skins = new[] { Livery("Brabham #3", VehicleDir) };

        var result = SkinAssignmentResolver.Resolve(plan, skins, Library(), installedAiNames: null);

        var car = Assert.Single(result.Assignments);
        Assert.Equal(SkinStatus.CustomSkin, car.Status);
        Assert.Equal(VehicleDir, car.VehicleFolder);
        Assert.Equal(1, result.CustomSkinCount);
    }

    [Fact]
    public void SeatMatchingStockName_IsStockDefault()
    {
        var plan = PlanWith(Seat("Stock Car #1", "A. Driver"));
        var result = SkinAssignmentResolver.Resolve(
            plan, installedLiveries: [], LibraryWithStock("Stock Car #1"), installedAiNames: null);

        Assert.Equal(SkinStatus.StockDefault, Assert.Single(result.Assignments).Status);
    }

    [Fact]
    public void SeatInAiFileButNoSkin_IsNameOnly()
    {
        var plan = PlanWith(Seat("Dallara-Ford #21 G. Morbidelli", "G. Morbidelli"));
        var result = SkinAssignmentResolver.Resolve(
            plan, installedLiveries: [], Library(), AiNames("Dallara-Ford #21 G. Morbidelli"));

        Assert.Equal(SkinStatus.NameOnly, Assert.Single(result.Assignments).Status);
        Assert.Equal(1, result.DefaultSkinCount);
    }

    [Fact]
    public void SeatMatchingNothing_IsUnbound_NoNearMiss()
    {
        var plan = PlanWith(Seat("Nonexistent-Team #99 Nobody", "Nobody"));
        var result = SkinAssignmentResolver.Resolve(plan, installedLiveries: [], Library(), installedAiNames: null);

        var car = Assert.Single(result.Assignments);
        Assert.Equal(SkinStatus.Unbound, car.Status);
        Assert.Null(car.NearMiss);
        Assert.Equal(1, result.UnboundCount);
    }

    [Fact]
    public void UnboundSeatWithCaseOnlyDifference_SurfacesNearMiss()
    {
        // Pack livery differs only in case from an installed override — the classic authoring typo.
        var plan = PlanWith(Seat("brabham #3", "J. Brabham"));
        var skins = new[] { Livery("Brabham #3", VehicleDir) };

        var result = SkinAssignmentResolver.Resolve(plan, skins, Library(), installedAiNames: null);

        var car = Assert.Single(result.Assignments);
        Assert.Equal(SkinStatus.Unbound, car.Status);
        Assert.Equal("Brabham #3", car.NearMiss);
    }

    [Fact]
    public void PlayerSeat_IsSurfacedAsPlayerCar()
    {
        var plan = PlanWith(
            Seat("Brabham #3", "J. Brabham"),
            Seat("Lotus #1", "You", isPlayer: true));
        var skins = new[] { Livery("Brabham #3", VehicleDir), Livery("Lotus #1", VehicleDir) };

        var result = SkinAssignmentResolver.Resolve(plan, skins, Library(), installedAiNames: null);

        Assert.NotNull(result.PlayerCar);
        Assert.Equal("Lotus #1", result.PlayerCar!.LiveryName);
        Assert.True(result.PlayerCar.IsPlayer);
    }

    [Fact]
    public void OverrideInThisClassFolder_IsPreferredOverSameNameElsewhere()
    {
        var plan = PlanWith(Seat("Shared #7", "A. Driver"));
        // Same NAME on two cars; only one folder belongs to this class.
        var skins = new[]
        {
            Livery("Shared #7", "some_other_car"),
            Livery("Shared #7", VehicleDir),
        };

        var result = SkinAssignmentResolver.Resolve(plan, skins, Library(), installedAiNames: null);

        var car = Assert.Single(result.Assignments);
        Assert.Equal(SkinStatus.CustomSkin, car.Status);
        Assert.Equal(VehicleDir, car.VehicleFolder);
    }

    [Fact]
    public void Summary_CountsEachStatus()
    {
        var plan = PlanWith(
            Seat("Brabham #3", "A"),            // custom
            Seat("Stock Car #1", "B"),          // stock
            Seat("Nobody #99", "C"));           // unbound
        var skins = new[] { Livery("Brabham #3", VehicleDir) };

        var result = SkinAssignmentResolver.Resolve(plan, skins, LibraryWithStock("Stock Car #1"), installedAiNames: null);

        Assert.Equal(1, result.CustomSkinCount);
        Assert.Equal(1, result.DefaultSkinCount);
        Assert.Equal(1, result.UnboundCount);
        Assert.Contains("1 custom skin", result.Summary);
    }

    // ---------- helpers ----------

    private static GridSeat Seat(string livery, string driver, bool isPlayer = false) => new()
    {
        DriverId = "driver." + driver.Replace(" ", "").Replace(".", "").ToLowerInvariant(),
        DriverName = driver,
        TeamId = "team.x",
        TeamName = "Team X",
        Ams2LiveryName = livery,
        Ratings = new PackDriverRatings { RaceSkill = 0.8, QualifyingSkill = 0.8 },
        Reliability = 0.9,
        WeightScalar = 1.0,
        PowerScalar = 1.0,
        DragScalar = 1.0,
        IsPlayer = isPlayer,
    };

    private static GridPlan PlanWith(params GridSeat[] seats) => new()
    {
        PackId = "test",
        Year = 1969,
        SeriesName = "Formula One",
        Ams2Class = VehicleClass,
        Round = 1,
        RoundName = "Test GP",
        TrackId = "test_track",
        Seats = seats,
    };

    private static InstalledLivery Livery(string name, string folder) => new()
    {
        Name = name,
        VehicleFolder = folder,
        SourceFile = $@"Y:\...\Overrides\{folder}\{folder}.xml",
    };

    private static InstalledAiNameSet AiNames(params string[] names) => new()
    {
        VehicleClass = VehicleClass,
        LiveryNames = names,
        SourceFile = @"Y:\...\CustomAIDrivers\F-Vintage_Gen2.xml",
    };

    /// <summary>A library whose class maps to <see cref="VehicleDir"/> (so in-class disambiguation
    /// works) with no stock liveries.</summary>
    private static Ams2ContentLibrary Library() => Build(stockLiveries: []);

    private static Ams2ContentLibrary LibraryWithStock(params string[] stock) => Build(stock);

    private static Ams2ContentLibrary Build(IReadOnlyList<string> stockLiveries) => new()
    {
        Classes = new Dictionary<string, Ams2Class>(StringComparer.Ordinal)
        {
            [VehicleClass] = new Ams2Class { XmlName = VehicleClass },
        },
        Vehicles = new Dictionary<string, Ams2Vehicle>(StringComparer.Ordinal)
        {
            [VehicleDir] = new Ams2Vehicle { Id = VehicleDir, Dir = VehicleDir, VehicleClass = VehicleClass },
        },
        Tracks = new Dictionary<string, Ams2Track>(StringComparer.Ordinal),
        Liveries = new Dictionary<string, Ams2LiveryClassEntry>(StringComparer.Ordinal)
        {
            [VehicleClass] = new Ams2LiveryClassEntry { StockLib1563 = stockLiveries },
        },
        ExtractedFrom = "unit-test",
    };
}
