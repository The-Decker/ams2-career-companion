using Companion.Ams2.Packs;
using Companion.Ams2.Skins;
using Companion.Core.Packs;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static Companion.Ams2.Skins.VariantOverrideBinder;

namespace Companion.Tests.Packs;

/// <summary>
/// Pins f1-2010 to LadyCroussette's archived 260627 skinpack and AFry's Realistic
/// Formula Reiza Custom-AI/selector data, including replacement drivers and track form.
/// </summary>
public sealed class F12010SourceParityTests
{
    private static string PackDirectory =>
        Path.Combine(AppContext.BaseDirectory, "packs", "f1-2010");

    private static string SkinSetDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "ams2", "skin-seasons", "f1-2010");

    [Fact]
    public void SkinSeasonPointer_IsFormulaReizaOnly_AndSourceBound()
    {
        SeasonPack pack = LoadPack();

        Assert.Equal("f1-2010", pack.Manifest.SkinSeason);
        Assert.Equal("F-Reiza", pack.Season.Ams2Class);
        Assert.All(pack.Teams,
            team => Assert.Equal(["formula_reiza"], team.CarVehicleIds));

        Assert.True(Directory.Exists(SkinSetDirectory),
            $"2010 pointer fixture missing at '{SkinSetDirectory}'.");
        string[] files = Directory.GetFiles(SkinSetDirectory, "*.xml")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray()!;
        Assert.Equal(["formula_reiza.xml"], files);

        string pointerPath = Path.Combine(SkinSetDirectory, "formula_reiza.xml");
        Assert.Equal(
            "19CB989ED8D293A8197255F16EF9D4CE62305181A792EE70621F12A58E7C81AD",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pointerPath))));

        string pointerText = File.ReadAllText(pointerPath);
        string[] pointerNames = XDocument.Parse(pointerText)
            .Descendants("LIVERY_OVERRIDE")
            .Select(node => (string?)node.Attribute("NAME"))
            .Where(name => name is not null)
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] openingEntryNames = pack.Entries
            .Where(entry => RoundsRange.Parse(entry.Rounds).Contains(1))
            .Select(entry => entry.Ams2LiveryName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(24, pointerNames.Length);
        Assert.Equal(openingEntryNames, pointerNames);
        Assert.Equal(["F1_Season_2010"], Regex.Matches(pointerText, @"F1_Season_\d{4}")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void HistoricalSourceProfiles_RetainAllRatingsAndCarTuning()
    {
        SeasonPack pack = LoadPack();

        Assert.Equal(27, pack.Drivers.Count);
        Assert.All(pack.Drivers, driver =>
        {
            Assert.Equal(14, driver.Ratings.Enumerate().Count());
            Assert.NotNull(driver.Car);
            Assert.Equal(4, driver.Car!.Enumerate().Count());
        });

        string ratingsCanonical = string.Join("\n", pack.Drivers
            .OrderBy(driver => driver.Id, StringComparer.Ordinal)
            .Select(driver => driver.Id + "|" + string.Join(",", driver.Ratings.Enumerate()
                .Select(field => field.Name + "=" +
                    field.Value.ToString("R", CultureInfo.InvariantCulture)))));
        string carCanonical = string.Join("\n", pack.Drivers
            .OrderBy(driver => driver.Id, StringComparer.Ordinal)
            .Select(driver => driver.Id + "|" + string.Join(",", driver.Car!.Enumerate()
                .Select(field => field.Name + "=" +
                    field.Value.ToString("R", CultureInfo.InvariantCulture)))));

        Assert.Equal(
            "4C8FFAAFEFEF7BAE9C6F682422FF36AC4D9D480A2B1CFD36D162F414366586D8",
            Sha256(ratingsCanonical));
        Assert.Equal(
            "6FDC0D785B3FEB9B653783FF69E6A4D18928CA8D275A90C1CBA09CF013A6BB42",
            Sha256(carCanonical));

        PackDriver yamamoto = pack.Drivers.Single(driver => driver.Id == "driver.sakon_yamamoto");
        Assert.Equal(0.85, yamamoto.Ratings.RaceSkill);
        Assert.Equal(0.67, yamamoto.Ratings.QualifyingSkill);
        Assert.Equal(0.48, yamamoto.Car!.VehicleReliability);
    }

    [Fact]
    public void RosterWindows_TrackTheSelector_AndDnsEvents()
    {
        SeasonPack pack = LoadPack();
        var specialRanges = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["2010 Hispania #20 - K. Chandhok"] = "1-10",
            ["2010 Hispania #21 - B. Senna"] = "1-9,11-19",
            ["2010 Hispania #21 - S. Yamamoto"] = "10",
            ["2010 Hispania #20 - S. Yamamoto"] = "11-14,16-17",
            ["2010 Hispania #20 - C. Klien"] = "15,18-19",
            ["2010 BMW Sauber #22 - P. de la Rosa"] = "1-14",
            ["2010 BMW Sauber #22 - N. Heidfeld"] = "15-19",
        };
        var dnsByRound = new Dictionary<int, string>
        {
            [2] = "driver.jarno_trulli",
            [3] = "driver.pedro_de_la_rosa",
            [4] = "driver.timo_glock",
            [5] = "driver.heikki_kovalainen",
            [16] = "driver.lucas_di_grassi",
        };

        Assert.Equal(12, pack.Teams.Count);
        Assert.Equal(27, pack.Drivers.Count);
        Assert.Equal(28, pack.Entries.Count);
        foreach (PackEntry entry in pack.Entries)
            Assert.Equal(specialRanges.GetValueOrDefault(entry.Ams2LiveryName, "1-19"), entry.Rounds);

        foreach (PackRound round in pack.Season.Rounds)
        {
            var expected = pack.Entries
                .Where(entry => RoundsRange.Parse(entry.Rounds).Contains(round.Round))
                .Select(entry => entry.DriverId)
                .Distinct(StringComparer.Ordinal)
                .Where(driverId => !dnsByRound.TryGetValue(round.Round, out string? dns) || driverId != dns)
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.NotNull(round.Grid);
            Assert.Equal(expected, round.Grid!.StarterDriverIds.Order(StringComparer.Ordinal));
            Assert.Equal(expected.Length, round.Grid.Size);
            Assert.Equal(dnsByRound.ContainsKey(round.Round) ? 23 : 24, round.Grid.Size);
            Assert.Equal(round.Grid.Size - 1, round.SetupGuide!.Session.Opponents);
            Assert.InRange(round.Grid.Size, 1, 24);
        }
    }

    [Fact]
    public void SelectorChangePoints_AnchorToThe2010Calendar()
    {
        SeasonPack pack = LoadPack();
        CalendarRound[] calendar = pack.Season.Rounds
            .Select(round => new CalendarRound(round.Round, round.Name, round.Track.RealVenue))
            .ToArray();
        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["2010_01BHR"] = 1,
            ["2010_02AUS"] = 2,
            ["2010_03MYS"] = 3,
            ["2010_04CHN"] = 4,
            ["2010_05ESP"] = 5,
            ["2010_07TUR"] = 7,
            ["2010_08CAN"] = 8,
            ["2010_10GBR"] = 10,
            ["2010_11DEU"] = 11,
            ["2010_13BEL"] = 13,
            ["2010_14ITA"] = 14,
            ["2010_15SGP"] = 15,
            ["2010_16JPN"] = 16,
            ["2010_18BRA"] = 18,
            ["2010_19UAE"] = 19,
        };

        foreach ((string suffix, int round) in expected)
            Assert.Equal(round, VariantOverrideBinder.AnchorRound(suffix, calendar, 2010));
        Assert.Null(VariantOverrideBinder.AnchorRound("2010_60WIF", calendar, 2010));
        Assert.Null(VariantOverrideBinder.AnchorRound("2010_61WIF", calendar, 2010));
        Assert.Null(VariantOverrideBinder.AnchorRound("2012_01AUS", calendar, 2010));
    }

    [Fact]
    public void PerTrackSourceBlocks_MatchTheActiveLineup()
    {
        SeasonPack pack = LoadPack();
        var expectedCounts = new Dictionary<int, int>
        {
            [1] = 24,
            [2] = 23,
            [5] = 23,
            [6] = 24,
            [8] = 24,
            [10] = 24,
            [11] = 24,
            [12] = 24,
            [13] = 24,
            [14] = 24,
            [16] = 23,
            [18] = 24,
            [19] = 24,
        };

        foreach (PackRound round in pack.Season.Rounds)
        {
            int expected = expectedCounts.GetValueOrDefault(round.Round);
            Assert.Equal(expected, round.AiOverrides.Count);
            if (expected > 0)
                Assert.Equal(
                    round.Grid!.StarterDriverIds.Order(StringComparer.Ordinal),
                    round.AiOverrides.Keys.Order(StringComparer.Ordinal));
        }

        Assert.Equal(309, pack.Season.Rounds.Sum(round => round.AiOverrides.Count));
        Assert.Equal(1_674, pack.Season.Rounds.Sum(round => round.AiOverrides.Values.Sum(
            patch => patch.Enumerate().Count() + patch.EnumerateCar().Count())));
        Assert.True(pack.Season.Rounds.SelectMany(round => round.AiOverrides.Values)
            .Any(patch => patch.VehicleReliability < 0),
            "The source's intentional forced-retirement reliability patches were lost.");

        string canonical = string.Join("\n", pack.Season.Rounds
            .OrderBy(round => round.Round)
            .SelectMany(round => round.AiOverrides
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => $"R{round.Round}|{entry.Key}|" +
                    string.Join(",", entry.Value.Enumerate().Select(field =>
                        field.Name + "=" + field.Value.ToString("R", CultureInfo.InvariantCulture))) + "|" +
                    string.Join(",", entry.Value.EnumerateCar().Select(field =>
                        field.Name + "=" + field.Value.ToString("R", CultureInfo.InvariantCulture))))));
        Assert.Equal(
            "847F35102AF89F98852844F4189FF35976A4A6D43B2C38224F9F1EA634E31577",
            Sha256(canonical));

        PackRatingsPatch yamamoto = pack.Season.Rounds.Single(round => round.Round == 10)
            .AiOverrides["driver.sakon_yamamoto"];
        Assert.Equal(0.84, yamamoto.RaceSkill);
        Assert.Equal(0.65, yamamoto.QualifyingSkill);
        Assert.Equal(0.12, yamamoto.Aggression);
        Assert.Equal(0.17, yamamoto.Defending);
        Assert.Equal(0.82, yamamoto.Stamina);
        Assert.Equal(0.82, yamamoto.Consistency);
        Assert.Equal(0.29, yamamoto.StartReactions);
        Assert.Equal(0.13, yamamoto.WetSkill);
        Assert.Equal(0.54, yamamoto.TyreManagement);
        Assert.Equal(0.56, yamamoto.AvoidanceOfMistakes);
        Assert.Equal(0.11, yamamoto.BlueFlagConceding);
        Assert.Equal(0.42, yamamoto.WeatherTyreChanges);
        Assert.Equal(0.45, yamamoto.AvoidanceOfForcedMistakes);
        Assert.Equal(0.58, yamamoto.FuelManagement);
        Assert.Equal(1.022, yamamoto.WeightScalar);
        Assert.Equal(0.915, yamamoto.PowerScalar);
        Assert.Equal(1.062, yamamoto.DragScalar);
        Assert.Equal(0.4, yamamoto.VehicleReliability);
    }

    [Fact]
    public void RefuellingAndResearchedWeather_RemainAuthored()
    {
        SeasonPack pack = LoadPack();
        string[] clear = ["Clear", "Clear", "Clear", "Clear"];
        var qualifying = new Dictionary<int, string[]>
        {
            [3] = ["Rain", "Light Rain", "Heavy Rain", "Light Rain"],
            [13] = ["Light Cloud", "Light Rain", "Medium Cloud", "Light Rain"],
            [18] = ["Light Rain", "Light Rain", "Overcast", "Medium Cloud"],
        };
        var races = new Dictionary<int, string[]>
        {
            [2] = ["Light Rain", "Overcast", "Overcast", "Overcast"],
            [4] = ["Light Rain", "Rain", "Light Rain", "Rain"],
            [7] = ["Clear", "Light Cloud", "Heavy Cloud", "Light Rain"],
            [13] = ["Light Rain", "Medium Cloud", "Heavy Cloud", "Rain"],
            [17] = ["Storm", "Rain", "Light Rain", "Overcast"],
        };

        Assert.Equal(false, pack.Season.RefuellingAllowed);
        foreach (PackRound round in pack.Season.Rounds)
        {
            Assert.NotNull(round.Weekend);
            Assert.Equal(clear, round.Weekend!.Practice!.WeatherSlots);
            Assert.Equal(qualifying.GetValueOrDefault(round.Round, clear),
                round.Weekend.Qualifying!.WeatherSlots);
            Assert.Equal(races.GetValueOrDefault(round.Round, clear),
                Assert.Single(round.Weekend.Races).WeatherSlots);
        }
    }

    [Fact]
    public void TracksAndPlaceholderAlternates_RemainPinned()
    {
        SeasonPack pack = LoadPack();
        (string Id, bool Placeholder, int Laps)[] expected =
        [
            ("jerez_2019", true, 70),
            ("adelaide_modern", true, 96),
            ("curvelo", true, 70),
            ("spielberg_gp", true, 71),
            ("barcelona_gp", false, 66),
            ("azure_circuit", false, 78),
            ("nurb_gp_2020", true, 60),
            ("montrealmodern", false, 70),
            ("long_beach", true, 98),
            ("silverstone_2019", false, 52),
            ("hockenheim_gp", false, 67),
            ("hungaroring_gp_2025", false, 70),
            ("spa-francorchamps_2020", false, 44),
            ("monza_2005", false, 53),
            ("salvador", true, 114),
            ("kansai_gp", false, 53),
            ("termas_rio_hondo", true, 64),
            ("interlagos", false, 71),
            ("imola", true, 62),
        ];
        var alternates = new Dictionary<int, (string Id, int Laps, bool Real)>
        {
            [1] = ("emirates_raceway_gp", 58, false),
            [2] = ("lakeville_raceway_gp", 76, false),
            [3] = ("florence_gp", 59, false),
            [4] = ("circuit_of_the_americas_gp", 55, false),
            [7] = ("florence_gp", 59, false),
            [17] = ("circuit_of_the_americas_gp", 56, false),
            [19] = ("emirates_raceway_gp", 57, false),
        };

        Assert.Equal(expected.Length, pack.Season.Rounds.Count);
        foreach (PackRound round in pack.Season.Rounds)
        {
            var binding = expected[round.Round - 1];
            Assert.Equal(binding, (round.Track.Id, round.Track.IsPlaceholder, round.Laps));
            if (alternates.TryGetValue(round.Round, out var alternate))
            {
                Assert.NotNull(round.Track.Alternate);
                Assert.Equal(alternate,
                    (round.Track.Alternate!.Id, round.Track.Alternate.Laps,
                        round.Track.Alternate.IsRealVenue));
            }
            else
            {
                Assert.Null(round.Track.Alternate);
            }
        }

        Assert.Equal("Bahrain International Circuit, Endurance Circuit",
            pack.Season.Rounds[0].Track.RealVenue);
        Assert.Equal("Albert Park Grand Prix Circuit", pack.Season.Rounds[1].Track.RealVenue);
        Assert.Equal("Circuit de Catalunya", pack.Season.Rounds[4].Track.RealVenue);
        Assert.Equal("Suzuka International Racing Course", pack.Season.Rounds[15].Track.RealVenue);
        Assert.Equal("Korea International Circuit", pack.Season.Rounds[16].Track.RealVenue);
        Assert.Null(pack.Season.Rounds[8].Track.Alternate);  // Valencia street-circuit gap
        Assert.Null(pack.Season.Rounds[14].Track.Alternate); // Marina Bay street-circuit gap
    }

    [Fact]
    public void HistoryCoverage_IsCompleteAndUses2010CircuitNames()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "history", "2010.json");
        using JsonDocument history = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement rounds = history.RootElement.GetProperty("rounds");

        Assert.Equal(19, rounds.GetArrayLength());
        foreach (JsonElement round in rounds.EnumerateArray())
        {
            JsonElement circuit = round.GetProperty("circuit");
            Assert.False(string.IsNullOrWhiteSpace(circuit.GetProperty("layoutId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(circuit.GetProperty("history").GetString()));
            Assert.NotEmpty(circuit.GetProperty("facts").EnumerateArray());

            string eraText = circuit.GetProperty("history").GetString() + " " +
                string.Join(" ", circuit.GetProperty("facts").EnumerateArray()
                    .Select(fact => fact.GetString()));
            Assert.All(Regex.Matches(eraText, @"\b(?:19|20)\d{2}\b")
                .Select(match => int.Parse(match.Value, CultureInfo.InvariantCulture)),
                year => Assert.True(year <= 2010,
                    $"Round {round.GetProperty("round").GetInt32()} leaks post-2010 history year {year}."));
        }

        Assert.Equal("Bahrain International Circuit, Endurance Circuit",
            rounds[0].GetProperty("circuit").GetProperty("name").GetString());
        Assert.Equal("Albert Park Grand Prix Circuit",
            rounds[1].GetProperty("circuit").GetProperty("name").GetString());
        Assert.Equal("Circuit de Catalunya",
            rounds[4].GetProperty("circuit").GetProperty("name").GetString());
        Assert.Equal("Suzuka International Racing Course",
            rounds[15].GetProperty("circuit").GetProperty("name").GetString());
        Assert.Equal("Korea International Circuit",
            rounds[16].GetProperty("circuit").GetProperty("name").GetString());
    }

    [Fact]
    public void ProvenanceAndKnownSourceCaveats_RemainDocumented()
    {
        SeasonPack pack = LoadPack();
        string attribution = string.Join("\n", pack.Manifest.Attribution);
        string notes = string.Join("\n", pack.Manifest.Notes);
        string[] whatIfLiveries =
        [
            "2010 BMW Sauber #22 - J. Villeneuve",
            "2010 Mercedes #3 - N. Heidfeld",
            "2010 Mercedes #1 - J. Button",
            "2010 Mercedes #2 - N. Rosberg",
            "2010 Ferrari #7 - M. Schumacher",
            "2010 Ferrari #8 - K. Räikkönen",
            "2010 McLaren #3 - K. Räikkönen",
            "2010 McLaren #4 - L. Hamilton",
            "2010 Renault #12 - F. Alonso",
        ];

        Assert.Contains("LadyCroussette", attribution, StringComparison.Ordinal);
        Assert.Contains("AFry", attribution, StringComparison.Ordinal);
        Assert.Contains("4CBA68519B37A97677D2B23A0E800E1B12CACB1AF25DF5B3B5840BAB43A835FC",
            notes, StringComparison.Ordinal);
        Assert.Contains("DBD7C5953C72F39D3B41C8022FF2F7599F3F349C4C3D4260750F38720ECB8BBE",
            notes, StringComparison.Ordinal);
        Assert.Contains("Sauber_visor_Heidfeld.dds", notes, StringComparison.Ordinal);
        Assert.Contains("Sauber_visor.dds", notes, StringComparison.Ordinal);
        Assert.Contains("309", notes, StringComparison.Ordinal);
        foreach (string livery in whatIfLiveries)
            Assert.DoesNotContain(pack.Entries, entry => entry.Ams2LiveryName == livery);
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
