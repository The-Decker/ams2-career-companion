using Companion.Core.Numerics;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Settings;
using Companion.ViewModels.Standings;

namespace Companion.Tests.ViewModels;

/// <summary>
/// UX-round standings customization (contract section 2): sortable columns (click header,
/// click again to flip), the right-click column chooser persisting through the settings
/// seam, and tab memory across screen reopenings.
/// </summary>
public sealed class StandingsCustomizationTests
{
    // ---------- fixture: 3 drivers, 2 teams, 2 rounds, 9-6-4, best 1 of 2 ----------

    private static readonly (string Driver, string Team)[] Entrants =
    [
        ("d.alpha", "t.red"),
        ("d.bravo", "t.red"),
        ("d.zulu", "t.blue"),
    ];

    private static CatalogSeason Rules() => new()
    {
        RacePoints = [new(9), new(6), new(4)],
        DriversBestN = new CatalogBestN { WholeSeason = 1 },
        Constructors = new CatalogConstructors { BestCarOnly = true, BestN = "sameAsDrivers" },
    };

    private static RoundResult Round(int number, params string[] order) => new()
    {
        Round = number,
        Sessions =
        [
            new SessionResult
            {
                Kind = SessionKind.Race,
                Entries = order.Select((driver, i) => new ClassifiedEntry
                {
                    DriverId = driver,
                    ConstructorId = Entrants.Single(e => e.Driver == driver).Team,
                    Position = i + 1,
                }).ToArray(),
            },
        ],
    };

    /// <summary>Gross: alpha 15, bravo 10, zulu 8. Best-1 counted: alpha 9, bravo 6 (wait —
    /// see the asserts, which are computed from the ENGINE, not by hand).</summary>
    private static IReadOnlyList<StandingsSnapshot> Snapshots() =>
        StandingsEngine.ComputeSeason(
            Rules().ResolveScoringDefinition(2),
            [
                Round(1, "d.alpha", "d.bravo", "d.zulu"),
                Round(2, "d.alpha", "d.zulu", "d.bravo"),
            ]).Snapshots;

    private static SeasonPack Pack() => new()
    {
        Manifest = new PackManifest
        {
            PackId = "sorting-season",
            Name = "Sorting Season",
            Version = "1.0.0",
            FormatVersion = 1,
        },
        Season = new SeasonDefinition
        {
            Year = 1967,
            SeriesName = "Sort GP",
            Ams2Class = "TestClass",
            PointsSystem = Rules(),
            Rounds =
            [
                PackRoundFor(1),
                PackRoundFor(2),
            ],
        },
        Teams =
        [
            new PackTeam { Id = "t.red", Name = "Red", CarVehicleIds = ["car.x"] },
            new PackTeam { Id = "t.blue", Name = "Blue", CarVehicleIds = ["car.x"] },
        ],
        Drivers =
        [
            Driver("d.alpha", "Alpha Aa"),
            Driver("d.bravo", "Bravo Bb"),
            Driver("d.zulu", "Zulu Zz"),
        ],
        Entries = [],
    };

    private static PackRound PackRoundFor(int round) => new()
    {
        Round = round,
        Name = $"GP {round}",
        Date = "1967-05-07",
        Track = new PackTrackRef { Id = "interlagos" },
        Laps = 40,
    };

    private static PackDriver Driver(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Ratings = ResultEntryViewModelTests.Ratings,
    };

    private static (StandingsViewModel Vm, SettingsService Settings) Vm(
        AppSettings? initial = null)
    {
        var settings = new SettingsService(new InMemorySettingsStore(initial));
        return (new StandingsViewModel(Snapshots(), Pack(), settings), settings);
    }

    // ---------- sorting ----------

    [Fact]
    public void DefaultSort_IsChampionshipOrder()
    {
        var (vm, _) = Vm();
        var engineOrder = StandingsEngine.ComputeSeason(
                Rules().ResolveScoringDefinition(2),
                [Round(1, "d.alpha", "d.bravo", "d.zulu"), Round(2, "d.alpha", "d.zulu", "d.bravo")])
            .Final.Drivers.Select(d => d.DriverId);

        Assert.Equal(engineOrder, vm.DriverRows.Select(r => r.CompetitorId));
        Assert.Equal(StandingsViewModel.ColumnPosition, vm.SortColumn);
        Assert.False(vm.SortDescending);
    }

    [Fact]
    public void ClickName_SortsAlphabetically_ClickAgainFlips()
    {
        var (vm, _) = Vm();

        vm.SortByCommand.Execute(StandingsViewModel.ColumnName);
        Assert.Equal(
            new[] { "Alpha Aa", "Bravo Bb", "Zulu Zz" },
            vm.DriverRows.Select(r => r.DisplayName));

        vm.SortByCommand.Execute(StandingsViewModel.ColumnName);
        Assert.Equal(
            new[] { "Zulu Zz", "Bravo Bb", "Alpha Aa" },
            vm.DriverRows.Select(r => r.DisplayName));
        Assert.True(vm.SortDescending);
    }

    [Fact]
    public void ClickCounted_StartsDescending_BiggestFirst()
    {
        var (vm, _) = Vm();

        vm.SortByCommand.Execute(StandingsViewModel.ColumnCounted);

        Assert.True(vm.SortDescending);
        var counted = vm.DriverRows.Select(r => r.CountedValue).ToArray();
        Assert.Equal(counted.OrderByDescending(v => v), counted);
    }

    [Fact]
    public void ClickGross_SortsByGross_NotCounted()
    {
        var (vm, _) = Vm();

        vm.SortByCommand.Execute(StandingsViewModel.ColumnGross);

        var gross = vm.DriverRows.Select(r => r.GrossValue).ToArray();
        Assert.Equal(gross.OrderByDescending(v => v), gross);
    }

    [Fact]
    public void SortAppliesToConstructorsTabToo()
    {
        var (vm, _) = Vm();

        vm.SortByCommand.Execute(StandingsViewModel.ColumnName);

        Assert.Equal(new[] { "Blue", "Red" }, vm.ConstructorRows.Select(r => r.DisplayName));
    }

    [Fact]
    public void HeaderTexts_CarryTheSortGlyph()
    {
        var (vm, _) = Vm();
        Assert.Equal("Pos ▲", vm.PositionHeader);
        Assert.Equal("Points", vm.CountedHeader);

        vm.SortByCommand.Execute(StandingsViewModel.ColumnCounted);

        Assert.Equal("Pos", vm.PositionHeader);
        Assert.Equal("Points ▼", vm.CountedHeader);
    }

    [Fact]
    public void UnknownColumn_IsIgnored()
    {
        var (vm, _) = Vm();
        vm.SortByCommand.Execute("nonsense");
        Assert.Equal(StandingsViewModel.ColumnPosition, vm.SortColumn);
    }

    [Fact]
    public void PerRoundValues_AreCountedDividedByAppliedRounds()
    {
        var (vm, _) = Vm();

        foreach (var row in vm.DriverRows)
            Assert.Equal(row.CountedValue / 2, row.PerRoundValue, precision: 10);
    }

    // ---------- column chooser persistence ----------

    [Fact]
    public void ColumnToggles_PersistThroughTheSettingsSeam()
    {
        var (vm, settings) = Vm();
        Assert.True(vm.ShowGrossColumn);   // defaults
        Assert.False(vm.ShowPerRoundColumn);

        vm.ShowGrossColumn = false;
        vm.ShowPerRoundColumn = true;
        vm.ShowDroppedColumn = false;

        var columns = settings.Current.StandingsColumns;
        Assert.False(columns.ShowGross);
        Assert.True(columns.ShowPerRound);
        Assert.False(columns.ShowDropped);
        Assert.True(columns.ShowCounted); // untouched

        // A NEW screen (same seam) reopens with the chosen columns, persistence.
        var reopened = new StandingsViewModel(Snapshots(), Pack(), settings);
        Assert.False(reopened.ShowGrossColumn);
        Assert.True(reopened.ShowPerRoundColumn);
        Assert.False(reopened.ShowDroppedColumn);
        Assert.True(reopened.ShowCountedColumn);
    }

    [Fact]
    public void ColumnToggle_NoOpWhenUnchanged_DoesNotTouchTheStore()
    {
        var store = new InMemorySettingsStore();
        var settings = new SettingsService(store);
        var vm = new StandingsViewModel(Snapshots(), Pack(), settings);

        vm.ShowGrossColumn = vm.ShowGrossColumn; // same value

        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void WithoutASettingsSeam_TogglesStillWorkInMemory()
    {
        var vm = new StandingsViewModel(Snapshots(), Pack());
        vm.ShowGrossColumn = false;
        Assert.False(vm.ShowGrossColumn);
    }

    // ---------- tab memory ----------

    [Fact]
    public void SelectedTab_PersistsAndRestores()
    {
        var (vm, settings) = Vm();
        Assert.Equal(0, vm.SelectedTabIndex);

        vm.SelectedTabIndex = 2; // round matrix

        Assert.Equal(2, settings.Current.StandingsTabIndex);
        var reopened = new StandingsViewModel(Snapshots(), Pack(), settings);
        Assert.Equal(2, reopened.SelectedTabIndex);
    }

    [Fact]
    public void RememberedConstructorsTab_DegradesToDrivers_WhenSeasonHasNone()
    {
        var settings = new SettingsService(new InMemorySettingsStore(
            new AppSettings { StandingsTabIndex = 1 }));
        var rules = Rules() with { Constructors = null };
        var snapshots = StandingsEngine.ComputeSeason(
            rules.ResolveScoringDefinition(2),
            [Round(1, "d.alpha", "d.bravo", "d.zulu")]).Snapshots;
        var pack = Pack();
        pack = pack with
        {
            Season = pack.Season with { PointsSystem = rules },
        };

        var vm = new StandingsViewModel(snapshots, pack, settings);

        Assert.False(vm.HasConstructors);
        Assert.Equal(0, vm.SelectedTabIndex);
        // The degraded index must land on a tab this season actually shows (never a hidden one)
        // so the TabControl never opens on an invisible tab and blanks the screen.
        Assert.Contains(vm.Tabs, t => t.Index == vm.SelectedTabIndex);
    }

    [Fact]
    public void RememberedRoundMatrixTab_SurvivesInAConstructorsSeason()
    {
        // Index 2 (round matrix) is a real tab here, it must be restored, not clamped away.
        var settings = new SettingsService(new InMemorySettingsStore(
            new AppSettings { StandingsTabIndex = 2 }));

        var vm = new StandingsViewModel(Snapshots(), Pack(), settings);

        Assert.Equal(2, vm.SelectedTabIndex);
        Assert.Contains(vm.Tabs, t => t.Kind == StandingsTabKind.Matrix && t.Index == 2);
    }

    [Fact]
    public void OutOfRangeSavedTab_SnapsToAShownTab()
    {
        // A hand-edited settings file (Normalized clamps 0..2, but be defensive at the VM too)
        // must never leave SelectedTabIndex pointing at a tab that is not shown.
        var settings = new SettingsService(new InMemorySettingsStore(
            new AppSettings { StandingsTabIndex = 2 }));
        var rules = Rules() with { Constructors = null }; // no constructors → index 1 absent
        var snapshots = StandingsEngine.ComputeSeason(
            rules.ResolveScoringDefinition(2),
            [Round(1, "d.alpha", "d.bravo", "d.zulu")]).Snapshots;
        var pack = Pack() with { Season = Pack().Season with { PointsSystem = rules } };

        var vm = new StandingsViewModel(snapshots, pack, settings);

        // Index 2 (matrix) IS shown even without constructors, so it is honored.
        Assert.Equal(2, vm.SelectedTabIndex);
        Assert.Contains(vm.Tabs, t => t.Index == vm.SelectedTabIndex);
    }
}
