using Companion.Core.Career;
using Companion.ViewModels;

namespace Companion.Tests.Career;

/// <summary>The display-only car-spec pipeline: the absent-tolerant <see cref="CarSpecCatalog"/>
/// (keyed by team or vehicle id, team wins) and the <see cref="CarSpecCardViewModel"/> projection.
/// Also checks the shipped data/rules/car-specs.json parses and covers the five SMGP car models.</summary>
public sealed class CarSpecTests
{
    private const string Sample = """
    {
      "barMax": 8,
      "cars": {
        "team.madonna":         { "machineName": "MADONNA", "engine": "V12", "maxPowerHp": 700,
                                  "bars": { "engine": 8, "transmission": 7, "suspension": 6, "tyre": 7, "brake": 8 } },
        "formula_classic_g3m2": { "machineName": "Type G3-M2",
                                  "bars": { "engine": 4, "transmission": 4, "suspension": 5, "tyre": 4, "brake": 4 } }
      }
    }
    """;

    [Fact]
    public void Catalog_ResolvesTeamIdOverVehicleId_AndBarMax()
    {
        var catalog = CarSpecCatalog.Parse(Sample);
        Assert.Equal(8, catalog.BarMax);

        // A team-id row wins over the car's vehicle-id row.
        var madonna = catalog.For("team.madonna", "formula_classic_g3m2");
        Assert.NotNull(madonna);
        Assert.Equal("MADONNA", madonna!.MachineName);
        Assert.Equal(700, madonna.MaxPowerHp);
        Assert.Equal(8, madonna.Bars.Engine);

        // No team row → falls back to the vehicle-id row.
        var byModel = catalog.For("team.unknown", "formula_classic_g3m2");
        Assert.NotNull(byModel);
        Assert.Equal("Type G3-M2", byModel!.MachineName);
    }

    [Fact]
    public void Catalog_IsAbsentTolerant()
    {
        Assert.Null(CarSpecCatalog.Empty.For("team.madonna", "formula_classic_g3m1"));
        Assert.Null(CarSpecCatalog.Parse(Sample).For("team.nope", "vehicle.nope"));
        Assert.Null(CarSpecCatalog.Parse(Sample).For(null, null));
        // A missing file loads the empty catalog rather than throwing.
        Assert.Same(CarSpecCatalog.Empty, CarSpecCatalog.Load(Path.Combine(Path.GetTempPath(), "no-such-rules-dir")));
    }

    [Fact]
    public void CardViewModel_FromNull_IsNull_AndFromSpec_HasTheFiveBarsAndSubheader()
    {
        Assert.Null(CarSpecCardViewModel.From(null, 8));

        var spec = new CarSpec
        {
            MachineName = "MP4/5B",
            Engine = "Honda V10",
            MaxPowerHp = 690,
            Bars = new CarSpecBars { Engine = 8, Transmission = 7, Suspension = 7, Tyre = 7, Brake = 8 },
        };
        var card = CarSpecCardViewModel.From(spec, 8)!;

        Assert.Equal("MP4/5B", card.MachineName);
        Assert.Contains("Honda V10", card.SubHeader);
        Assert.Contains("690 hp", card.SubHeader);
        Assert.Equal(["ENG", "TM", "SUS", "TIRE", "BRA"], card.Bars.Select(b => b.Label));
        Assert.Equal(8, card.Bars[0].Value);
        Assert.All(card.Bars, b => Assert.Equal(8, b.Max));

        // With neither engine nor power authored the subheader is empty (the row hides).
        var bare = CarSpecCardViewModel.From(spec with { Engine = "", MaxPowerHp = 0 }, 8)!;
        Assert.Equal("", bare.SubHeader);
    }

    [Fact]
    public void ShippedCarSpecs_ParseAndCoverTheFiveSmgpCarModels()
    {
        var catalog = CarSpecCatalog.Parse(CareerTestData.ReadRules("car-specs.json"));
        foreach (string vehicle in new[]
        {
            "formula_classic_g3m1", "formula_classic_g3m2", "formula_classic_g3m3",
            "formula_classic_g3m4", "mclaren_mp45b",
        })
        {
            var spec = catalog.For(null, vehicle);
            Assert.True(spec is not null, $"car-specs.json has no entry for '{vehicle}'.");
            Assert.False(string.IsNullOrWhiteSpace(spec!.MachineName));
            // Every bar sits within the catalog's 0..barMax scale.
            foreach (int bar in new[] { spec.Bars.Engine, spec.Bars.Transmission, spec.Bars.Suspension, spec.Bars.Tyre, spec.Bars.Brake })
                Assert.InRange(bar, 0, catalog.BarMax);
        }
    }
}
