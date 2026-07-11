using Companion.Ams2.Packs;
using Companion.Core.Packs;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Companion.Tests.Packs;

/// <summary>
/// Pins the 1969 pack to jusk's installed 26-livery F-Vintage_Gen2 source. The pack fields the
/// full base set, represents the Monza name/rating swap explicitly, and has one historical
/// same-livery proxy (Bill Brack in George Eaton's #22).
/// </summary>
public sealed class F11969SourceParityTests
{
    private static string PackDirectory =>
        Path.Combine(AppContext.BaseDirectory, "packs", "f1-1969");

    private static readonly string[] SourceBaseDriverIds =
    [
        "driver.graham_hill", "driver.jochen_rindt", "driver.mario_andretti",
        "driver.jo_siffert", "driver.denny_hulme", "driver.bruce_mclaren",
        "driver.jackie_stewart", "driver.jean_pierre_beltoise", "driver.chris_amon",
        "driver.john_surtees", "driver.jackie_oliver", "driver.jack_brabham",
        "driver.jacky_ickx", "driver.richard_attwood", "driver.piers_courage",
        "driver.peter_de_klerk", "driver.gerhard_mitter", "driver.pedro_rodriguez",
        "driver.george_eaton", "driver.silvio_moser", "driver.jo_bonnier",
        "driver.pete_lovely", "driver.henri_pescarolo", "driver.vic_elford",
        "driver.derek_bell", "driver.francois_cevert",
    ];

    private static readonly IReadOnlyDictionary<string, double> ReliabilityByBaseLivery =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Lotus-Ford Cosworth #1 G. Hill"] = 0.82,
            ["Lotus-Ford Cosworth #2 J. Rindt"] = 0.49,
            ["Lotus-Ford Cosworth #9 M. Andretti"] = 0.26,
            ["Lotus-Ford Cosworth #10 J. Siffert"] = 0.48,
            ["McLaren-Ford Cosworth #5 D. Hulme"] = 0.58,
            ["McLaren-Ford Cosworth #6 B. McLaren"] = 0.67,
            ["Matra-Ford Cosworth #7 J. Stewart"] = 0.86,
            ["Matra-Ford Cosworth #8 J-P. Beltoise"] = 0.73,
            ["Ferrari #11 C. Amon"] = 0.30,
            ["Ferrari #12 P. Rodriguez"] = 0.40,
            ["BRM #14 J. Surtees"] = 0.40,
            ["BRM #15 J. Oliver"] = 0.24,
            ["Brabham-Ford Cosworth #3 J. Brabham"] = 0.52,
            ["Brabham-Ford Cosworth #4 J. Ickx"] = 0.72,
            ["Brabham-Ford Cosworth #32 P. Courage"] = 0.47,
            ["McLaren-Ford Cosworth #18 V. Elford"] = 0.68,
            ["Brabham-Ford Cosworth #17 S. Moser"] = 0.36,
            ["Lotus-Ford Cosworth #16 J. Bonnier"] = 0.31,
            ["Brabham-Repco #19 P. de Klerk"] = 0.38,
            ["McLaren-Ford Cosworth #20 D. Bell"] = 0.60,
            ["Lotus-Ford Cosworth #25 P. Lovely"] = 0.59,
            ["BRM #22 G. Eaton"] = 0.18,
            ["Brabham-Ford Cosworth #29 R. Attwood"] = 0.57,
            ["BMW #24 G. Mitter"] = 0.41,
            ["Matra-Ford Cosworth #26 H. Pescarolo"] = 0.75,
            ["Tecno-Ford Cosworth #28 F. Cevert"] = 0.26,
        };

    [Fact]
    public void SourceBaseDrivers_RetainAllThirteenAuthoredRatingDimensions()
    {
        var drivers = LoadPack().Drivers.ToDictionary(driver => driver.Id, StringComparer.Ordinal);

        Assert.Equal(26, SourceBaseDriverIds.Length);
        foreach (string driverId in SourceBaseDriverIds)
            Assert.Equal(13, drivers[driverId].Ratings.Enumerate().Count());
    }

    [Fact]
    public void SourceBaseRatings_MatchTheVerifiedCanonicalHash()
    {
        var drivers = LoadPack().Drivers.ToDictionary(driver => driver.Id, StringComparer.Ordinal);
        string canonical = string.Join("\n", SourceBaseDriverIds
            .Order(StringComparer.Ordinal)
            .Select(driverId => driverId + "|" + string.Join(",", drivers[driverId].Ratings
                .Enumerate()
                .Select(field => field.Name + "=" +
                    field.Value.ToString("R", CultureInfo.InvariantCulture)))));

        string actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        Assert.Equal("9F3F28B7755BFC431F84235E673DF2DA44C6598EC6E933D80A96327ADF31C878", actual);
    }

    [Fact]
    public void MonzaSourceSwap_IsAnExplicitBrambillaEntry_NotAnAmonPatch()
    {
        SeasonPack pack = LoadPack();
        var drivers = pack.Drivers.ToDictionary(driver => driver.Id, StringComparer.Ordinal);
        PackEntry amon = Assert.Single(pack.Entries, entry => entry.DriverId == "driver.chris_amon");
        PackEntry brambilla = Assert.Single(pack.Entries, entry => entry.DriverId == "driver.ernesto_brambilla");
        PackRound monza = pack.Season.Rounds.Single(round => round.Round == 8);

        Assert.Equal("1-7,9-11", amon.Rounds);
        Assert.Equal("8", brambilla.Rounds);
        Assert.Equal(13, drivers[brambilla.DriverId].Ratings.Enumerate().Count());
        string canonical = brambilla.DriverId + "|" + string.Join(",",
            drivers[brambilla.DriverId].Ratings.Enumerate().Select(field =>
                field.Name + "=" + field.Value.ToString("R", CultureInfo.InvariantCulture)));
        Assert.Equal(
            "09483E453B7B92CC604557198E7ECDB760E9615F118018DD7D29AE0A06368FD9",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
        Assert.Equal(0.28, drivers[brambilla.DriverId].Car!.VehicleReliability);
        Assert.Contains(brambilla.DriverId, monza.Grid!.StarterDriverIds);
        Assert.DoesNotContain(amon.DriverId, monza.Grid.StarterDriverIds);
        Assert.Empty(monza.AiOverrides);
    }

    [Fact]
    public void BillBrack_RemainsTheOnlyPaceProxy_AndInheritsEatonReliability()
    {
        SeasonPack pack = LoadPack();
        PackDriver brack = pack.Drivers.Single(driver => driver.Id == "driver.bill_brack");

        Assert.Equal(2, brack.Ratings.Enumerate().Count());
        Assert.Equal(0.18, brack.Car!.VehicleReliability);
        Assert.Equal(
            ["driver.bill_brack"],
            pack.Drivers.Where(driver => driver.Ratings.Enumerate().Count() == 2)
                .Select(driver => driver.Id));
    }

    [Fact]
    public void EveryRoundStarter_StagesTheReliabilityOfItsActiveSourceBlock()
    {
        SeasonPack pack = LoadPack();
        var drivers = pack.Drivers.ToDictionary(driver => driver.Id, StringComparer.Ordinal);
        Assert.All(pack.Drivers, driver => Assert.Single(driver.Car!.Enumerate()));
        Assert.Equal(
            ReliabilityByBaseLivery.Keys.Order(StringComparer.Ordinal),
            pack.Entries.Select(entry => entry.Ams2LiveryName).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));

        foreach (PackRound round in pack.Season.Rounds)
        foreach (string driverId in round.Grid!.StarterDriverIds)
        {
            PackEntry entry = Assert.Single(pack.Entries, candidate =>
                candidate.DriverId == driverId && RoundsRange.Parse(candidate.Rounds).Contains(round.Round));
            double expected = driverId == "driver.ernesto_brambilla"
                ? 0.28
                : ReliabilityByBaseLivery[entry.Ams2LiveryName];
            double? actual = round.AiOverrides.TryGetValue(driverId, out PackRatingsPatch? patch) &&
                             patch.VehicleReliability is { } roundReliability
                ? roundReliability
                : drivers[driverId].Car?.VehicleReliability;

            Assert.True(actual.HasValue,
                $"Round {round.Round} {driverId} does not stage vehicleReliability.");
            Assert.Equal(expected, actual.Value);
        }
    }

    [Fact]
    public void FullBaseRoster_AndHistoricalExitBoundaries_RemainAuthored()
    {
        SeasonPack pack = LoadPack();

        Assert.Equal("1.2.0", pack.Manifest.Version);
        Assert.Equal(28, pack.Drivers.Count);
        Assert.Equal(29, pack.Entries.Count);
        Assert.Equal("1-11", Assert.Single(pack.Entries,
            entry => entry.DriverId == "driver.piers_courage").Rounds);
        Assert.Equal("1-11", Assert.Single(pack.Entries,
            entry => entry.DriverId == "driver.richard_attwood").Rounds);
        Assert.Equal("1-6", Assert.Single(pack.Entries,
            entry => entry.DriverId == "driver.gerhard_mitter").Rounds);
        Assert.Equal("1-10", Assert.Single(pack.Entries,
            entry => entry.DriverId == "driver.graham_hill").Rounds);

        PackRound monaco = pack.Season.Rounds.Single(round => round.Round == 3);
        Assert.Equal(25, monaco.Grid!.Size);
        Assert.Equal(26, monaco.Grid.StarterDriverIds.Count);
    }

    [Fact]
    public void RemainingPerTrackRatingBlocks_MatchTheSource()
    {
        SeasonPack pack = LoadPack();
        PackRound monaco = pack.Season.Rounds.Single(round => round.Round == 3);
        PackRound silverstone = pack.Season.Rounds.Single(round => round.Round == 6);

        Assert.Equal(2, monaco.AiOverrides.Count);
        Assert.All(monaco.AiOverrides.Values, patch =>
        {
            Assert.Equal(2, patch.Enumerate().Count());
            Assert.Empty(patch.EnumerateCar());
        });
        Assert.Equal(0.87, monaco.AiOverrides["driver.jean_pierre_beltoise"].RaceSkill);
        Assert.Equal(0.92, monaco.AiOverrides["driver.jean_pierre_beltoise"].QualifyingSkill);
        Assert.Equal(0.85, monaco.AiOverrides["driver.john_surtees"].RaceSkill);
        Assert.Equal(0.89, monaco.AiOverrides["driver.john_surtees"].QualifyingSkill);
        Assert.Single(silverstone.AiOverrides);
        Assert.Equal(2, silverstone.AiOverrides["driver.john_surtees"].Enumerate().Count());
        Assert.Empty(silverstone.AiOverrides["driver.john_surtees"].EnumerateCar());
        Assert.Equal(0.85, silverstone.AiOverrides["driver.john_surtees"].RaceSkill);
        Assert.Equal(0.89, silverstone.AiOverrides["driver.john_surtees"].QualifyingSkill);
    }

    private static SeasonPack LoadPack() => PackLoader.Parse(
        Read("pack.json"),
        Read("season.json"),
        Read("teams.json"),
        Read("drivers.json"),
        Read("entries.json"));

    private static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(PackDirectory, fileName));
}
