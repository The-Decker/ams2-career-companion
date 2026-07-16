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
        Assert.Equal("driver.jbrabham", car.DriverId);
        Assert.Equal("team.x", car.TeamId);
        Assert.Equal("51", car.SkinSlot);
        Assert.Equal(VehicleDir, car.VehicleFolder);
        Assert.Equal("51", result.ActiveLiverySlots["Brabham #3"]);
        Assert.Equal(VehicleDir, result.ActiveCustomLiveryModels["Brabham #3"]);
        Assert.Equal(1, result.CustomSkinCount);
    }

    [Fact]
    public void SeatWithPlaceholderOverride_IsInstalledInactive_NotCustomSkin()
    {
        // Installed on disk as a "##" placeholder (no real slot) → NOT active in-game (the Skoal #10 bug).
        var plan = PlanWith(Seat("Skoal #10", "K. Acheson"));
        var skins = new[] { Livery("Skoal #10", VehicleDir, slot: "##") };

        var result = SkinAssignmentResolver.Resolve(plan, skins, Library(), installedAiNames: null);

        var car = Assert.Single(result.Assignments);
        Assert.Equal(SkinStatus.InstalledInactive, car.Status);
        Assert.Equal("", car.SkinSlot);
        Assert.Equal(1, result.InactiveCount);
        Assert.Contains("Skoal #10", result.InactiveLiveries);
        Assert.DoesNotContain("Skoal #10", result.ActiveLiveries);
    }

    [Fact]
    public void ActiveSlot_IsPreferredOverPlaceholder_ForTheSameName()
    {
        // The same NAME shipped both as an active slot AND a placeholder — the active one wins.
        var plan = PlanWith(Seat("Skoal #9", "P. Alliot"));
        var skins = new[]
        {
            Livery("Skoal #9", VehicleDir, slot: "##"),
            Livery("Skoal #9", VehicleDir, slot: "53"),
        };

        var result = SkinAssignmentResolver.Resolve(plan, skins, Library(), installedAiNames: null);

        Assert.Equal(SkinStatus.CustomSkin, Assert.Single(result.Assignments).Status);
        Assert.Contains("Skoal #9", result.ActiveLiveries);
    }

    [Fact]
    public void SeatMatchingStockName_IsStockDefault()
    {
        var plan = PlanWith(Seat("Stock Car #1", "A. Driver"));
        var result = SkinAssignmentResolver.Resolve(
            plan, installedLiveries: [], LibraryWithStock("Stock Car #1"), installedAiNames: null);

        Assert.Equal(SkinStatus.StockDefault, Assert.Single(result.Assignments).Status);
        Assert.Empty(result.ActiveCustomLiveryModels);
    }

    [Fact]
    public void OfficialStockLivery_PublishesItsRealSlot()
    {
        var plan = PlanWith(Seat("Stock Car #1", "A. Driver"));
        var result = SkinAssignmentResolver.Resolve(
            plan,
            installedLiveries: [],
            Build(
                stockLiveries: [],
                officialLiveries: [new OfficialLivery { Name = "Stock Car #1", Slot = 17 }]),
            installedAiNames: null);

        var car = Assert.Single(result.Assignments);
        Assert.Equal(SkinStatus.StockDefault, car.Status);
        Assert.Equal("17", car.SkinSlot);
        Assert.Equal("17", result.ActiveLiverySlots["Stock Car #1"]);
        Assert.DoesNotContain("Stock Car #1", result.ActiveCustomLiveryModels);
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

    private static InstalledLivery Livery(string name, string folder, string slot = "51") => new()
    {
        Name = name,
        VehicleFolder = folder,
        SourceFile = $@"Y:\...\Overrides\{folder}\{folder}.xml",
        Slot = slot,
    };

    private static InstalledAiNameSet AiNames(params string[] names) => new()
    {
        VehicleClass = VehicleClass,
        LiveryNames = names,
        SourceFile = @"Y:\...\CustomAIDrivers\F-Vintage_Gen2.xml",
    };

    [Fact]
    public void LiveryCap_IsSetFromTheLibrary()
    {
        var plan = PlanWith(Seat("Brabham #3", "A"));
        var result = SkinAssignmentResolver.Resolve(plan, [], Build([], cap: 24), installedAiNames: null);
        Assert.Equal(24, result.LiveryCap);
    }

    [Fact]
    public void ExceedsCap_WhenGridNeedsMoreDistinctLiveriesThanTheClassAllows()
    {
        // 3 distinct liveries on the grid, class caps at 2 → the field can't be fully represented.
        var plan = PlanWith(Seat("Car #1", "A"), Seat("Car #2", "B"), Seat("Car #3", "C"));
        var result = SkinAssignmentResolver.Resolve(plan, [], Build([], cap: 2), installedAiNames: null);

        Assert.True(result.ExceedsCap);
        Assert.Equal(3, result.DistinctLiveriesOnGrid);
    }

    [Fact]
    public void DoesNotExceedCap_WhenGridFitsOrCapUnknown()
    {
        var plan = PlanWith(Seat("Car #1", "A"), Seat("Car #2", "B"));
        Assert.False(SkinAssignmentResolver.Resolve(plan, [], Build([], cap: 2), installedAiNames: null).ExceedsCap);
        Assert.False(SkinAssignmentResolver.Resolve(plan, [], Build([], cap: null), installedAiNames: null).ExceedsCap);
    }

    /// <summary>A library whose class maps to <see cref="VehicleDir"/> (so in-class disambiguation
    /// works) with no stock liveries.</summary>
    private static Ams2ContentLibrary Library() => Build(stockLiveries: []);

    private static Ams2ContentLibrary LibraryWithStock(params string[] stock) => Build(stock);

    private static Ams2ContentLibrary Build(
        IReadOnlyList<string> stockLiveries,
        int? cap = null,
        IReadOnlyList<OfficialLivery>? officialLiveries = null) => new()
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
        LiveryCaps = cap is { } c
            ? new Dictionary<string, int>(StringComparer.Ordinal) { [VehicleClass] = c }
            : new Dictionary<string, int>(StringComparer.Ordinal),
        OfficialLiveries = officialLiveries is null
            ? new Dictionary<string, IReadOnlyList<OfficialLivery>>(StringComparer.Ordinal)
            : new Dictionary<string, IReadOnlyList<OfficialLivery>>(StringComparer.Ordinal)
            {
                [VehicleClass] = officialLiveries,
            },
        ExtractedFrom = "unit-test",
    };
}
