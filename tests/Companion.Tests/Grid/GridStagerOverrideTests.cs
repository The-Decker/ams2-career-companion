using System.Xml.Linq;
using Companion.Ams2.CustomAi;
using Companion.Ams2.Grid;
using Companion.Core.Grid;
using Companion.Core.Packs;

namespace Companion.Tests.Grid;

/// <summary>
/// The Skins grid editor's cosmetic per-seat overrides (<see cref="GridStager.ApplyStagingOverrides"/>):
/// a custom driver NAME and/or a rebound LIVERY, applied to the STAGED custom-AI file only. Applied
/// AFTER the NAMeS-primary merge, so the player's explicit edit wins over the installed community
/// value; empty overrides leave the file byte-identical. Never touches the resolved grid the sim
/// scores, so it is sim-inert.
/// </summary>
public class GridStagerOverrideTests
{
    private const string Livery = "Skoal Bandit Formula 1 Team #10";

    [Fact]
    public void ApplyStagingOverrides_RenamesAndRebinds_TheMatchingSeat()
    {
        var file = Build(Seat(livery: Livery, name: "K. Acheson"), Seat(livery: "Other #1", name: "Someone"));
        var overrides = Map(Livery, new SeatStagingOverride { DriverName = "Mike K.", LiveryName = "Ferrari #11 C. Amon" });

        var result = GridStager.ApplyStagingOverrides(file, overrides);

        var edited = result.Drivers.Single(d => d.LiveryName == "Ferrari #11 C. Amon");
        Assert.Equal("Mike K.", edited.Name);           // renamed
        Assert.Equal("Ferrari #11 C. Amon", edited.LiveryName); // rebound skin
        // The other seat is untouched.
        Assert.Contains(result.Drivers, d => d is { LiveryName: "Other #1", Name: "Someone" });
    }

    [Fact]
    public void ApplyStagingOverrides_RenameOnly_KeepsLivery()
    {
        var file = Build(Seat(livery: Livery, name: "K. Acheson"));
        var result = GridStager.ApplyStagingOverrides(file, Map(Livery, new SeatStagingOverride { DriverName = "Mike K." }));

        var driver = Assert.Single(result.Drivers);
        Assert.Equal("Mike K.", driver.Name);
        Assert.Equal(Livery, driver.LiveryName); // unchanged
    }

    [Fact]
    public void ApplyStagingOverrides_NullOrEmpty_ReturnsUnchanged()
    {
        var file = Build(Seat(livery: Livery, name: "K. Acheson"));

        Assert.Same(file, GridStager.ApplyStagingOverrides(file, null));
        Assert.Same(file, GridStager.ApplyStagingOverrides(file, Map(Livery, new SeatStagingOverride())));
        Assert.Same(file, GridStager.ApplyStagingOverrides(file, new Dictionary<string, SeatStagingOverride>()));
    }

    [Fact]
    public void Stage_AppliesOverride_WinningOverTheInstalledCommunityName()
    {
        using var dir = new TempInstall(
            "<custom_ai_drivers><driver livery_name=\"" + Livery + "\">" +
            "<name>Kenny Acheson</name><race_skill>0.6</race_skill></driver></custom_ai_drivers>");

        var generated = Build(Seat(livery: Livery, name: "K. Acheson"));
        var overrides = Map(Livery, new SeatStagingOverride { DriverName = "Mike Kobra" });

        // force:true stages over the community file; the merge would keep "Kenny Acheson", but the
        // grid-editor override is applied last, so Mike's name is what lands on disk.
        var result = GridStager.Stage(generated, dir.Path, Timestamp(), force: true, overrides: overrides);

        var written = XDocument.Load(result.WrittenPath).Root!.Elements("driver").Single();
        Assert.Equal("Mike Kobra", written.Element("name")!.Value);
    }

    [Fact]
    public void Stage_NoOverrides_IsByteIdenticalToStagingWithout()
    {
        var generated = Build(Seat(livery: Livery, name: "K. Acheson"));

        using var a = new TempInstall(null);
        using var b = new TempInstall(null);
        string withNull = GridStager.Stage(generated, a.Path, Timestamp()).WrittenPath;
        string withEmpty = GridStager.Stage(generated, b.Path, Timestamp(),
            overrides: new Dictionary<string, SeatStagingOverride>()).WrittenPath;

        Assert.Equal(File.ReadAllText(withNull), File.ReadAllText(withEmpty));
    }

    // ---------- explicit apply: alwaysWrite (the #1 diagnosis fix) ----------

    [Fact]
    public void AlwaysWrite_WritesAnAppMarkedFile_OverACommunityFile_WithoutForce()
    {
        // A foreign community file exists. Normal staging without force REFUSES (0 bytes) — the bug
        // that left Mike's edits unwritten. alwaysWrite writes it anyway (backup-first), marked.
        using var dir = new TempInstall(
            "<custom_ai_drivers><driver livery_name=\"" + Livery + "\">" +
            "<name>Kenny Acheson</name></driver></custom_ai_drivers>");

        var generated = Build(Seat(livery: Livery, name: "K. Acheson"));

        var refused = GridStager.StageOrRefuse(generated, dir.Path, Timestamp());
        Assert.True(refused.RequiresForce); // the old behavior: nothing written

        var applied = GridStager.Stage(generated, dir.Path, Timestamp(), alwaysWrite: true);
        Assert.False(applied.NoOpAlreadyMatches);
        Assert.Null(applied.RequiresForce ? "x" : null);
        Assert.NotNull(applied.BackupPath); // community file snapshotted first
        string onDisk = File.ReadAllText(applied.WrittenPath!);
        Assert.Contains(GridStager.GeneratedMarker, onDisk); // app-marked → verifiable on disk
    }

    [Fact]
    public void AlwaysWrite_WritesEvenWhenContentMatches_KillingTheSilentNoOp()
    {
        // Stage once to an empty dir → app-marked file on disk. Re-staging the SAME grid normally is
        // a diff-aware no-op (0 bytes). alwaysWrite writes again regardless, so an explicit apply is
        // ALWAYS verifiable on disk.
        using var dir = new TempInstall(null);
        var generated = Build(Seat(livery: Livery, name: "K. Acheson"));

        GridStager.Stage(generated, dir.Path, Timestamp()); // first write (app-marked)

        var noOp = GridStager.Stage(generated, dir.Path, Timestamp());
        Assert.True(noOp.NoOpAlreadyMatches); // nothing written

        var applied = GridStager.Stage(generated, dir.Path, Timestamp(), alwaysWrite: true);
        Assert.False(applied.NoOpAlreadyMatches); // written despite matching
        Assert.NotNull(applied.BackupPath);
    }

    // ---------- helpers ----------

    private static IReadOnlyDictionary<string, SeatStagingOverride> Map(string livery, SeatStagingOverride ov) =>
        new Dictionary<string, SeatStagingOverride>(StringComparer.Ordinal) { [livery] = ov };

    private static GridSeat Seat(string livery, string name) => new()
    {
        DriverId = "driver." + name.Replace(" ", "").Replace(".", "").ToLowerInvariant(),
        DriverName = name,
        Country = "GBR",
        TeamId = "team.x",
        TeamName = "Team X",
        Number = "10",
        Ams2LiveryName = livery,
        Ratings = new PackDriverRatings { RaceSkill = 0.6, QualifyingSkill = 0.6 },
        Reliability = 0.9,
        WeightScalar = 1.0,
        PowerScalar = 1.0,
        DragScalar = 1.0,
    };

    private static CustomAiFile Build(params GridSeat[] seats) => GridStager.Build(new GridPlan
    {
        PackId = "test",
        Year = 1985,
        SeriesName = "Test",
        Ams2Class = "F-Retro_Gen3",
        Round = 1,
        RoundName = "Test GP",
        TrackId = "test_track",
        Seats = seats,
    }, "override test");

    private static DateTimeOffset Timestamp() => new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

    private sealed class TempInstall : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "companion-grid-override", Guid.NewGuid().ToString("N"));

        public TempInstall(string? installedClassXml)
        {
            Directory.CreateDirectory(Path);
            if (installedClassXml is not null)
                File.WriteAllText(System.IO.Path.Combine(Path, "F-Retro_Gen3.xml"), installedClassXml);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
