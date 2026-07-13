using System.Globalization;
using System.Xml.Linq;
using Companion.Ams2.CustomAi;
using Companion.Ams2.Grid;
using Companion.Core.Grid;
using Companion.Core.Packs;

namespace Companion.Tests.Grid;

/// <summary>
/// NAMeS-primary staging (Mike's requirement: the installed names/AI mod is PRIMARY if found —
/// "found before overwritten"). When a temp fake install already has a class AI file that
/// defines a seat's livery, staging prefers that file's NAME + base AI ratings over the pinned
/// pack's (potentially stale) values, applying only the career/round delta on top. The
/// diff-aware no-op and backup-first contracts are preserved through the merge path.
/// </summary>
public class GridStagerNamesPrimaryTests
{
    private const string Livery = "Dallara-Ford #21 G. Morbidelli";

    // The user's installed community AI file: curated NAME + ratings that DIFFER from the
    // pinned pack's stale values below. This is the authority that must survive.
    private const string InstalledXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!--Custom AI by the community - 1990 F-Classic_Gen3
        ----------------------------------------------------
        -->
        <custom_ai_drivers>
        	<driver livery_name="Dallara-Ford #21 G. Morbidelli">
        		<name>Gianni Morbidelli</name>
        		<country>ITA</country>
                <race_skill>0.72</race_skill>
                <qualifying_skill>0.70</qualifying_skill>
                <aggression>0.55</aggression>
                <vehicle_reliability>0.90</vehicle_reliability>
                <weight_scalar>0.94</weight_scalar>
                <power_scalar>1.06</power_scalar>
                <drag_scalar>0.97</drag_scalar>
        	</driver>
        </custom_ai_drivers>
        """;

    // ---------- installed name + ratings are PRIMARY over stale pinned-pack values ----------

    [Fact]
    public void Stage_PrefersInstalledNameAndRatings_OverStalePinnedPackValues()
    {
        using var dir = new TempInstall(InstalledXml);
        string classPath = Path.Combine(dir.Path, "F-Classic_Gen3.xml");

        // The pinned pack ships STALE values for this livery — the same the generated round
        // carries (no career change this round). The installed file already defines the driver.
        var packBaseline = BaselineDriver(
            name: "G. Morbidelli", raceSkill: 0.50, qualifying: 0.50, aggression: 0.50);
        var generated = Build(Seat(name: "G. Morbidelli", raceSkill: 0.50, qualifying: 0.50, aggression: 0.50));

        var before = File.ReadAllText(classPath);

        var result = GridStager.Stage(
            generated, dir.Path, Timestamp(0),
            packBaselineByLivery: SingleBaseline(packBaseline));

        // NAMeS-primary: the installed name/ratings are kept, the stale pinned values are NEVER
        // written over them — so with no career delta this collapses to a diff-aware no-op.
        Assert.True(result.NoOpAlreadyMatches);
        Assert.Null(result.BackupPath);
        Assert.Equal(before, File.ReadAllText(classPath)); // installed file untouched, byte-for-byte

        // And the stale pinned name/ratings are provably NOT in the file the game reads (parsed
        // leniently — the untouched community file still has its malformed dashed comment).
        var onDisk = CommunityAiReader.ReadFile(classPath).BaseEntries.Single();
        Assert.Equal("Gianni Morbidelli", onDisk.Name);
        Assert.Equal(0.72, onDisk.RaceSkill!.Value, 3);
    }

    [Fact]
    public void Stage_AppliesCareerDeltaOverInstalledPrimary_NotStalePinnedValue()
    {
        using var dir = new TempInstall(InstalledXml);

        // Pinned baseline race_skill 0.50; the generated round bumped it to 0.60 (a +0.10
        // career/round delta). The installed primary is 0.72, so the result must be 0.72 + 0.10.
        var packBaseline = BaselineDriver(name: "G. Morbidelli", raceSkill: 0.50, qualifying: 0.70, aggression: 0.55);
        var generated = Build(Seat(name: "G. Morbidelli", raceSkill: 0.60, qualifying: 0.70, aggression: 0.55));

        var result = GridStager.Stage(
            generated, dir.Path, Timestamp(0), force: true,
            packBaselineByLivery: SingleBaseline(packBaseline));

        var written = XDocument.Load(result.WrittenPath).Root!.Elements("driver").Single();
        // Delta applied over the INSTALLED value (0.72 + 0.10), never the stale 0.50/0.60.
        Assert.Equal(0.82, Stat(written, "race_skill"), 3);
        // Fields with no career delta keep the installed value verbatim.
        Assert.Equal(0.70, Stat(written, "qualifying_skill"), 3);
        Assert.Equal("Gianni Morbidelli", written.Element("name")!.Value);
    }

    [Fact]
    public void Stage_AuthoritativeNeutralPlayerScalars_ReplaceInstalledTuning_AndKeepCuratedDriver()
    {
        using var dir = new TempInstall(InstalledXml);

        var packBaseline = BaselineDriver(
            name: "G. Morbidelli", raceSkill: 0.50, qualifying: 0.50, aggression: 0.50);
        var generated = Build(Seat(
            name: "G. Morbidelli", raceSkill: 0.50, qualifying: 0.50, aggression: 0.50) with
        {
            IsPlayer = true,
            PlayerCarScalarsAuthoritative = true,
            WeightScalar = 1.0,
            PowerScalar = 1.0,
            DragScalar = 1.0,
        });

        var result = GridStager.Stage(
            generated, dir.Path, Timestamp(0), force: true,
            packBaselineByLivery: SingleBaseline(packBaseline));

        Assert.False(result.NoOpAlreadyMatches);
        Assert.NotNull(result.BackupPath);
        var written = XDocument.Load(result.WrittenPath).Root!.Elements("driver").Single();
        Assert.Equal("Gianni Morbidelli", written.Element("name")!.Value);
        Assert.Equal(0.72, Stat(written, "race_skill"), 3);
        Assert.Equal(0.70, Stat(written, "qualifying_skill"), 3);
        Assert.Equal(1.0, Stat(written, "weight_scalar"));
        Assert.Equal(1.0, Stat(written, "power_scalar"));
        Assert.Equal(1.0, Stat(written, "drag_scalar"));
    }

    [Fact]
    public void Stage_NoCareerChange_MergesToInstalled_AndIsANoOp()
    {
        using var dir = new TempInstall(InstalledXml);

        // Pinned baseline == generated (no career change). With the installed file made primary,
        // the merged file equals the installed file — a NO-OP, nothing written, nothing backed up.
        var packBaseline = BaselineDriver(name: "G. Morbidelli", raceSkill: 0.50, qualifying: 0.50, aggression: 0.50);
        var generated = Build(Seat(name: "G. Morbidelli", raceSkill: 0.50, qualifying: 0.50, aggression: 0.50));

        var before = File.ReadAllText(Path.Combine(dir.Path, "F-Classic_Gen3.xml"));

        var result = GridStager.Stage(
            generated, dir.Path, Timestamp(0),
            packBaselineByLivery: SingleBaseline(packBaseline));

        Assert.True(result.NoOpAlreadyMatches);
        Assert.Null(result.BackupPath);
        Assert.Contains("installed names/AI are kept", result.Report);
        // The installed file is byte-identical afterwards, and no backup was taken.
        Assert.Equal(before, File.ReadAllText(Path.Combine(dir.Path, "F-Classic_Gen3.xml")));
        Assert.Empty(new CustomAiBackup(dir.Path).ListBackups("F-Classic_Gen3"));
    }

    [Fact]
    public void Stage_SeatNotInInstalledFile_PassesThroughUnmerged()
    {
        using var dir = new TempInstall(InstalledXml);

        // A seat whose livery the installed file does NOT define: the generated values stand.
        var packBaseline = BaselineDriver(name: "Someone", raceSkill: 0.40, qualifying: 0.40, aggression: 0.40);
        var generated = Build(Seat(
            livery: "Other-Team #33 Someone", name: "Someone",
            raceSkill: 0.40, qualifying: 0.40, aggression: 0.40));

        var result = GridStager.Stage(
            generated, dir.Path, Timestamp(0), force: true,
            packBaselineByLivery: SingleBaseline(packBaseline, "Other-Team #33 Someone"));

        var written = XDocument.Load(result.WrittenPath).Root!.Elements("driver").Single();
        Assert.Equal("Other-Team #33 Someone", written.Attribute("livery_name")!.Value);
        Assert.Equal(0.40, Stat(written, "race_skill"), 3);
    }

    [Fact]
    public void Stage_MergeIsOptIn_NoBaseline_WritesGeneratedVerbatim()
    {
        using var dir = new TempInstall(InstalledXml);

        // The NAMeS-primary merge is OPT-IN: the low-level Stage without a pinned-pack baseline
        // (dry-run / legacy callers) writes the generated file verbatim, exactly as before. The
        // live staging service (CareerSessionService) always supplies the baseline, so Mike's
        // install is always protected — this only guards the low-level API's back-compat.
        var generated = Build(Seat(name: "G. Morbidelli", raceSkill: 0.50, qualifying: 0.50, aggression: 0.50));

        var result = GridStager.Stage(generated, dir.Path, Timestamp(0), force: true);

        var written = XDocument.Load(result.WrittenPath).Root!.Elements("driver").Single();
        Assert.Equal(0.50, Stat(written, "race_skill"), 3); // generated value, no merge
    }

    [Fact]
    public void MergeInstalledPrimary_KeepsInstalledNameAndRatings_ForKnownSeat()
    {
        // Direct unit of the merge: with a baseline, a seat the installed file defines keeps the
        // installed name + ratings when there is no career delta (generated == baseline).
        var installed = CommunityAiReader.Parse(InstalledXml);
        var packBaseline = SingleBaseline(
            BaselineDriver(name: "G. Morbidelli", raceSkill: 0.50, qualifying: 0.50, aggression: 0.50));
        var generated = Build(Seat(name: "STALE NAME", raceSkill: 0.50, qualifying: 0.50, aggression: 0.50));

        var merged = GridStager.MergeInstalledPrimary(generated, installed, packBaseline);

        var driver = Assert.Single(merged.Drivers);
        Assert.Equal("Gianni Morbidelli", driver.Name);       // installed name, not "STALE NAME"
        Assert.Equal(0.72, driver.RaceSkill!.Value, 3);       // installed rating, not 0.50
        Assert.Equal(0.70, driver.QualifyingSkill!.Value, 3);
    }

    // ---------- helpers ----------

    private static IReadOnlyDictionary<string, CustomAiDriver> SingleBaseline(
        CustomAiDriver baseline, string livery = Livery) =>
        new Dictionary<string, CustomAiDriver>(StringComparer.Ordinal) { [livery] = baseline };

    private static CustomAiDriver BaselineDriver(
        string name, double raceSkill, double qualifying, double aggression, string livery = Livery) => new()
    {
        LiveryName = livery,
        Name = name,
        Country = "ITA",
        RaceSkill = raceSkill,
        QualifyingSkill = qualifying,
        Aggression = aggression,
    };

    private static GridSeat Seat(
        string livery = Livery,
        string name = "G. Morbidelli",
        double raceSkill = 0.50,
        double qualifying = 0.50,
        double aggression = 0.50) => new()
    {
        DriverId = "driver.morbidelli",
        DriverName = name,
        Country = "ITA",
        TeamId = "team.dallara",
        TeamName = "Dallara",
        Number = "21",
        Ams2LiveryName = livery,
        Ratings = new PackDriverRatings
        {
            RaceSkill = raceSkill,
            QualifyingSkill = qualifying,
            Aggression = aggression,
        },
        Reliability = 0.90,
        WeightScalar = 1.0,
        PowerScalar = 1.0,
        DragScalar = 1.0,
    };

    private static CustomAiFile Build(params GridSeat[] seats) => GridStager.Build(new GridPlan
    {
        PackId = "test-pack",
        Year = 1990,
        SeriesName = "Test Series",
        Ams2Class = "F-Classic_Gen3",
        Round = 1,
        RoundName = "Test Grand Prix",
        TrackId = "interlagos_1990",
        Seats = seats,
    }, "names-primary test");

    private static double Stat(XElement driver, string element) =>
        double.Parse(driver.Element(element)!.Value, CultureInfo.InvariantCulture);

    private static DateTimeOffset Timestamp(int minutes) =>
        new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero).AddMinutes(minutes);

    private sealed class TempInstall : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "companion-names-primary", Guid.NewGuid().ToString("N"));

        public TempInstall(string installedClassXml)
        {
            Directory.CreateDirectory(Path);
            File.WriteAllText(System.IO.Path.Combine(Path, "F-Classic_Gen3.xml"), installedClassXml);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }
}
