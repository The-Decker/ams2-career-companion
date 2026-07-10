using Companion.Ams2.CustomAi;
using Companion.Ams2.Grid;
using Companion.Core.Grid;
using Companion.Core.Packs;

namespace Companion.Tests.Grid;

/// <summary>
/// The v1.3 juppo schema: per-driver CAR tuning (weight/power/drag scalars + reliability) and the
/// setup-preference pair (setupDownforce/Randomness). All STAGING-ONLY — the resolved seat carries
/// them beside the team values the sim keeps reading, and only the staged custom-AI file prefers
/// them. Base blocks live on drivers.json "car"; per-round aiOverrides may patch every field.
/// </summary>
public sealed class CarTuningTests
{
    private static SeasonPack PackWith(
        PackDriverCar? car = null,
        IReadOnlyDictionary<string, PackRatingsPatch>? aiOverrides = null,
        PackDriverRatings? ratings = null)
    {
        var driver = GridTestData.Driver("driver.a", "Alpha", ratings) with { Car = car };
        return GridTestData.Pack(
            [GridTestData.Team("team.t", "Test Team", reliability: 0.90,
                weightScalar: 1.0, powerScalar: 1.0, dragScalar: 1.0)],
            [driver],
            [GridTestData.Entry("team.t", "driver.a", "1", "1", "Livery A")],
            [GridTestData.Round(1, aiOverrides: aiOverrides)]);
    }

    [Fact]
    public void Resolve_MergesDriverCarWithTheRoundPatch_PatchWinsPerField()
    {
        var pack = PackWith(
            car: new PackDriverCar { WeightScalar = 0.97, PowerScalar = 1.0, DragScalar = 1.0, VehicleReliability = 0.48 },
            aiOverrides: new Dictionary<string, PackRatingsPatch>
            {
                ["driver.a"] = new() { WeightScalar = 0.95, PowerScalar = 1.02 },
            });

        var seat = Assert.Single(RoundGridResolver.Resolve(pack, 1).Seats);

        Assert.NotNull(seat.CarTuning);
        Assert.Equal(0.95, seat.CarTuning!.WeightScalar);      // patch wins
        Assert.Equal(1.02, seat.CarTuning.PowerScalar);        // patch wins
        Assert.Equal(1.0, seat.CarTuning.DragScalar);          // from the driver car block
        Assert.Equal(0.48, seat.CarTuning.VehicleReliability); // from the driver car block
        // The TEAM values the sim reads are untouched (sim-inert).
        Assert.Equal(1.0, seat.WeightScalar);
        Assert.Equal(0.90, seat.Reliability);
    }

    [Fact]
    public void Resolve_NoCarDataAnywhere_LeavesCarTuningNull()
    {
        var seat = Assert.Single(RoundGridResolver.Resolve(PackWith(), 1).Seats);
        Assert.Null(seat.CarTuning);
    }

    [Fact]
    public void Resolve_SetupDownforceRidesTheRatings_AndTheRoundPatch()
    {
        var pack = PackWith(
            ratings: GridTestData.Ratings() with { SetupDownforce = 0.60, SetupDownforceRandomness = 0.10 },
            aiOverrides: new Dictionary<string, PackRatingsPatch>
            {
                ["driver.a"] = new() { SetupDownforce = 0.45 },
            });

        var seat = Assert.Single(RoundGridResolver.Resolve(pack, 1).Seats);

        Assert.Equal(0.45, seat.Ratings.SetupDownforce);           // patch wins
        Assert.Equal(0.10, seat.Ratings.SetupDownforceRandomness); // baseline kept
    }

    [Fact]
    public void Stager_PrefersCarTuningOverTeamValues_AndEmitsTheSetupPair()
    {
        var pack = PackWith(
            car: new PackDriverCar { WeightScalar = 0.97, PowerScalar = 1.02, VehicleReliability = 0.45 },
            ratings: GridTestData.Ratings() with { SetupDownforce = 0.60, SetupDownforceRandomness = 0.10 });
        var plan = RoundGridResolver.Resolve(pack, 1);

        var file = GridStager.Build(plan, "test");
        var driver = Assert.Single(file.Drivers);

        Assert.Equal(0.97, driver.WeightScalar);
        Assert.Equal(1.02, driver.PowerScalar);
        Assert.Null(driver.DragScalar);                 // tuning omits it, team is neutral 1.0
        Assert.Equal(0.45, driver.VehicleReliability);  // per-driver reliability beats the team's 0.90
        Assert.Equal(0.60, driver.SetupDownforce);
        Assert.Equal(0.10, driver.SetupDownforceRandomness);

        // And the written XML carries the juppo vocabulary verbatim.
        string xml = CustomAiXmlWriter.ToXml(file);
        Assert.Contains("<weight_scalar>0.97</weight_scalar>", xml);
        Assert.Contains("<power_scalar>1.02</power_scalar>", xml);
        Assert.Contains("<vehicle_reliability>0.45</vehicle_reliability>", xml);
        Assert.Contains("<setup_downforce>0.6</setup_downforce>", xml);
        Assert.Contains("<setup_downforce_randomness>0.1</setup_downforce_randomness>", xml);
        Assert.DoesNotContain("drag_scalar", xml);
    }

    [Fact]
    public void Stager_WithoutCarTuning_KeepsTheTeamValues_AndOmitsTheSetupPair()
    {
        var plan = RoundGridResolver.Resolve(PackWith(), 1);

        var driver = Assert.Single(GridStager.Build(plan, "test").Drivers);

        Assert.Equal(0.90, driver.VehicleReliability);
        Assert.Null(driver.WeightScalar); // neutral team scalar → omitted
        Assert.Null(driver.SetupDownforce);
        Assert.Null(driver.SetupDownforceRandomness);
        Assert.DoesNotContain("setup_downforce",
            CustomAiXmlWriter.ToXml(GridStager.Build(plan, "test")));
    }

    // ---------- validation ----------

    [Fact]
    public void Validator_RejectsOutOfRangeCarValues_OnDriversAndPatches()
    {
        var pack = PackWith(
            car: new PackDriverCar { WeightScalar = 2.4 }, // way past any sane scalar
            aiOverrides: new Dictionary<string, PackRatingsPatch>
            {
                ["driver.a"] = new() { VehicleReliability = 3.0 },
            });

        var issues = PackStructuralValidator.Validate(pack).Issues;

        Assert.Contains(issues, i => i.Severity == PackIssueSeverity.Error &&
            i.Message.Contains("weightScalar=2.4") && i.Message.Contains("driver.a"));
        Assert.Contains(issues, i => i.Severity == PackIssueSeverity.Error &&
            i.Message.Contains("vehicleReliability=3") && i.Message.Contains("Round 1"));
    }

    [Fact]
    public void Validator_AcceptsTheRealJuppoRanges()
    {
        var pack = PackWith(
            car: new PackDriverCar { WeightScalar = 0.95, PowerScalar = 1.02, DragScalar = 1.05, VehicleReliability = 1.43 },
            ratings: GridTestData.Ratings() with { SetupDownforce = 0.60, SetupDownforceRandomness = 0.20 },
            aiOverrides: new Dictionary<string, PackRatingsPatch>
            {
                ["driver.a"] = new() { WeightScalar = 0.96, VehicleReliability = 0.05 },
            });

        // No CAR-range error (the synthetic round legitimately trips unrelated rules like the
        // missing setupGuide — only the new range check is under test here).
        Assert.DoesNotContain(PackStructuralValidator.Validate(pack).Issues,
            i => i.Message.Contains("outside the sane range"));
    }

    // ---------- serialization (back-compat) ----------

    [Fact]
    public void DriversJson_CarBlock_RoundTrips_AndIsNeverInventedWhenAbsent()
    {
        var withCar = System.Text.Json.JsonSerializer.Deserialize<PackDriver>("""
            {
              "id": "driver.a",
              "name": "Alpha",
              "ratings": { "raceSkill": 0.8, "qualifyingSkill": 0.8, "setupDownforce": 0.6 },
              "car": { "weightScalar": 0.97, "vehicleReliability": 0.48 }
            }
            """, Companion.Core.Json.CoreJson.Options)!;

        Assert.Equal(0.97, withCar.Car!.WeightScalar);
        Assert.Null(withCar.Car.PowerScalar);
        Assert.Equal(0.48, withCar.Car.VehicleReliability);
        Assert.Equal(0.6, withCar.Ratings.SetupDownforce);

        var withoutCar = System.Text.Json.JsonSerializer.Deserialize<PackDriver>("""
            { "id": "driver.b", "name": "Beta", "ratings": { "raceSkill": 0.8, "qualifyingSkill": 0.8 } }
            """, Companion.Core.Json.CoreJson.Options)!;

        Assert.Null(withoutCar.Car);
        // The car block carries WhenWritingNull, so a driver without one never gains the key
        // (ratings nulls serialize per the existing convention — packs are written surgically
        // by tools, never via the serializer, so that convention is unchanged).
        string reserialized = System.Text.Json.JsonSerializer.Serialize(withoutCar, Companion.Core.Json.CoreJson.Options);
        Assert.DoesNotContain("\"car\"", reserialized);
    }
}
