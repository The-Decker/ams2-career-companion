using System.Globalization;
using System.Xml.Linq;
using Companion.Ams2.ContentLibrary;
using Companion.Ams2.CustomAi;
using Companion.Ams2.Grid;
using Companion.Core.Grid;
using Companion.Core.Packs;

namespace Companion.Tests.Grid;

/// <summary>
/// GridStager contract: 1:1 field mapping from a resolved plan to the custom-AI model (exact
/// float round-trip, scalars only when != 1.0, no per-track entries in v1), backup-then-write
/// staging with restore round-trip, and dry-run output that re-parses as the writer's XML.
/// </summary>
public class GridStagerTests
{
    // ---------- Build: field mapping ----------

    [Fact]
    public void Build_MapsEverySeatFieldOntoTheCustomAiDriver_Exactly()
    {
        var seat = Seat() with
        {
            Ratings = new PackDriverRatings
            {
                RaceSkill = 0.9375,
                QualifyingSkill = 0.8125,
                Aggression = 0.5625,
                Defending = 0.4375,
                Stamina = 0.6875,
                Consistency = 0.3125,
                StartReactions = 0.1875,
                WetSkill = 0.0625,
                TyreManagement = 0.7125,
                AvoidanceOfMistakes = 0.2625,
            },
            Reliability = 0.9325,
            WeightScalar = 0.985,
            PowerScalar = 1.015,
            DragScalar = 0.9975,
        };

        var file = GridStager.Build(Plan(seat), "unit test");

        Assert.Equal("F-Vintage_Gen1", file.VehicleClass);
        var driver = Assert.Single(file.Drivers);

        Assert.Equal("Team A #1", driver.LiveryName);
        Assert.Equal("Driver One", driver.Name);
        Assert.Equal("TST", driver.Country);

        // Exact doubles, no transformation on the way through.
        Assert.Equal(0.9375, driver.RaceSkill);
        Assert.Equal(0.8125, driver.QualifyingSkill);
        Assert.Equal(0.5625, driver.Aggression);
        Assert.Equal(0.4375, driver.Defending);
        Assert.Equal(0.6875, driver.Stamina);
        Assert.Equal(0.3125, driver.Consistency);
        Assert.Equal(0.1875, driver.StartReactions);
        Assert.Equal(0.0625, driver.WetSkill);
        Assert.Equal(0.7125, driver.TyreManagement);
        Assert.Equal(0.2625, driver.AvoidanceOfMistakes);

        // Team reliability -> vehicle_reliability; scalars carried because they differ from 1.0.
        Assert.Equal(0.9325, driver.VehicleReliability);
        Assert.Equal(0.985, driver.WeightScalar);
        Assert.Equal(1.015, driver.PowerScalar);
        Assert.Equal(0.9975, driver.DragScalar);

        // Fields the v1 pack vocabulary does not author stay null (game defaults apply).
        Assert.Null(driver.FuelManagement);
        Assert.Null(driver.BlueFlagConceding);
        Assert.Null(driver.WeatherTyreChanges);
        Assert.Null(driver.AvoidanceOfForcedMistakes);
        Assert.Null(driver.SetupDownforce);
        Assert.Null(driver.SetupDownforceRandomness);

        // v1 regenerates the file before every round: no per-track override entries.
        Assert.Empty(driver.Tracks);
    }

    [Fact]
    public void Build_OmitsNeutralScalars_AndKeepsNonNeutralOnes()
    {
        var neutral = Seat() with { WeightScalar = 1.0, PowerScalar = 1.0, DragScalar = 1.0 };
        var tuned = Seat() with
        {
            Ams2LiveryName = "Team B #2",
            WeightScalar = 1.0,
            PowerScalar = 0.97,
            DragScalar = 1.03,
        };

        var file = GridStager.Build(Plan(neutral, tuned));

        Assert.Null(file.Drivers[0].WeightScalar);
        Assert.Null(file.Drivers[0].PowerScalar);
        Assert.Null(file.Drivers[0].DragScalar);

        Assert.Null(file.Drivers[1].WeightScalar);   // per-scalar, not per-seat
        Assert.Equal(0.97, file.Drivers[1].PowerScalar);
        Assert.Equal(1.03, file.Drivers[1].DragScalar);
    }

    [Fact]
    public void Build_IncludesThePlayerSeat_WithSkillsAndScalars()
    {
        var player = Seat() with { IsPlayer = true, PowerScalar = 0.96 };

        var file = GridStager.Build(Plan(player));

        // The player's entry is IN the file: the team scalars must apply to their car, and the
        // AI skill fields are inert while the player drives the livery.
        var driver = Assert.Single(file.Drivers);
        Assert.Equal("Team A #1", driver.LiveryName);
        Assert.NotNull(driver.RaceSkill);
        Assert.Equal(0.96, driver.PowerScalar);
    }

    [Fact]
    public void Build_HeaderCommentCarriesTheGeneratedMarkerAndCallerText()
    {
        var file = GridStager.Build(Plan(Seat()), "f1-1967 round 1");

        Assert.Contains(GridStager.GeneratedMarker, file.HeaderComment);
        Assert.Contains("f1-1967 round 1", file.HeaderComment);

        var bare = GridStager.Build(Plan(Seat()));
        Assert.Contains(GridStager.GeneratedMarker, bare.HeaderComment);
    }

    // ---------- Preflight: delegation against the real library ----------

    [Fact]
    public void Preflight_Resolved1967Round1_HasNoErrorsAgainstTheRealLibrary()
    {
        var pack = GridTestData.LoadReferencePack("f1-1967");
        var plan = RoundGridResolver.Resolve(pack, 1);
        var file = GridStager.Build(plan, "preflight test");
        var library = Ams2ContentLibrary.Load(GridTestData.Ams2DataDirectory);

        var report = GridStager.Preflight(file, library, installedLiveries: [], plan.TrackId, plan.Seats.Count);

        // With no skin packs installed the livery misses are warnings by design; anything the
        // preflight calls an ERROR (class casing, duplicate livery, AI cap) is a pipeline bug.
        Assert.False(report.HasErrors,
            string.Join("\n", report.Issues.Select(i => $"{i.Severity}: {i.Message}")));
    }

    [Fact]
    public void Preflight_GridBiggerThanTheVenueAiCap_IsAnError()
    {
        var pack = GridTestData.LoadReferencePack("f1-1967");
        var plan = RoundGridResolver.Resolve(pack, 1);
        var file = GridStager.Build(plan);
        var library = Ams2ContentLibrary.Load(GridTestData.Ams2DataDirectory);

        var report = GridStager.Preflight(file, library, installedLiveries: [], plan.TrackId, gridSize: 999);

        Assert.True(report.HasErrors);
        Assert.Contains(report.Issues, i => i.Message.Contains("AI cap"));
    }

    // ---------- Stage: backup FIRST, then write ----------

    [Fact]
    public void Stage_IntoAnEmptyDirectory_WritesTheFileWithNoBackup()
    {
        using var dir = new TempDir();
        var file = GridStager.Build(Plan(Seat()), "stage test");

        var result = GridStager.Stage(file, dir.Path, Timestamp(0));

        Assert.Null(result.BackupPath);
        Assert.Equal(Path.Combine(dir.Path, "F-Vintage_Gen1.xml"), result.WrittenPath);
        Assert.Equal(CustomAiXmlWriter.ToXml(file), File.ReadAllText(result.WrittenPath));
        Assert.Contains("F-Vintage_Gen1.xml", result.Report);
    }

    [Fact]
    public void Stage_OverAForeignFile_RequiresForce_ThenBacksUpBeforeWriting()
    {
        using var dir = new TempDir();
        string target = Path.Combine(dir.Path, "F-Vintage_Gen1.xml");
        const string userContent = "<custom_ai_drivers><!-- the user's own NAMeS file --></custom_ai_drivers>";
        File.WriteAllText(target, userContent);

        var file = GridStager.Build(Plan(Seat()), "stage test");

        // Without force: the user's curated file is protected, and NOTHING was touched.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            GridStager.Stage(file, dir.Path, Timestamp(0)));
        Assert.Contains("force", ex.Message);
        Assert.Equal(userContent, File.ReadAllText(target));
        Assert.Empty(new CustomAiBackup(dir.Path).ListBackups("F-Vintage_Gen1"));

        // With force: the ORIGINAL content is in the backup (backup ran before the write),
        // and the target now holds the generated file.
        var result = GridStager.Stage(file, dir.Path, Timestamp(1), force: true);

        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Equal(userContent, File.ReadAllText(result.BackupPath!));
        Assert.Equal(CustomAiXmlWriter.ToXml(file), File.ReadAllText(target));
    }

    [Fact]
    public void Stage_OverAnAppGeneratedFile_NeedsNoForce_ButStillBacksUp()
    {
        using var dir = new TempDir();
        var first = GridStager.Build(Plan(Seat()), "round 1");
        var second = GridStager.Build(Plan(Seat() with { DriverName = "Driver Two" }), "round 2");

        GridStager.Stage(first, dir.Path, Timestamp(0));
        var result = GridStager.Stage(second, dir.Path, Timestamp(1));

        Assert.NotNull(result.BackupPath);
        Assert.Equal(CustomAiXmlWriter.ToXml(first), File.ReadAllText(result.BackupPath!));
        Assert.Equal(CustomAiXmlWriter.ToXml(second), File.ReadAllText(result.WrittenPath));
    }

    [Fact]
    public void Stage_ThenRestoreLatest_RoundTripsTheOriginalFileExactly()
    {
        using var dir = new TempDir();
        string target = Path.Combine(dir.Path, "F-Vintage_Gen1.xml");
        const string userContent = "<custom_ai_drivers><driver livery_name=\"Précieux &amp; Co #7\"/></custom_ai_drivers>";
        File.WriteAllText(target, userContent);

        var file = GridStager.Build(Plan(Seat()), "stage test");
        GridStager.Stage(file, dir.Path, Timestamp(0), force: true);
        Assert.NotEqual(userContent, File.ReadAllText(target));

        Assert.True(new CustomAiBackup(dir.Path).RestoreLatest("F-Vintage_Gen1"));
        Assert.Equal(userContent, File.ReadAllText(target));
    }

    // ---------- DryRun: valid XML the writer's dialect re-parses ----------

    [Fact]
    public void DryRun_WritesTheWriterXml_ThatReparsesWithEveryFieldIntact()
    {
        using var dir = new TempDir();
        var seat = Seat() with
        {
            Ams2LiveryName = "Matra-Ford Cosworth #20 J-P. Beltoise & Co",  // XML-escaping case
            Reliability = 0.9325,
            PowerScalar = 1.015,
        };
        var file = GridStager.Build(Plan(seat), "dry-run test");

        string written = GridStager.DryRun(file, dir.Path);

        Assert.Equal(Path.Combine(dir.Path, "F-Vintage_Gen1.xml"), written);
        // No backup infrastructure appears for a scratch write.
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "_companion-backups")));

        // Byte-for-byte the writer's output...
        string text = File.ReadAllText(written);
        Assert.Equal(CustomAiXmlWriter.ToXml(file), text);

        // ...whose declaration tells the truth about the bytes on disk (a utf-16 declaration
        // over UTF-8 bytes makes strict parsers reject the file)...
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", text);

        // ...and it re-parses FROM DISK — byte-level decode, not just string parse: root
        // element, one driver, exact livery binding, and stat values that round-trip through
        // the writer's "0.0###" format.
        var doc = XDocument.Load(written);
        Assert.Equal("custom_ai_drivers", doc.Root!.Name.LocalName);

        var driver = Assert.Single(doc.Root.Elements("driver"));
        Assert.Equal(seat.Ams2LiveryName, driver.Attribute("livery_name")!.Value);
        Assert.Null(driver.Attribute("tracks"));
        Assert.Equal("Driver One", driver.Element("name")!.Value);
        Assert.Equal("TST", driver.Element("country")!.Value);

        Assert.Equal(seat.Ratings.RaceSkill, ParseStat(driver, "race_skill"));
        Assert.Equal(seat.Ratings.QualifyingSkill, ParseStat(driver, "qualifying_skill"));
        Assert.Equal(seat.Reliability, ParseStat(driver, "vehicle_reliability"));
        Assert.Equal(seat.PowerScalar, ParseStat(driver, "power_scalar"));
        Assert.Null(driver.Element("weight_scalar"));   // neutral scalar omitted
        Assert.Null(driver.Element("drag_scalar"));
    }

    [Fact]
    public void DryRun_Resolved1988Round11_ProducesAParsableGridWithBrundleInTheWilliams()
    {
        using var dir = new TempDir();
        var pack = GridTestData.LoadReferencePack("f1-1988");
        var plan = RoundGridResolver.Resolve(pack, 11);
        var file = GridStager.Build(plan, "1988 round 11");

        string written = GridStager.DryRun(file, dir.Path);

        var doc = XDocument.Load(written);
        var liveries = doc.Root!.Elements("driver")
            .Select(d => d.Attribute("livery_name")!.Value)
            .ToList();

        Assert.Equal(plan.Seats.Count, liveries.Count);
        Assert.Contains("1988 Williams #5 - M. Brundle", liveries);
        Assert.DoesNotContain("1988 Williams #5 - N. Mansell", liveries);
        // Every livery is unique — the duplicate-livery gate held.
        Assert.Equal(liveries.Count, liveries.Distinct(StringComparer.Ordinal).Count());
        // The grid-cap fix: Martini (Minardi #23) DNQ'd the 1988 Belgian GP in the preset-matched
        // grids, so he is NOT on round 11's grid even though his entry covers the round — only the
        // round's listed starters seat.
        Assert.DoesNotContain("1988 Minardi #23 - P. Martini", liveries);
    }

    [Fact]
    public void DryRun_Resolved1988Round1_NonAsciiLiverySurvivesUtf8RoundTrip()
    {
        using var dir = new TempDir();
        var pack = GridTestData.LoadReferencePack("f1-1988");
        // Round 1 (Brazil): Pérez-Sala started (DNF), so his accented livery is on the grid and
        // must survive UTF-8 round-tripping through the written custom-AI file.
        var plan = RoundGridResolver.Resolve(pack, 1);
        var file = GridStager.Build(plan, "1988 round 1");

        string written = GridStager.DryRun(file, dir.Path);

        var liveries = XDocument.Load(written).Root!.Elements("driver")
            .Select(d => d.Attribute("livery_name")!.Value)
            .ToList();

        Assert.Contains("1988 Minardi #24 - L. Pérez-Sala", liveries);
    }

    // ---------- helpers ----------

    // ---------- Build: staging-only per-race form nudge ----------

    [Fact]
    public void Build_AppliesRoundFormAsAStagingOnlyNudge_Clamped()
    {
        var seat = Seat() with
        {
            Ratings = new PackDriverRatings { RaceSkill = 0.80, QualifyingSkill = 0.75 },
        };

        // No form => baseline verbatim (byte-identical to the no-form path).
        var plain = Assert.Single(GridStager.Build(Plan(seat), "t").Drivers);
        Assert.Equal(0.80, plain.RaceSkill!.Value, 6);
        Assert.Equal(0.75, plain.QualifyingSkill!.Value, 6);

        // A positive form nudges race up, negative nudges quali down (additive on the two pace fields).
        var form = new Dictionary<string, PackDriverForm>
        {
            ["driver.one"] = new() { RaceSkill = 0.06, QualifyingSkill = -0.05 },
        };
        var nudged = Assert.Single(GridStager.Build(Plan(seat), "t", form).Drivers);
        Assert.Equal(0.86, nudged.RaceSkill!.Value, 6);
        Assert.Equal(0.70, nudged.QualifyingSkill!.Value, 6);

        // The nudge clamps into 0..1; a driver absent from the map is untouched.
        var edge = seat with { Ratings = new PackDriverRatings { RaceSkill = 0.98, QualifyingSkill = 0.05 } };
        var edgeForm = new Dictionary<string, PackDriverForm>
        {
            ["driver.one"] = new() { RaceSkill = 0.10, QualifyingSkill = -0.20 },
            ["driver.absent"] = new() { RaceSkill = 0.5, QualifyingSkill = 0.5 },
        };
        var clamped = Assert.Single(GridStager.Build(Plan(edge), "t", edgeForm).Drivers);
        Assert.Equal(1.0, clamped.RaceSkill!.Value, 6);    // 0.98 + 0.10 -> clamp 1.0
        Assert.Equal(0.0, clamped.QualifyingSkill!.Value, 6); // 0.05 - 0.20 -> clamp 0.0
    }

    private static GridSeat Seat() => new()
    {
        DriverId = "driver.one",
        DriverName = "Driver One",
        Country = "TST",
        TeamId = "team.a",
        TeamName = "Team A",
        Number = "1",
        Ams2LiveryName = "Team A #1",
        Ratings = GridTestData.Ratings(),
        Reliability = 0.90,
        WeightScalar = 1.0,
        PowerScalar = 1.0,
        DragScalar = 1.0,
    };

    private static GridPlan Plan(params GridSeat[] seats) => new()
    {
        PackId = "test-pack",
        Year = 1967,
        SeriesName = "Test Series",
        Ams2Class = "F-Vintage_Gen1",
        Round = 1,
        RoundName = "Test Grand Prix",
        TrackId = "kyalami_historic",
        Seats = seats,
    };

    private static DateTimeOffset Timestamp(int minutes) =>
        new DateTimeOffset(2026, 7, 2, 15, 0, 0, TimeSpan.Zero).AddMinutes(minutes);

    private static double ParseStat(XElement driver, string element) =>
        double.Parse(driver.Element(element)!.Value, CultureInfo.InvariantCulture);

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "companion-grid-tests", Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a stuck handle must not fail the test run.
            }
        }
    }
}
