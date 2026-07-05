using Companion.Core.Numerics;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Standings;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Standings screen viewmodel against REAL StandingsEngine output: gross vs counted for a
/// best-N season, dropped markers, the round matrix matching the engine's per-round scores,
/// constructor name resolution, and the rules-explainer chip from the pack's CatalogSeason.
/// </summary>
public class StandingsViewModelTests
{
    // ---------- season fixture: 4 drivers, 2 teams, 3 rounds, 9-6-4-3, best 2 of 3 ----------

    private static readonly (string Driver, string Team)[] Entrants =
    [
        ("d1", "t.lotus"),
        ("d2", "t.lotus"),
        ("d3", "t.ferrari"),
        ("d4", "t.ferrari"),
    ];

    private static CatalogSeason Rules() => new()
    {
        RacePoints = [new(9), new(6), new(4), new(3)],
        DriversBestN = new CatalogBestN { WholeSeason = 2 },
        Constructors = new CatalogConstructors { BestCarOnly = true, BestN = "sameAsDrivers" },
        FastestLap = new FastestLapRule { Points = Rational.One },
        SharedDrivePolicy = SharedDrivePolicy.Zero,
    };

    private static RoundResult Round(int number, params string[] finishingOrder) => new()
    {
        Round = number,
        Sessions =
        [
            new SessionResult
            {
                Kind = SessionKind.Race,
                Entries = finishingOrder
                    .Select((driver, i) => new ClassifiedEntry
                    {
                        DriverId = driver,
                        ConstructorId = Entrants.Single(e => e.Driver == driver).Team,
                        Position = i + 1,
                    })
                    .ToArray(),
            },
        ],
    };

    /// <summary>d1 wins R1, d2 wins R2, d3 wins R3; d4 is always fourth.
    /// Gross: d1 21, d2 19, d3 17, d4 9. Best-2 counted: d1 15, d2 15, d3 13, d4 6.</summary>
    private static SeasonStandingsResult Engine() => StandingsEngine.ComputeSeason(
        Rules().ResolveScoringDefinition(3),
        [
            Round(1, "d1", "d2", "d3", "d4"),
            Round(2, "d2", "d1", "d3", "d4"),
            Round(3, "d3", "d1", "d2", "d4"),
        ]);

    private static SeasonPack Pack(CatalogSeason? rules = null, int championshipRounds = 3) => new()
    {
        Manifest = new PackManifest
        {
            PackId = "test-season",
            Name = "Test Season",
            Version = "1.0.0",
            FormatVersion = 1,
        },
        Season = new SeasonDefinition
        {
            Year = 1967,
            SeriesName = "Test GP Series",
            Ams2Class = "TestClass",
            PointsSystem = rules ?? Rules(),
            Rounds = Enumerable.Range(1, championshipRounds)
                .Select(PackRoundFor)
                // A non-championship event on the calendar must not inflate best-N text.
                .Append(PackRoundFor(championshipRounds + 1) with { Championship = false })
                .ToArray(),
        },
        Teams =
        [
            Team("t.lotus", "Lotus"),
            Team("t.ferrari", "Ferrari"),
        ],
        // d4 deliberately missing: display must fall back to the raw id.
        Drivers =
        [
            Driver("d1", "Jim Clark"),
            Driver("d2", "Graham Hill"),
            Driver("d3", "Chris Amon"),
        ],
        Entries = [],
    };

    private static PackRound PackRoundFor(int round) => new()
    {
        Round = round,
        Name = $"Grand Prix {round}",
        Date = "1967-05-07",
        Track = new PackTrackRef { Id = "interlagos" },
        Laps = 40,
    };

    private static PackTeam Team(string id, string name) => new()
    {
        Id = id,
        Name = name,
        CarVehicleIds = ["car.test"],
    };

    private static PackDriver Driver(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Ratings = ResultEntryViewModelTests.Ratings,
    };

    private static StandingsViewModel Vm() => new(Engine().Snapshots, Pack());

    // ---------- drivers tab: gross vs counted + dropped markers ----------

    [Fact]
    public void DriverRows_ShowGrossVsCountedForABestNSeason()
    {
        var vm = Vm();

        Assert.Equal(
            new[]
            {
                ("1", "d1", "15", "21"),
                ("2", "d2", "15", "19"),
                ("3", "d3", "13", "17"),
                ("4", "d4", "6", "9"),
            },
            vm.DriverRows.Select(r => (r.PositionText, r.CompetitorId, r.CountedText, r.GrossText)));

        Assert.All(vm.DriverRows, r => Assert.True(r.ShowGross)); // every driver dropped something
    }

    [Fact]
    public void DriverRows_CarryDroppedRoundMarkers()
    {
        var vm = Vm();

        var d1 = vm.DriverRows.Single(r => r.CompetitorId == "d1");
        Assert.True(d1.HasDroppedRounds);
        Assert.Equal("R3", d1.DroppedRoundsText); // 9 + 6 kept, the later 6 dropped

        var d3 = vm.DriverRows.Single(r => r.CompetitorId == "d3");
        Assert.Equal("R2", d3.DroppedRoundsText); // 9 (R3) + 4 (R1) kept
    }

    [Fact]
    public void DriverRows_MatchTheEngineExactly()
    {
        var final = Engine().Final;
        var vm = Vm();

        Assert.Equal(final.Drivers.Count, vm.DriverRows.Count);
        foreach (var (standing, row) in final.Drivers.Zip(vm.DriverRows))
        {
            Assert.Equal(standing.DriverId, row.CompetitorId);
            Assert.Equal(standing.CountedPoints.ToString(), row.CountedText);
            Assert.Equal(standing.GrossPoints.ToString(), row.GrossText);
            Assert.Equal(standing.Dropped.Count > 0, row.HasDroppedRounds);
        }
    }

    [Fact]
    public void DriverRows_ResolveNamesFromThePack_FallingBackToTheId()
    {
        var vm = Vm();

        Assert.Equal("Jim Clark", vm.DriverRows.Single(r => r.CompetitorId == "d1").DisplayName);
        Assert.Equal("d4", vm.DriverRows.Single(r => r.CompetitorId == "d4").DisplayName);
    }

    // ---------- constructors tab ----------

    [Fact]
    public void ConstructorRows_FromTheLatestSnapshot_WithPackTeamNames()
    {
        var final = Engine().Final;
        var vm = Vm();

        Assert.True(vm.HasConstructors);
        Assert.Equal(final.Constructors!.Count, vm.ConstructorRows.Count);
        foreach (var (standing, row) in final.Constructors!.Zip(vm.ConstructorRows))
        {
            Assert.Equal(standing.ConstructorId, row.CompetitorId);
            Assert.Equal(standing.CountedPoints.ToString(), row.CountedText);
            Assert.Equal(standing.GrossPoints.ToString(), row.GrossText);
        }

        Assert.Equal("Lotus", vm.ConstructorRows.Single(r => r.CompetitorId == "t.lotus").DisplayName);
        Assert.Equal("Ferrari", vm.ConstructorRows.Single(r => r.CompetitorId == "t.ferrari").DisplayName);
    }

    // ---------- tab model (all applicable tabs present + selectable) ----------

    [Fact]
    public void AfterARound_AConstructorsSeason_ShowsAllThreeTabs_EachSelectable()
    {
        // A round has been applied to a season WITH a constructors championship (1967-style):
        // Drivers, Constructors and Round matrix must all be present and reachable — the exact
        // regression Mike hit where only Drivers showed after entering a round.
        var vm = Vm();

        Assert.True(vm.HasConstructors);
        Assert.Equal(
            new[] { StandingsTabKind.Drivers, StandingsTabKind.Constructors, StandingsTabKind.Matrix },
            vm.Tabs.Select(t => t.Kind));
        Assert.Equal(new[] { "Drivers", "Constructors", "Round matrix" }, vm.Tabs.Select(t => t.Header));
        Assert.True(vm.ShowDriversTab);
        Assert.True(vm.ShowConstructorsTab);
        Assert.True(vm.ShowMatrixTab);

        // Every shown tab is selectable by index (the same SelectedTabIndex that both the
        // mouse click and the keyboard drive), and lands on the tab it names.
        foreach (var tab in vm.Tabs)
        {
            vm.SelectedTabIndex = tab.Index;
            Assert.Equal(tab.Index, vm.SelectedTabIndex);
        }
    }

    [Fact]
    public void ConstructorsTab_HiddenOnlyWhenTheSeasonHasNoConstructorsChampionship()
    {
        // With a constructors championship the tab is present...
        Assert.Contains(Vm().Tabs, t => t.Kind == StandingsTabKind.Constructors);

        // ...and only a season that genuinely has none (pre-1958) hides it, while Drivers and
        // Round matrix stay present and selectable.
        var rules = Rules() with { Constructors = null };
        var result = StandingsEngine.ComputeSeason(
            rules.ResolveScoringDefinition(3),
            [Round(1, "d1", "d2", "d3", "d4")]);
        var vm = new StandingsViewModel(result.Snapshots, Pack(rules));

        Assert.False(vm.ShowConstructorsTab);
        Assert.DoesNotContain(vm.Tabs, t => t.Kind == StandingsTabKind.Constructors);
        Assert.Equal(
            new[] { StandingsTabKind.Drivers, StandingsTabKind.Matrix },
            vm.Tabs.Select(t => t.Kind));
    }

    [Fact]
    public void BeforeAnyRound_NoTabsAreShown()
    {
        // The whole table area is hidden until the first result; no tab claims to be present.
        var vm = new StandingsViewModel([], Pack());

        Assert.Empty(vm.Tabs);
        Assert.False(vm.ShowDriversTab);
        Assert.False(vm.ShowConstructorsTab);
        Assert.False(vm.ShowMatrixTab);
    }

    // ---------- round matrix ----------

    [Fact]
    public void Matrix_CellsEqualTheEnginePerRoundScores()
    {
        var final = Engine().Final;
        var vm = Vm();

        Assert.Equal(new[] { "R1", "R2", "R3" }, vm.RoundHeaders);
        Assert.Equal(final.Drivers.Count, vm.MatrixRows.Count);

        foreach (var row in vm.MatrixRows)
        {
            var standing = final.Drivers.Single(d => d.DriverId == row.DriverId);
            Assert.Equal(3, row.Cells.Count);
            for (int round = 1; round <= 3; round++)
            {
                var score = standing.RoundScores.Single(rs => rs.Round == round);
                Assert.Equal(score.Points.ToString(), row.Cells[round - 1].Text);
            }
        }
    }

    [Fact]
    public void Matrix_HandComputedSpotChecks()
    {
        var vm = Vm();

        var d1 = vm.MatrixRows.Single(r => r.DriverId == "d1");
        Assert.Equal(new[] { "9", "6", "6" }, d1.Cells.Select(c => c.Text));

        var d4 = vm.MatrixRows.Single(r => r.DriverId == "d4");
        Assert.Equal(new[] { "3", "3", "3" }, d4.Cells.Select(c => c.Text));
    }

    [Fact]
    public void Matrix_FlagsDroppedCells_FromTheLatestSnapshot()
    {
        var vm = Vm();

        var d1 = vm.MatrixRows.Single(r => r.DriverId == "d1");
        Assert.Equal(new[] { false, false, true }, d1.Cells.Select(c => c.IsDropped));

        var d3 = vm.MatrixRows.Single(r => r.DriverId == "d3");
        Assert.Equal(new[] { false, true, false }, d3.Cells.Select(c => c.IsDropped));
    }

    [Fact]
    public void Matrix_RowsFollowTheDriversTabOrder()
    {
        var vm = Vm();
        Assert.Equal(
            vm.DriverRows.Select(r => r.CompetitorId),
            vm.MatrixRows.Select(r => r.DriverId));
    }

    [Fact]
    public void Matrix_DriverAbsentFromARound_GetsAnEmptyCell()
    {
        // d4 skips round 2 entirely.
        var result = StandingsEngine.ComputeSeason(
            Rules().ResolveScoringDefinition(3),
            [
                Round(1, "d1", "d2", "d3", "d4"),
                Round(2, "d2", "d1", "d3"),
                Round(3, "d3", "d1", "d2", "d4"),
            ]);

        var vm = new StandingsViewModel(result.Snapshots, Pack());

        var d4 = vm.MatrixRows.Single(r => r.DriverId == "d4");
        Assert.Equal(new[] { "3", "", "3" }, d4.Cells.Select(c => c.Text));
    }

    [Fact]
    public void MidSeason_MatrixOnlyHasColumnsForAppliedRounds()
    {
        var result = StandingsEngine.ComputeSeason(
            Rules().ResolveScoringDefinition(3),
            [Round(1, "d1", "d2", "d3", "d4")]);

        var vm = new StandingsViewModel(result.Snapshots, Pack());

        Assert.Equal(new[] { "R1" }, vm.RoundHeaders);
        Assert.All(vm.MatrixRows, r => Assert.Single(r.Cells));
        // No drops yet with a single round: gross equals counted.
        Assert.All(vm.DriverRows, r => Assert.False(r.ShowGross));
    }

    // ---------- rules chip ----------

    [Fact]
    public void RulesChip_DescribesPointsBestNSharedDriveAndFastestLap()
    {
        var vm = Vm();

        Assert.Equal(
            new[]
            {
                "Points 9-6-4-3",
                "best 2 of 3 results count",
                "shared drives score no points",
                "fastest lap +1",
            },
            vm.RulesParts);
        Assert.Equal(
            "Points 9-6-4-3 · best 2 of 3 results count · shared drives score no points · fastest lap +1",
            vm.RulesChipText);
    }

    [Fact]
    public void RulesChip_SplitPolicy_TopTenFastestLap_AndNoBestN()
    {
        var rules = Rules() with
        {
            DriversBestN = null,
            SharedDrivePolicy = SharedDrivePolicy.Split,
            FastestLap = new FastestLapRule
            {
                Points = Rational.One,
                Eligibility = FastestLapEligibility.ClassifiedTopTen,
            },
        };

        var vm = new StandingsViewModel([], Pack(rules));

        Assert.Contains("all rounds count", vm.RulesParts);
        Assert.Contains("shared drives split points", vm.RulesParts);
        Assert.Contains("fastest lap +1 (top 10 only)", vm.RulesParts);
    }

    [Fact]
    public void RulesChip_SplitSeasonBestN_DescribesBothSegments()
    {
        var rules = Rules() with
        {
            DriversBestN = new CatalogBestN
            {
                Split = new CatalogSplitSeason
                {
                    FirstRounds = 6, FirstCount = 5, SecondRounds = 5, SecondCount = 4,
                },
            },
        };

        var vm = new StandingsViewModel([], Pack(rules, championshipRounds: 11));

        Assert.Contains("best 5 of rounds 1–6 + best 4 of rounds 7–11 count", vm.RulesParts);
    }

    [Fact]
    public void RulesChip_NoFastestLapRule()
    {
        var vm = new StandingsViewModel([], Pack(Rules() with { FastestLap = null }));
        Assert.Contains("no fastest-lap point", vm.RulesParts);
    }

    // ---------- degenerate inputs ----------

    [Fact]
    public void BeforeRoundOne_NoSnapshots_EverythingEmptyButTheChipStillExplains()
    {
        var vm = new StandingsViewModel([], Pack());

        Assert.Empty(vm.DriverRows);
        Assert.Empty(vm.ConstructorRows);
        Assert.Empty(vm.MatrixRows);
        Assert.Empty(vm.RoundHeaders);
        Assert.True(vm.HasConstructors); // known from the pack, not the snapshots
        Assert.NotEmpty(vm.RulesChipText);
    }

    [Fact]
    public void SeasonWithoutConstructorsChampionship_HidesTheTab()
    {
        var rules = Rules() with { Constructors = null };
        var result = StandingsEngine.ComputeSeason(
            rules.ResolveScoringDefinition(3),
            [Round(1, "d1", "d2", "d3", "d4")]);
        Assert.Null(result.Snapshots[0].Constructors);

        var vm = new StandingsViewModel(result.Snapshots, Pack(rules));

        Assert.False(vm.HasConstructors);
        Assert.Empty(vm.ConstructorRows);
        Assert.NotEmpty(vm.DriverRows);
    }

    [Fact]
    public void SnapshotsArriveUnordered_LatestStillWins()
    {
        var snapshots = Engine().Snapshots;
        var shuffled = new[] { snapshots[2], snapshots[0], snapshots[1] };

        var vm = new StandingsViewModel(shuffled, Pack());

        Assert.Equal(new[] { "R1", "R2", "R3" }, vm.RoundHeaders);
        Assert.Equal("15", vm.DriverRows[0].CountedText); // final counted points, not R2's
    }
}
