using Companion.Ams2.CustomAi;

namespace Companion.Tests.Ams2;

/// <summary>
/// The lenient community-XML reader against a fixture authored to the exact SHAPE of the
/// real jusk F-Vintage_Gen1.xml (never the live install): malformed header comment with '--'
/// runs (a calendar table drawn in dashes), nonstandard <c>tracks =</c> attribute spacing,
/// raw unescaped ampersands in livery names, tab/space soup, trailing spaces after values,
/// and per-track override entries that must stay separate from base entries.
/// </summary>
public class CommunityAiReaderTests
{
    /// <summary>Jusk-shaped fixture. Quirks, in order of appearance: '--' run inside the
    /// header comment (illegal XML), 'tracks =' spacing, multi-track comma list, a raw '&amp;'
    /// in a livery name, a non-numeric stat, and an entry with no livery_name at all.</summary>
    private const string JuskShapeXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!--Custom AI by jusk - F1 1967 Season

        Changelog v0.3:
        - Added new vehicle_relaiability attributes
        Changelog v0.1:
        - Initial version to match Alain Fry's skinpack

        ***  1967 Season Calendar  ***
        #	Track				Date			AMS2 Track
        --------------------------------------------------------------------------------------------------
        1 	South African GP	02 Jan			Kyalami Historic 1976
        9 	Italian GP			10 Sept			Monza 1971
        -->
        <custom_ai_drivers>
        	<driver livery_name="Brabham-Repco #1 J. Brabham">
        		<name>Jack Brabham</name>
        		<country>AUS</country>
                <race_skill>0.93</race_skill>
                <qualifying_skill>0.94</qualifying_skill>
        		<aggression>0.55</aggression>
                <defending>0.42</defending>
                <stamina>0.79</stamina>
                <consistency>0.80</consistency>
                <start_reactions>0.89</start_reactions>
                <wet_skill>0.84</wet_skill>
                <tyre_management>0.79</tyre_management>
                <blue_flag_conceding>0.88</blue_flag_conceding>
                <weather_tyre_changes>0.64</weather_tyre_changes>
                <avoidance_of_mistakes>0.71</avoidance_of_mistakes>
                <avoidance_of_forced_mistakes>0.62</avoidance_of_forced_mistakes>
        	    <vehicle_reliability>0.93</vehicle_reliability>
        	</driver>
        	<driver livery_name="Brabham-Repco #1 J. Brabham" tracks="Kyalami_Historic">
                <qualifying_skill>0.98</qualifying_skill>
                <consistency>0.90</consistency>
        	</driver>
        	<driver livery_name="BRM #3 J. Stewart" tracks="Spa_Francorchamps_1993,Nordschleife_2020,Nordschleife_2020_24hr">
                <race_skill>0.95</race_skill>
        	</driver>
        	<driver livery_name="Eagle-Climax #22 R. Ginther" tracks ="Monza_1971">
                <race_skill>0.49</race_skill>
                <qualifying_skill>0.50</qualifying_skill>
        	</driver>
        	<driver livery_name="Surtees Bang & Olufsen - C. Pace #18">
        		<name>Carlos Pace</name>
        		<country>BRA</country>
                <race_skill>0.88</race_skill>
                <weight_scalar>1.015</weight_scalar>
        	</driver>
        	<driver livery_name="Honda #7 J. Surtees">
        		<name>John Surtees</name>
                <race_skill>quick</race_skill>
                <qualifying_skill>0.91</qualifying_skill>
        	</driver>
        	<driver>
                <race_skill>0.50</race_skill>
        	</driver>
        </custom_ai_drivers>
        """;

    // ---------- lenient parse of the quirky shape ----------

    [Fact]
    public void Parse_JuskShape_SurvivesMalformedCommentAndSeparatesBaseFromTrackEntries()
    {
        var file = CommunityAiReader.Parse(JuskShapeXml);

        // Base entries: Brabham, Pace (raw &), Surtees. The livery-less entry is skipped.
        Assert.Equal(3, file.BaseEntries.Count);
        Assert.Equal(
            ["Brabham-Repco #1 J. Brabham", "Surtees Bang & Olufsen - C. Pace #18", "Honda #7 J. Surtees"],
            file.BaseEntries.Select(e => e.LiveryName));

        // Track-scoped entries stay separate, they are round-level, never baseline.
        Assert.Equal(3, file.TrackEntries.Count);
        Assert.All(file.TrackEntries, e => Assert.NotEmpty(e.Tracks));
        Assert.All(file.BaseEntries, e => Assert.Empty(e.Tracks));
    }

    [Fact]
    public void Parse_BaseEntry_CarriesNameCountryAndEveryRating()
    {
        var file = CommunityAiReader.Parse(JuskShapeXml);
        var brabham = file.BaseEntries[0];

        Assert.Equal("Jack Brabham", brabham.Name);
        Assert.Equal("AUS", brabham.Country);
        Assert.Equal(0.93, brabham.RaceSkill);
        Assert.Equal(0.94, brabham.QualifyingSkill);
        Assert.Equal(0.55, brabham.Aggression);
        Assert.Equal(0.42, brabham.Defending);
        Assert.Equal(0.79, brabham.Stamina);
        Assert.Equal(0.80, brabham.Consistency);
        Assert.Equal(0.89, brabham.StartReactions);
        Assert.Equal(0.84, brabham.WetSkill);
        Assert.Equal(0.79, brabham.TyreManagement);
        Assert.Equal(0.88, brabham.BlueFlagConceding);
        Assert.Equal(0.64, brabham.WeatherTyreChanges);
        Assert.Equal(0.71, brabham.AvoidanceOfMistakes);
        Assert.Equal(0.62, brabham.AvoidanceOfForcedMistakes);
        Assert.Equal(0.93, brabham.VehicleReliability);

        // Fields the file omits stay null (stock defaults apply in-game).
        Assert.Null(brabham.FuelManagement);
        Assert.Null(brabham.WeightScalar);
        Assert.Null(brabham.PowerScalar);
        Assert.Null(brabham.DragScalar);
    }

    [Fact]
    public void Parse_TrackEntries_ParseSingleSpacedAndCommaSeparatedTrackLists()
    {
        var file = CommunityAiReader.Parse(JuskShapeXml);

        var kyalami = file.TrackEntries[0];
        Assert.Equal("Brabham-Repco #1 J. Brabham", kyalami.LiveryName);
        Assert.Equal(["Kyalami_Historic"], kyalami.Tracks);
        Assert.Equal(0.98, kyalami.QualifyingSkill);
        Assert.Equal(0.90, kyalami.Consistency);
        Assert.Null(kyalami.RaceSkill); // partial override: absent fields inherit in-game

        var stewart = file.TrackEntries[1];
        Assert.Equal(
            ["Spa_Francorchamps_1993", "Nordschleife_2020", "Nordschleife_2020_24hr"],
            stewart.Tracks);

        // The nonstandard 'tracks =' spacing (real jusk quirk) parses like any other entry.
        var ginther = file.TrackEntries[2];
        Assert.Equal("Eagle-Climax #22 R. Ginther", ginther.LiveryName);
        Assert.Equal(["Monza_1971"], ginther.Tracks);
        Assert.Equal(0.49, ginther.RaceSkill);
    }

    [Fact]
    public void Parse_RawAmpersandInLiveryName_IsRepairedNotFatal()
    {
        var file = CommunityAiReader.Parse(JuskShapeXml);
        var pace = file.BaseEntries[1];

        Assert.Equal("Surtees Bang & Olufsen - C. Pace #18", pace.LiveryName);
        Assert.Equal("Carlos Pace", pace.Name);
        Assert.Equal(0.88, pace.RaceSkill);
        Assert.Equal(1.015, pace.WeightScalar);

        // A properly-escaped entity must NOT be double-escaped by the repair.
        var escaped = CommunityAiReader.Parse(
            "<custom_ai_drivers><driver livery_name=\"Pr&#233;cieux &amp; Co #7\"/></custom_ai_drivers>");
        Assert.Equal("Précieux & Co #7", Assert.Single(escaped.BaseEntries).LiveryName);
    }

    [Fact]
    public void Parse_Oddities_DegradeToWarningsNeverExceptions()
    {
        var file = CommunityAiReader.Parse(JuskShapeXml);

        // Non-numeric stat: value ignored, the rest of the entry survives.
        var surtees = file.BaseEntries[2];
        Assert.Null(surtees.RaceSkill);
        Assert.Equal(0.91, surtees.QualifyingSkill);
        Assert.Contains(file.Warnings, w => w.Contains("race_skill") && w.Contains("quick"));

        // Livery-less entry: skipped with a warning, not thrown.
        Assert.Contains(file.Warnings, w => w.Contains("livery_name"));
    }

    [Fact]
    public void BaseEntriesByLivery_DuplicateBaseEntries_LastOneWins()
    {
        var file = CommunityAiReader.Parse("""
            <custom_ai_drivers>
              <driver livery_name="Team A #1"><race_skill>0.50</race_skill></driver>
              <driver livery_name="Team A #1"><race_skill>0.60</race_skill></driver>
            </custom_ai_drivers>
            """);

        Assert.Equal(0.60, file.BaseEntriesByLivery()["Team A #1"].RaceSkill);
    }

    // ---------- file access ----------

    [Fact]
    public void ReadFile_ParsesTheSameShapeFromDisk()
    {
        string path = Path.Combine(TempDir(), "F-Vintage_Gen1.xml");
        File.WriteAllText(path, JuskShapeXml);
        try
        {
            var file = CommunityAiReader.ReadFile(path);
            Assert.Equal(3, file.BaseEntries.Count);
            Assert.Equal(3, file.TrackEntries.Count);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void TryReadFile_HopelessMarkupOrMissingFile_ReturnsNull()
    {
        string dir = TempDir();
        try
        {
            Assert.Null(CommunityAiReader.TryReadFile(Path.Combine(dir, "does-not-exist.xml")));

            string hopeless = Path.Combine(dir, "broken.xml");
            File.WriteAllText(hopeless, "<custom_ai_drivers><driver livery_name=");
            Assert.Null(CommunityAiReader.TryReadFile(hopeless));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Parse_HopelessMarkup_ThrowsInvalidOperation_WithLenientWording()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CommunityAiReader.Parse("<custom_ai_drivers><driver livery_name="));
        Assert.Contains("lenient", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string TempDir()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "companion-communityai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
