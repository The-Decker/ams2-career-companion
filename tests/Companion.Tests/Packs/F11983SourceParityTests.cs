using Companion.Ams2.Packs;
using Companion.Core.Packs;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Companion.Tests.Packs;

/// <summary>
/// Pins f1-1983 to Humpty's 26-profile TAMS2SP source and the 24 liveries exposed by the
/// committed active pointer set. The two source-only optional skins remain documented omissions.
/// </summary>
public sealed class F11983SourceParityTests
{
    private static string PackDirectory =>
        Path.Combine(AppContext.BaseDirectory, "packs", "f1-1983");

    private static string SkinSetDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "ams2", "skin-seasons", "f1-1983");

    private static readonly IReadOnlyDictionary<string, string> PointerVehicles =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["brabham_bt52.xml"] = "brabham_bt52",
            ["formula_retro_g3.xml"] = "formula_retro_g3",
            ["formula_retro_g3_te.xml"] = "formula_retro_g3_te",
            ["mclaren_mp4_1c.xml"] = "mclaren_mp4_1c",
        };

    [Fact]
    public void SkinSeasonPointers_ExposeExactlyThePackLiveries_OnOwnedVehicles()
    {
        SeasonPack pack = LoadPack();
        var teams = pack.Teams.ToDictionary(team => team.Id, StringComparer.Ordinal);
        var liveryVehicle = new Dictionary<string, string>(StringComparer.Ordinal);

        Assert.Equal("f1-1983", pack.Manifest.SkinSeason);
        Assert.Equal("F-Retro_Gen3", pack.Season.Ams2Class);
        Assert.True(Directory.Exists(SkinSetDirectory),
            $"1983 pointer fixture missing at '{SkinSetDirectory}'.");

        foreach ((string fileName, string vehicleId) in PointerVehicles)
        {
            string path = Path.Combine(SkinSetDirectory, fileName);
            Assert.True(File.Exists(path), $"Missing 1983 pointer fixture '{path}'.");
            XDocument xml = XDocument.Load(path);
            foreach (string name in xml.Descendants("LIVERY_OVERRIDE")
                         .Select(node => (string?)node.Attribute("NAME"))
                         .Where(name => name is not null)
                         .Cast<string>())
            {
                Assert.True(liveryVehicle.TryAdd(name, vehicleId),
                    $"Active livery '{name}' appears in more than one pointer.");
            }
        }

        Assert.Equal(24, liveryVehicle.Count);
        Assert.Equal(
            pack.Entries.Select(entry => entry.Ams2LiveryName).Order(StringComparer.Ordinal),
            liveryVehicle.Keys.Order(StringComparer.Ordinal));

        foreach (PackEntry entry in pack.Entries)
            Assert.Contains(liveryVehicle[entry.Ams2LiveryName], teams[entry.TeamId].CarVehicleIds);
    }

    [Fact]
    public void ActiveSourceProfiles_RetainAllRatingsAndReliability_CanonicalHashes()
    {
        SeasonPack pack = LoadPack();

        Assert.Equal(24, pack.Drivers.Count);
        Assert.All(pack.Drivers, driver =>
        {
            Assert.Equal(13, driver.Ratings.Enumerate().Count());
            Assert.NotNull(driver.Car?.VehicleReliability);
            Assert.Single(driver.Car!.Enumerate());
        });
        Assert.All(pack.Season.Rounds, round => Assert.Empty(round.AiOverrides));

        string ratingsCanonical = string.Join("\n", pack.Drivers
            .OrderBy(driver => driver.Id, StringComparer.Ordinal)
            .Select(driver => driver.Id + "|" + string.Join(",", driver.Ratings.Enumerate()
                .Select(field => field.Name + "=" +
                    field.Value.ToString("R", CultureInfo.InvariantCulture)))));
        string reliabilityCanonical = string.Join("\n", pack.Drivers
            .OrderBy(driver => driver.Id, StringComparer.Ordinal)
            .Select(driver => driver.Id + "|vehicleReliability=" +
                driver.Car!.VehicleReliability!.Value.ToString("R", CultureInfo.InvariantCulture)));

        Assert.Equal(
            "C7D0A3B97E7033677261A975D1B107F9CA840E2BE423E193C4AFE332AEB6AF82",
            Sha256(ratingsCanonical));
        Assert.Equal(
            "2C252A28C17A2F857DA1D6F5105B4E81685EDDD8756ABBC4A8F56E7CFF2EE9CD",
            Sha256(reliabilityCanonical));
    }

    [Fact]
    public void RosterWindows_AndEveryRoundGrid_RemainHonest()
    {
        SeasonPack pack = LoadPack();
        var specialRanges = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["driver.johnny_cecotto"] = "1-13",
            ["driver.thierry_boutsen"] = "6-15",
            ["driver.stefan_johansson"] = "9-14",
        };

        Assert.Equal(24, pack.Entries.Count);
        foreach (PackEntry entry in pack.Entries)
            Assert.Equal(specialRanges.GetValueOrDefault(entry.DriverId, "1-15"), entry.Rounds);

        foreach (PackRound round in pack.Season.Rounds)
        {
            var covering = pack.Entries
                .Where(entry => RoundsRange.Parse(entry.Rounds).Contains(round.Round))
                .Select(entry => entry.DriverId)
                .Order(StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(covering.Length, round.Grid!.Size);
            Assert.Equal(covering, round.Grid.StarterDriverIds.Order(StringComparer.Ordinal));
            Assert.Equal(round.Grid.Size - 1, round.SetupGuide!.Session.Opponents);
            Assert.InRange(round.Grid.Size, 1, 24);
        }
    }

    [Fact]
    public void SourceOnlyOptionalSkins_AndJarierRepair_RemainDocumented()
    {
        SeasonPack pack = LoadPack();
        string notes = string.Join("\n", pack.Manifest.Notes);
        string naturalPointer = File.ReadAllText(
            Path.Combine(SkinSetDirectory, "formula_retro_g3.xml"));

        Assert.DoesNotContain(pack.Entries,
            entry => entry.Ams2LiveryName.Contains("#33 Guerrero", StringComparison.Ordinal));
        Assert.DoesNotContain(pack.Entries,
            entry => entry.Ams2LiveryName.Contains("#36 Giacomelli", StringComparison.Ordinal));
        Assert.Contains("#33 Roberto Guerrero", notes, StringComparison.Ordinal);
        Assert.Contains("#36 Bruno Giacomelli", notes, StringComparison.Ordinal);
        Assert.Contains("83_Jarier_visor_spec.dds", naturalPointer, StringComparison.Ordinal);
        Assert.DoesNotContain("83_Jarrier_visor_spec.dds", naturalPointer, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuelling_Weather_AndPlaceholderAlternates_RemainAuthored()
    {
        SeasonPack pack = LoadPack();

        Assert.True(pack.Season.RefuellingAllowed);
        AssertWeather(pack, 5,
            ["Rain", "Rain", "Heavy Rain", "Rain"],
            ["Light Rain", "Heavy Cloud", "Medium Cloud", "Light Cloud"]);
        AssertWeather(pack, 6,
            ["Clear", "Heavy Cloud", "Rain", "Heavy Rain"],
            ["Clear", "Light Cloud", "Clear", "Clear"]);
        AssertWeather(pack, 7,
            ["Rain", "Heavy Rain", "Heavy Cloud", "Clear"],
            ["Clear", "Clear", "Clear", "Clear"]);
        AssertWeather(pack, 10,
            ["Heavy Cloud", "Rain", "Heavy Rain", "Rain"],
            ["Overcast", "Heavy Cloud", "Heavy Cloud", "Medium Cloud"]);

        PackRound paulRicard = pack.Season.Rounds.Single(round => round.Round == 3);
        PackRound detroit = pack.Season.Rounds.Single(round => round.Round == 7);
        PackRound zandvoort = pack.Season.Rounds.Single(round => round.Round == 12);
        Assert.Equal(("florence_gp", 60, false),
            (paulRicard.Track.Alternate!.Id, paulRicard.Track.Alternate.Laps,
                paulRicard.Track.Alternate.IsRealVenue));
        Assert.Null(detroit.Track.Alternate);
        Assert.Equal(("Heusden", 76, false),
            (zandvoort.Track.Alternate!.Id, zandvoort.Track.Alternate.Laps,
                zandvoort.Track.Alternate.IsRealVenue));
        Assert.Equal("Jacarepaguá", pack.Season.Rounds[0].Track.RealVenue);
        Assert.Equal("Österreichring", pack.Season.Rounds[10].Track.RealVenue);
        Assert.Equal("Circuit Zandvoort", zandvoort.Track.RealVenue);
    }

    [Fact]
    public void HistoryCoverage_IsCompleteAndUses1983CircuitNames()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "history", "1983.json");
        using JsonDocument history = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement rounds = history.RootElement.GetProperty("rounds");

        Assert.Equal(15, rounds.GetArrayLength());
        foreach (JsonElement round in rounds.EnumerateArray())
        {
            JsonElement circuit = round.GetProperty("circuit");
            Assert.False(string.IsNullOrWhiteSpace(circuit.GetProperty("layoutId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(circuit.GetProperty("history").GetString()));
            Assert.NotEmpty(circuit.GetProperty("facts").EnumerateArray());
        }

        Assert.Equal("Jacarepaguá", rounds[0].GetProperty("circuit").GetProperty("name").GetString());
        Assert.Equal("Autodromo Dino Ferrari",
            rounds[3].GetProperty("circuit").GetProperty("name").GetString());
        Assert.Equal("Österreichring",
            rounds[10].GetProperty("circuit").GetProperty("name").GetString());
    }

    private static void AssertWeather(
        SeasonPack pack,
        int roundNumber,
        IReadOnlyList<string> qualifying,
        IReadOnlyList<string> race)
    {
        PackRound round = pack.Season.Rounds.Single(candidate => candidate.Round == roundNumber);
        Assert.Equal(qualifying, round.Weekend!.Qualifying!.WeatherSlots);
        Assert.Equal(race, Assert.Single(round.Weekend.Races).WeatherSlots);
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static SeasonPack LoadPack() => PackLoader.Parse(
        Read("pack.json"),
        Read("season.json"),
        Read("teams.json"),
        Read("drivers.json"),
        Read("entries.json"));

    private static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(PackDirectory, fileName));
}
