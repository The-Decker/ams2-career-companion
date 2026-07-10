using System.Text.Json;
using Companion.Core.Json;
using Companion.Core.Numerics;
using Companion.Core.Packs;
using Companion.Core.Scoring;

namespace Companion.Tests.Packs;

/// <summary>
/// Round-trips a minimal synthetic pack (contract example shapes, two rounds) through
/// PackLoader and CoreJson, and checks that malformed parts fail with the file-part named.
/// </summary>
public class PackLoaderTests
{
    private const string ManifestJson = """
        {
          "packId": "test-1967",
          "name": "Test Pack 1967",
          "version": "1.0.0",
          "formatVersion": 1,
          "gameVersionTested": "1.6.9.8",
          "license": "CC BY 4.0",
          "attribution": ["Historical data derived from f1db (CC BY 4.0)"],
          "requires": {
            "dlc": [],
            "skinPacks": [
              {
                "name": "F1 1967 Season (Alain Fry)",
                "url": "https://example.test/downloads/f1-1967",
                "overridesFolder": "F1_Season_1967"
              }
            ]
          }
        }
        """;

    private const string SeasonJson = """
        {
          "year": 1967,
          "seriesName": "Test World Championship",
          "ams2Class": "F-Vintage_Gen1",
          "pointsSystem": {
            "racePoints": ["9", "6", "4", "3", "2", "1"],
            "sharedDrivePolicy": "zero",
            "driversBestN": { "wholeSeason": 2 }
          },
          "rounds": [
            {
              "round": 1,
              "name": "South African Grand Prix",
              "date": "1967-01-02",
              "championship": true,
              "track": { "id": "kyalami_historic", "fallbacks": ["kyalami_2020"] },
              "laps": 80,
              "setupGuide": {
                "session": {
                  "opponents": 17,
                  "startTime": "14:30",
                  "date": "1967-01-02",
                  "weatherSlots": ["Clear"],
                  "timeProgression": "1x",
                  "mandatoryPitStop": false
                },
                "notes": "Kyalami rewards low wing; watch fuel load at altitude."
              },
              "guestEntries": [],
              "aiOverrides": {}
            },
            {
              "round": 2,
              "name": "Monaco Grand Prix",
              "date": "1967-05-07",
              "championship": true,
              "track": { "id": "monaco_1966" },
              "laps": 100,
              "setupGuide": {
                "session": { "opponents": 15, "weatherSlots": ["Clear", "Light Cloud"] }
              },
              "aiOverrides": { "driver.j_brabham": { "raceSkill": 0.95 } }
            }
          ]
        }
        """;

    private const string TeamsJson = """
        {
          "teams": [
            {
              "id": "team.brabham",
              "name": "Brabham-Repco",
              "carVehicleIds": ["formula_vintage_g1m2"],
              "performance": { "weightScalar": 1.000, "powerScalar": 0.998, "dragScalar": 1.002 },
              "reliability": 0.93,
              "prestige": 5,
              "budgetTier": 5
            }
          ]
        }
        """;

    private const string DriversJson = """
        {
          "drivers": [
            {
              "id": "driver.j_brabham",
              "name": "Jack Brabham",
              "country": "AUS",
              "born": 1926,
              "ratings": {
                "raceSkill": 0.93, "qualifyingSkill": 0.94, "aggression": 0.55, "defending": 0.42,
                "stamina": 0.79, "consistency": 0.80, "startReactions": 0.89, "wetSkill": 0.84,
                "tyreManagement": 0.79, "avoidanceOfMistakes": 0.71
              },
              "trackForm": { "kyalami_historic": 0.03 }
            },
            {
              "id": "driver.d_hulme",
              "name": "Denny Hulme",
              "country": "NZL",
              "born": 1936,
              "ratings": {
                "raceSkill": 0.90, "qualifyingSkill": 0.88, "aggression": 0.60, "defending": 0.55,
                "stamina": 0.85, "consistency": 0.86, "startReactions": 0.82, "wetSkill": 0.80,
                "tyreManagement": 0.83, "avoidanceOfMistakes": 0.78
              }
            }
          ]
        }
        """;

    private const string EntriesJson = """
        {
          "entries": [
            {
              "teamId": "team.brabham",
              "driverId": "driver.j_brabham",
              "number": "1",
              "rounds": "1-2",
              "ams2LiveryName": "Brabham-Repco #1 J. Brabham"
            },
            {
              "teamId": "team.brabham",
              "driverId": "driver.d_hulme",
              "number": "2",
              "rounds": "1,2",
              "ams2LiveryName": "Brabham-Repco #2 D. Hulme"
            }
          ]
        }
        """;

    private static SeasonPack ParseSynthetic() =>
        PackLoader.Parse(ManifestJson, SeasonJson, TeamsJson, DriversJson, EntriesJson);

    private static IReadOnlyList<Rational> Table(params long[] points) =>
        points.Select(p => new Rational(p)).ToArray();

    // ---------- parsing ----------

    [Fact]
    public void Parse_Manifest_ReadsIdentityAndRequirements()
    {
        var manifest = ParseSynthetic().Manifest;

        Assert.Equal("test-1967", manifest.PackId);
        Assert.Equal("Test Pack 1967", manifest.Name);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal(1, manifest.FormatVersion);
        Assert.Equal("1.6.9.8", manifest.GameVersionTested);
        Assert.Equal("CC BY 4.0", manifest.License);
        Assert.Single(manifest.Attribution);
        Assert.Empty(manifest.Requires.Dlc);

        var skinPack = Assert.Single(manifest.Requires.SkinPacks);
        Assert.Equal("F1 1967 Season (Alain Fry)", skinPack.Name);
        Assert.Equal("https://example.test/downloads/f1-1967", skinPack.Url);
        Assert.Equal("F1_Season_1967", skinPack.OverridesFolder);

        // v1.2 additive skinSeason: absent → null, and re-serializing does not invent the key
        // (a pack without it round-trips byte-identically).
        Assert.Null(manifest.SkinSeason);
        Assert.DoesNotContain("skinSeason",
            JsonSerializer.Serialize(manifest, CoreJson.Options));
    }

    [Fact]
    public void Parse_Manifest_ReadsTheOptionalSkinSeason_AndCareerStyle()
    {
        var manifest = PackLoader.ParseManifest("""
            {
              "packId": "smgp-1",
              "name": "Super Monaco GP",
              "version": "1.0.0",
              "formatVersion": 1,
              "skinSeason": "smgp",
              "careerStyle": "smgp"
            }
            """);

        Assert.Equal("smgp", manifest.SkinSeason);
        Assert.Equal("smgp", manifest.CareerStyle);
        // Absent on a normal pack → null, and never invented on re-serialize.
        var plain = PackLoader.ParseManifest("""
            { "packId": "f1-1985", "name": "Formula One 1985", "version": "1.0.0", "formatVersion": 1 }
            """);
        Assert.Null(plain.CareerStyle);
        Assert.DoesNotContain("careerStyle",
            JsonSerializer.Serialize(plain, CoreJson.Options));
    }

    [Fact]
    public void Parse_Season_ReadsCalendarAndCatalogShapedPointsSystem()
    {
        var season = ParseSynthetic().Season;

        Assert.Equal(1967, season.Year);
        Assert.Equal("Test World Championship", season.SeriesName);
        Assert.Equal("F-Vintage_Gen1", season.Ams2Class);

        // pointsSystem is EXACTLY the CatalogSeason shape and resolves like one.
        Assert.Equal(Table(9, 6, 4, 3, 2, 1), season.PointsSystem.RacePoints);
        Assert.Equal(SharedDrivePolicy.Zero, season.PointsSystem.SharedDrivePolicy);
        var system = season.PointsSystem.ResolvePointsSystem(season.Rounds.Count);
        Assert.Equal(
            new[] { new BestNSegment { FromRound = 1, ToRound = 2, Count = 2 } },
            system.DriversBestN!.Segments);

        Assert.Equal(2, season.Rounds.Count);

        var kyalami = season.Rounds[0];
        Assert.Equal(1, kyalami.Round);
        Assert.Equal("South African Grand Prix", kyalami.Name);
        Assert.Equal("1967-01-02", kyalami.Date);
        Assert.True(kyalami.Championship);
        Assert.Equal("kyalami_historic", kyalami.Track.Id);
        Assert.Equal(new[] { "kyalami_2020" }, kyalami.Track.Fallbacks);
        Assert.Equal(80, kyalami.Laps);
        Assert.NotNull(kyalami.SetupGuide);
        Assert.Equal(17, kyalami.SetupGuide!.Session.Opponents);
        Assert.Equal("14:30", kyalami.SetupGuide.Session.StartTime);
        Assert.Equal("1967-01-02", kyalami.SetupGuide.Session.Date);
        Assert.Equal(new[] { "Clear" }, kyalami.SetupGuide.Session.WeatherSlots);
        Assert.Equal("1x", kyalami.SetupGuide.Session.TimeProgression);
        Assert.False(kyalami.SetupGuide.Session.MandatoryPitStop);
        Assert.Equal("Kyalami rewards low wing; watch fuel load at altitude.", kyalami.SetupGuide.Notes);
        Assert.Empty(kyalami.GuestEntries);
        Assert.Empty(kyalami.AiOverrides);

        var monaco = season.Rounds[1];
        Assert.Empty(monaco.Track.Fallbacks);            // defaulted
        Assert.Null(monaco.SetupGuide!.Notes);           // optional
        var (overriddenDriver, patch) = Assert.Single(monaco.AiOverrides);
        Assert.Equal("driver.j_brabham", overriddenDriver);
        Assert.Equal(0.95, patch.RaceSkill);
        Assert.Null(patch.QualifyingSkill);              // partial patch
        Assert.Equal(("raceSkill", 0.95), Assert.Single(patch.Enumerate()));
    }

    [Fact]
    public void Parse_Teams_ReadsCarBindingAndScalars()
    {
        var team = Assert.Single(ParseSynthetic().Teams);

        Assert.Equal("team.brabham", team.Id);
        Assert.Equal("Brabham-Repco", team.Name);
        Assert.Equal(new[] { "formula_vintage_g1m2" }, team.CarVehicleIds);
        Assert.Equal(1.000, team.Performance.WeightScalar);
        Assert.Equal(0.998, team.Performance.PowerScalar);
        Assert.Equal(1.002, team.Performance.DragScalar);
        Assert.Equal(0.93, team.Reliability);
        Assert.Equal(5, team.Prestige);
        Assert.Equal(5, team.BudgetTier);
    }

    [Fact]
    public void Parse_Drivers_ReadsRatingsVocabularyAndTrackForm()
    {
        var drivers = ParseSynthetic().Drivers;

        Assert.Equal(2, drivers.Count);
        var brabham = drivers[0];
        Assert.Equal("driver.j_brabham", brabham.Id);
        Assert.Equal("Jack Brabham", brabham.Name);
        Assert.Equal("AUS", brabham.Country);
        Assert.Equal(1926, brabham.Born);
        Assert.Equal(0.93, brabham.Ratings.RaceSkill);
        Assert.Equal(0.71, brabham.Ratings.AvoidanceOfMistakes);
        Assert.Equal(10, brabham.Ratings.Enumerate().Count());
        Assert.Equal(0.03, brabham.TrackForm["kyalami_historic"]);

        Assert.Empty(drivers[1].TrackForm);              // defaulted
    }

    [Fact]
    public void Parse_Entries_ReadsRoundsRangesAndLiveryBindings()
    {
        var entries = ParseSynthetic().Entries;

        Assert.Equal(2, entries.Count);
        Assert.Equal("team.brabham", entries[0].TeamId);
        Assert.Equal("driver.j_brabham", entries[0].DriverId);
        Assert.Equal("1", entries[0].Number);
        Assert.Equal("1-2", entries[0].Rounds);
        Assert.Equal("Brabham-Repco #1 J. Brabham", entries[0].Ams2LiveryName);

        Assert.Equal(new[] { 1, 2 }, RoundsRange.Parse(entries[1].Rounds).Rounds);
    }

    [Fact]
    public void Parse_SyntheticPack_PassesStructuralValidationCleanly()
    {
        var report = PackStructuralValidator.Validate(ParseSynthetic());

        Assert.Empty(report.Issues);
        Assert.False(report.HasErrors);
    }

    // ---------- round-trip through CoreJson ----------

    [Fact]
    public void RoundTrip_SerializeAndReparse_IsStable()
    {
        var pack = ParseSynthetic();

        string manifest = JsonSerializer.Serialize(pack.Manifest, CoreJson.Options);
        string season = JsonSerializer.Serialize(pack.Season, CoreJson.Options);
        string teams = JsonSerializer.Serialize(new PackTeamsFile { Teams = pack.Teams }, CoreJson.Options);
        string drivers = JsonSerializer.Serialize(new PackDriversFile { Drivers = pack.Drivers }, CoreJson.Options);
        string entries = JsonSerializer.Serialize(new PackEntriesFile { Entries = pack.Entries }, CoreJson.Options);

        var reparsed = PackLoader.Parse(manifest, season, teams, drivers, entries);

        // Second serialization must be byte-identical to the first: nothing is lost or mutated.
        Assert.Equal(manifest, JsonSerializer.Serialize(reparsed.Manifest, CoreJson.Options));
        Assert.Equal(season, JsonSerializer.Serialize(reparsed.Season, CoreJson.Options));
        Assert.Equal(teams, JsonSerializer.Serialize(new PackTeamsFile { Teams = reparsed.Teams }, CoreJson.Options));
        Assert.Equal(drivers, JsonSerializer.Serialize(new PackDriversFile { Drivers = reparsed.Drivers }, CoreJson.Options));
        Assert.Equal(entries, JsonSerializer.Serialize(new PackEntriesFile { Entries = reparsed.Entries }, CoreJson.Options));

        // And the reparsed pack still validates cleanly.
        Assert.Empty(PackStructuralValidator.Validate(reparsed).Issues);
    }

    // ---------- error reporting names the file part ----------

    [Theory]
    [InlineData("pack.json")]
    [InlineData("season.json")]
    [InlineData("teams.json")]
    [InlineData("drivers.json")]
    [InlineData("entries.json")]
    public void Parse_MalformedPart_ThrowsJsonExceptionNamingTheFile(string brokenPart)
    {
        const string malformed = "{ this is not json";

        var ex = Assert.Throws<JsonException>(() => PackLoader.Parse(
            brokenPart == "pack.json" ? malformed : ManifestJson,
            brokenPart == "season.json" ? malformed : SeasonJson,
            brokenPart == "teams.json" ? malformed : TeamsJson,
            brokenPart == "drivers.json" ? malformed : DriversJson,
            brokenPart == "entries.json" ? malformed : EntriesJson));

        Assert.StartsWith($"{brokenPart}:", ex.Message);
    }

    [Fact]
    public void Parse_NullLiteralPart_ThrowsJsonExceptionNamingTheFile()
    {
        var ex = Assert.Throws<JsonException>(() =>
            PackLoader.Parse(ManifestJson, SeasonJson, TeamsJson, DriversJson, "null"));

        Assert.StartsWith("entries.json:", ex.Message);
    }

    [Fact]
    public void Parse_MissingRequiredProperty_ThrowsJsonExceptionNamingTheFile()
    {
        // A team without its required "name".
        const string teamsMissingName = """
            { "teams": [ { "id": "team.brabham", "carVehicleIds": ["formula_vintage_g1m2"] } ] }
            """;

        var ex = Assert.Throws<JsonException>(() =>
            PackLoader.Parse(ManifestJson, SeasonJson, teamsMissingName, DriversJson, EntriesJson));

        Assert.StartsWith("teams.json:", ex.Message);
    }
}
