using Companion.Ams2.CustomAi;
using Companion.Ams2.Grid;
using Companion.Core.Grid;
using Companion.Core.Packs;

namespace Companion.Tests.Grid;

/// <summary>
/// Diff-aware staging (NAMeS-first, locked decision #7b): when the installed class XML —
/// lenient-parsed, community quirks and all — already carries an equivalent base entry for
/// every generated seat, Stage writes NOTHING (no backup, no force needed) and reports
/// NoOpAlreadyMatches. Any genuine divergence (rating beyond 1e-4, missing livery, different
/// name, non-neutral scalar) stages exactly as before: force-gated on foreign files,
/// backup-first always.
/// </summary>
public class GridStagerNoOpTests
{
    // The community-file counterpart of Seat(): same effective values written in the loose
    // jusk dialect — malformed dashed comment, '0.80'-style zero-padding, an unrelated extra
    // livery, and a track-scoped override entry that must NOT break base equivalence.
    private const string EquivalentInstalledXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!--Custom AI by jusk - F1 1967 Season
        --------------------------------------------------------------------------------------
        1 	South African GP	02 Jan			Kyalami Historic 1976
        -->
        <custom_ai_drivers>
        	<driver livery_name="Team A &amp; Co #1">
        		<name>Driver One</name>
        		<country>TST</country>
                <race_skill>0.80</race_skill>
                <qualifying_skill>0.850</qualifying_skill>
                <aggression>0.5</aggression>
                <defending>0.5</defending>
                <stamina>0.8</stamina>
                <consistency>0.8</consistency>
                <start_reactions>0.8</start_reactions>
                <wet_skill>0.8</wet_skill>
                <tyre_management>0.8</tyre_management>
                <avoidance_of_mistakes>0.8</avoidance_of_mistakes>
                <vehicle_reliability>0.90</vehicle_reliability>
        	</driver>
        	<driver livery_name="Team A &amp; Co #1" tracks="Monza_1971">
                <race_skill>0.70</race_skill>
        	</driver>
        	<driver livery_name="Some Other Livery #99">
        		<name>Someone Else</name>
                <race_skill>0.40</race_skill>
        	</driver>
        </custom_ai_drivers>
        """;

    // ---------- no-op: equivalent installed file ----------

    [Fact]
    public void Stage_InstalledFileAlreadyMatches_IsANoOp_NoWriteNoBackupNoForce()
    {
        using var dir = new TempDir();
        string target = Path.Combine(dir.Path, "F-Vintage_Gen1.xml");
        File.WriteAllText(target, EquivalentInstalledXml);
        var before = File.GetLastWriteTimeUtc(target);

        // The installed file is the user's own (no GeneratedMarker) — yet NO force is needed,
        // because nothing is written.
        var result = GridStager.Stage(Build(Seat()), dir.Path, Timestamp(0));

        Assert.True(result.NoOpAlreadyMatches);
        Assert.Null(result.BackupPath);
        Assert.Equal(target, result.WrittenPath);
        Assert.Contains("already matches", result.Report);
        Assert.Contains("nothing written", result.Report);

        Assert.Equal(EquivalentInstalledXml, File.ReadAllText(target)); // byte-identical
        Assert.Equal(before, File.GetLastWriteTimeUtc(target));
        Assert.Empty(new CustomAiBackup(dir.Path).ListBackups("F-Vintage_Gen1"));
    }

    [Fact]
    public void Stage_FloatWithinTolerance_StillCountsAsMatching()
    {
        using var dir = new TempDir();
        string target = Path.Combine(dir.Path, "F-Vintage_Gen1.xml");
        // 0.80005 vs generated 0.8 — inside the 1e-4 tolerance.
        File.WriteAllText(target, EquivalentInstalledXml.Replace(
            "<race_skill>0.80</race_skill>", "<race_skill>0.80005</race_skill>"));

        var result = GridStager.Stage(Build(Seat()), dir.Path, Timestamp(0));

        Assert.True(result.NoOpAlreadyMatches);
    }

    [Fact]
    public void Stage_SameGeneratedFileTwice_SecondStageIsANoOp()
    {
        using var dir = new TempDir();
        var file = Build(Seat());

        var first = GridStager.Stage(file, dir.Path, Timestamp(0));
        var second = GridStager.Stage(file, dir.Path, Timestamp(1));

        Assert.False(first.NoOpAlreadyMatches);
        // The writer's "0.0###" format re-parses within 1e-4 — restaging the identical round
        // never rewrites, so clicking Stage twice takes no pointless second backup.
        Assert.True(second.NoOpAlreadyMatches);
        Assert.Empty(new CustomAiBackup(dir.Path).ListBackups("F-Vintage_Gen1"));
    }

    // ---------- NOT a no-op: genuine divergences ----------

    [Fact]
    public void Stage_RoundOverrideDiverges_WritesAfterForce_BackupFirst()
    {
        using var dir = new TempDir();
        string target = Path.Combine(dir.Path, "F-Vintage_Gen1.xml");
        File.WriteAllText(target, EquivalentInstalledXml);

        // A per-round override moved race_skill 0.8 -> 0.75: the installed file no longer
        // satisfies the round.
        var overridden = Build(Seat() with
        {
            Ratings = Ratings() with { RaceSkill = 0.75 },
        });

        // Divergent + foreign file: the force gate still protects the user's file...
        Assert.Throws<InvalidOperationException>(() =>
            GridStager.Stage(overridden, dir.Path, Timestamp(0)));
        Assert.Equal(EquivalentInstalledXml, File.ReadAllText(target)); // untouched

        // ...and force stages backup-first as before.
        var result = GridStager.Stage(overridden, dir.Path, Timestamp(1), force: true);
        Assert.False(result.NoOpAlreadyMatches);
        Assert.NotNull(result.BackupPath);
        Assert.Equal(EquivalentInstalledXml, File.ReadAllText(result.BackupPath!));
        Assert.Equal(CustomAiXmlWriter.ToXml(overridden), File.ReadAllText(target));
    }

    [Fact]
    public void Stage_FloatBeyondTolerance_IsNotANoOp()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "F-Vintage_Gen1.xml"),
            EquivalentInstalledXml.Replace(
                "<race_skill>0.80</race_skill>", "<race_skill>0.801</race_skill>"));

        var result = GridStager.Stage(Build(Seat()), dir.Path, Timestamp(0), force: true);

        Assert.False(result.NoOpAlreadyMatches);
        Assert.NotNull(result.BackupPath);
    }

    [Fact]
    public void Stage_SeatLiveryMissingFromInstalledFile_IsNotANoOp()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "F-Vintage_Gen1.xml"), EquivalentInstalledXml);

        var twoSeats = Build(
            Seat(),
            Seat() with { Ams2LiveryName = "Team B #2", DriverName = "Driver Two" });

        var result = GridStager.Stage(twoSeats, dir.Path, Timestamp(0), force: true);

        Assert.False(result.NoOpAlreadyMatches);
    }

    [Fact]
    public void Stage_NameDiffersOrdinally_IsNotANoOp()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "F-Vintage_Gen1.xml"),
            EquivalentInstalledXml.Replace("<name>Driver One</name>", "<name>Driver 0ne</name>"));

        var result = GridStager.Stage(Build(Seat()), dir.Path, Timestamp(0), force: true);

        Assert.False(result.NoOpAlreadyMatches);
    }

    [Fact]
    public void Stage_ScalarDiffers_ButOmittedVsNeutralMatches()
    {
        using var dir = new TempDir();
        string target = Path.Combine(dir.Path, "F-Vintage_Gen1.xml");

        // Installed sets weight_scalar 1.01 while the generated seat is neutral (omitted):
        // effective physics differ -> stages.
        File.WriteAllText(target, EquivalentInstalledXml.Replace(
            "<vehicle_reliability>0.90</vehicle_reliability>",
            "<vehicle_reliability>0.90</vehicle_reliability><weight_scalar>1.01</weight_scalar>"));
        var diverged = GridStager.Stage(Build(Seat()), dir.Path, Timestamp(0), force: true);
        Assert.False(diverged.NoOpAlreadyMatches);

        // Installed writes weight_scalar 1.0 EXPLICITLY while the generated file omits it
        // (both mean neutral) -> no-op.
        File.WriteAllText(target, EquivalentInstalledXml.Replace(
            "<vehicle_reliability>0.90</vehicle_reliability>",
            "<vehicle_reliability>0.90</vehicle_reliability><weight_scalar>1.0</weight_scalar>"));
        var neutral = GridStager.Stage(Build(Seat()), dir.Path, Timestamp(1));
        Assert.True(neutral.NoOpAlreadyMatches);
    }

    [Fact]
    public void Stage_InstalledEntryCarriesAFieldTheGridDoesNot_IsNotANoOp()
    {
        using var dir = new TempDir();
        // blue_flag_conceding present in the installed base entry, absent from the generated
        // seat: the effective in-game behavior differs.
        File.WriteAllText(Path.Combine(dir.Path, "F-Vintage_Gen1.xml"),
            EquivalentInstalledXml.Replace(
                "<vehicle_reliability>0.90</vehicle_reliability>",
                "<vehicle_reliability>0.90</vehicle_reliability><blue_flag_conceding>0.88</blue_flag_conceding>"));

        var result = GridStager.Stage(Build(Seat()), dir.Path, Timestamp(0), force: true);

        Assert.False(result.NoOpAlreadyMatches);
    }

    [Fact]
    public void Stage_UnreadableInstalledFile_FallsThroughToTheForceGate()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "F-Vintage_Gen1.xml"),
            "<custom_ai_drivers><driver livery_name=");

        // Not parseable even leniently -> no equivalence claim -> foreign-file force gate.
        Assert.Throws<InvalidOperationException>(() =>
            GridStager.Stage(Build(Seat()), dir.Path, Timestamp(0)));

        var forced = GridStager.Stage(Build(Seat()), dir.Path, Timestamp(1), force: true);
        Assert.False(forced.NoOpAlreadyMatches);
        Assert.NotNull(forced.BackupPath);
    }

    // ---------- equivalence comparer unit facts ----------

    [Fact]
    public void Compare_ReportsPerFieldDifferences()
    {
        var generated = Build(Seat());
        var installed = CommunityAiReader.Parse(EquivalentInstalledXml
            .Replace("<name>Driver One</name>", "<name>Driver Uno</name>")
            .Replace("<qualifying_skill>0.850</qualifying_skill>", "<qualifying_skill>0.9</qualifying_skill>"));

        var result = CustomAiEquivalence.Compare(generated, installed);

        Assert.False(result.Matches);
        Assert.Contains(result.Differences, d => d.Contains("name"));
        Assert.Contains(result.Differences, d => d.Contains("qualifying_skill"));
    }

    [Fact]
    public void Compare_ExtraInstalledLiveriesAndTrackEntries_NeverBreakEquivalence()
    {
        var generated = Build(Seat());
        var installed = CommunityAiReader.Parse(EquivalentInstalledXml);

        Assert.True(CustomAiEquivalence.Compare(generated, installed).Matches);
        Assert.NotEmpty(installed.TrackEntries); // the file HAS per-track overrides
        Assert.True(installed.BaseEntries.Count > generated.Drivers.Count); // and extra liveries
    }

    // ---------- helpers ----------

    private static PackDriverRatings Ratings() => new()
    {
        RaceSkill = 0.8,
        QualifyingSkill = 0.85,
        Aggression = 0.5,
        Defending = 0.5,
        Stamina = 0.8,
        Consistency = 0.8,
        StartReactions = 0.8,
        WetSkill = 0.8,
        TyreManagement = 0.8,
        AvoidanceOfMistakes = 0.8,
    };

    private static GridSeat Seat() => new()
    {
        DriverId = "driver.one",
        DriverName = "Driver One",
        Country = "TST",
        TeamId = "team.a",
        TeamName = "Team A",
        Number = "1",
        Ams2LiveryName = "Team A & Co #1",
        Ratings = Ratings(),
        Reliability = 0.90,
        WeightScalar = 1.0,
        PowerScalar = 1.0,
        DragScalar = 1.0,
    };

    private static CustomAiFile Build(params GridSeat[] seats) => GridStager.Build(new GridPlan
    {
        PackId = "test-pack",
        Year = 1967,
        SeriesName = "Test Series",
        Ams2Class = "F-Vintage_Gen1",
        Round = 1,
        RoundName = "Test Grand Prix",
        TrackId = "kyalami_historic",
        Seats = seats,
    }, "no-op test");

    private static DateTimeOffset Timestamp(int minutes) =>
        new DateTimeOffset(2026, 7, 2, 16, 0, 0, TimeSpan.Zero).AddMinutes(minutes);

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "companion-noop-tests", Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

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
