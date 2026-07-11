using Companion.Ams2.Packs;
using Companion.Core.Packs;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Companion.Tests.Packs;

/// <summary>
/// Pins the 1967 pack to the locally verified jusk v0.3 Custom-AI source documented in
/// docs/research/1967-source-parity.md. The source authors reliability per livery, while the
/// max-grid roster can place historical substitute drivers in those same livery slots.
/// </summary>
public sealed class F11967SourceParityTests
{
    private static string PackDirectory =>
        Path.Combine(AppContext.BaseDirectory, "packs", "f1-1967");

    private static readonly IReadOnlyDictionary<string, double> ReliabilityByLivery =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Brabham-Repco #1 J. Brabham"] = 0.93,
            ["Brabham-Repco #2 D. Hulme"] = 0.96,
            ["Lotus-Ford Cosworth #5 J. Clark"] = 0.51,
            ["Lotus-Ford Cosworth #6 G. Hill"] = 0.36,
            ["Cooper-Maserati #12 J. Siffert"] = 0.43,
            ["McLaren-BRM #14 B. McLaren"] = 0.33,
            ["Brabham-Repco #15 G. Ligier"] = 0.70,
            ["BRM #3 J. Stewart"] = 0.29,
            ["BRM #4 M. Spence"] = 0.50,
            ["Honda #7 J. Surtees"] = 0.54,
            ["Lola-BMW #17 H. Hahne"] = 0.19,
            ["Matra-Ford Cosworth #20 J-P. Beltoise"] = 0.38,
            ["Matra-Ford Cosworth #29 J. Ickx"] = 0.39,
            ["Ferrari #8 C. Amon"] = 0.93,
            ["Ferrari #18 L. Bandini"] = 0.89,
            ["Ferrari #19 L. Scarfiotti"] = 0.90,
            ["Cooper-Maserati #30 J. Rindt"] = 0.24,
            ["Cooper-Maserati #11 P. Rodriguez"] = 0.81,
            ["Eagle-Climax #10 D. Gurney"] = 0.25,
            ["Eagle-Climax #22 R. Ginther"] = 0.20,
        };

    private static readonly string[] SourceNamedDriverIds =
    [
        "driver.jack_brabham", "driver.denny_hulme", "driver.jim_clark",
        "driver.graham_hill", "driver.jo_siffert", "driver.bruce_mclaren",
        "driver.guy_ligier", "driver.jackie_stewart", "driver.mike_spence",
        "driver.john_surtees", "driver.hubert_hahne", "driver.jean_pierre_beltoise",
        "driver.jacky_ickx", "driver.chris_amon", "driver.lorenzo_bandini",
        "driver.ludovico_scarfiotti", "driver.jochen_rindt", "driver.pedro_rodriguez",
        "driver.dan_gurney", "driver.richie_ginther",
    ];

    private static readonly string[] RosterProxyDriverIds =
    [
        "driver.bob_anderson", "driver.mike_parkes", "driver.jonathan_williams",
        "driver.richard_attwood", "driver.jo_bonnier", "driver.al_pease",
        "driver.jo_schlesser", "driver.johnny_servoz_gavin",
    ];

    [Fact]
    public void SourceNamedDrivers_RetainAllThirteenAuthoredRatingDimensions()
    {
        var drivers = LoadPack().Drivers.ToDictionary(d => d.Id, StringComparer.Ordinal);

        foreach (string driverId in SourceNamedDriverIds)
        {
            Assert.Equal(13, drivers[driverId].Ratings.Enumerate().Count());
        }
    }

    [Fact]
    public void SourceNamedDriverRatings_MatchTheVerifiedCanonicalHash()
    {
        var drivers = LoadPack().Drivers.ToDictionary(d => d.Id, StringComparer.Ordinal);
        string canonical = string.Join("\n", SourceNamedDriverIds
            .Order(StringComparer.Ordinal)
            .Select(driverId => driverId + "|" + string.Join(",", drivers[driverId].Ratings
                .Enumerate()
                .Select(field => field.Name + "=" +
                    field.Value.ToString("R", CultureInfo.InvariantCulture)))));

        string actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        Assert.Equal("9B2F28690933CA5631BA1AD0E620081E8A3A4B933B16AF3DEA455DC3EF252E2D", actual);
    }

    [Fact]
    public void RosterProxies_KeepOnlyTheirEvidenceBackedPaceRatings()
    {
        var drivers = LoadPack().Drivers.ToDictionary(d => d.Id, StringComparer.Ordinal);

        foreach (string driverId in RosterProxyDriverIds)
        {
            Assert.Equal(2, drivers[driverId].Ratings.Enumerate().Count());
        }
    }

    [Fact]
    public void EveryRoundStarter_StagesTheReliabilityOfItsActiveLivery()
    {
        SeasonPack pack = LoadPack();
        var drivers = pack.Drivers.ToDictionary(d => d.Id, StringComparer.Ordinal);
        Assert.Equal(
            ReliabilityByLivery.Keys.Order(StringComparer.Ordinal),
            pack.Entries.Select(e => e.Ams2LiveryName).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));

        foreach (var round in pack.Season.Rounds)
        foreach (string driverId in round.Grid!.StarterDriverIds)
        {
            PackEntry entry = Assert.Single(pack.Entries, e =>
                e.DriverId == driverId && RoundsRange.Parse(e.Rounds).Contains(round.Round));

            Assert.True(ReliabilityByLivery.TryGetValue(entry.Ams2LiveryName, out double expected),
                $"Round {round.Round} entry '{entry.Ams2LiveryName}' has no audited source reliability.");

            double? actual = round.AiOverrides.TryGetValue(driverId, out var patch) &&
                             patch.VehicleReliability is { } roundReliability
                ? roundReliability
                : drivers[driverId].Car?.VehicleReliability;

            Assert.True(actual.HasValue,
                $"Round {round.Round} {driverId} does not stage a vehicleReliability value.");
            Assert.Equal(expected, actual.Value);
        }
    }

    [Fact]
    public void Scarfiotti_MonzaPatchFromTheSourceIsNotDroppedByTheExpandedGrid()
    {
        var monza = LoadPack().Season.Rounds.Single(r => r.Round == 9);
        var patch = monza.AiOverrides["driver.ludovico_scarfiotti"];

        Assert.Equal(0.55, patch.RaceSkill);
        Assert.Equal(0.56, patch.QualifyingSkill);
    }

    [Fact]
    public void FatalityAndCareerExitBoundaries_RemainAuthored()
    {
        var entries = LoadPack().Entries;

        Assert.Equal("1-2", Assert.Single(entries, e => e.DriverId == "driver.lorenzo_bandini").Rounds);
        Assert.Equal("3-4", Assert.Single(entries, e => e.DriverId == "driver.mike_parkes").Rounds);
        Assert.Equal("1-2", Assert.Single(entries, e => e.DriverId == "driver.richie_ginther").Rounds);
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
