using Companion.Core.Packs;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Season-end restore (locked decision #7c) through the real CareerSessionService against a
/// TEMP fake install: RestoreOriginalAiFile re-backs-up the CURRENT file first (restore never
/// destroys state), then puts back the pre-season original, preferring the newest backup the
/// app did NOT generate (the user's own file) over later snapshots of app-generated rounds.
/// Plus the end-to-end NAMeS-first happy path: import the installed file at creation, then
/// staging round 1 is a no-op because the installed file already matches.
/// </summary>
public sealed class AiFileRestoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-restore-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // SQLite WAL sidecars can outlive the connection briefly on Windows; leaking a
            // temp folder is better than failing the suite.
        }
    }

    private string PackDirectory => Path.Combine(_root, "packs", "pack");

    private string InstallDirectory => Path.Combine(_root, "install");

    private string CustomAiDirectory => Path.Combine(InstallDirectory, "UserData", "CustomAIDrivers");

    private string LiveAiPath => Path.Combine(CustomAiDirectory, TestPackBuilder.VintageClass + ".xml");

    private const string UserFileXml = """
        <custom_ai_drivers>
        <!--my curated NAMeS file
        ----------------------------------------------------->
        	<driver livery_name="Stock Livery #1"><name>The User's Guy</name><race_skill>0.5</race_skill></driver>
        </custom_ai_drivers>
        """;

    /// <summary>Advances one minute per read so second-resolution backup names never collide.</summary>
    private sealed class SteppingClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            var current = _now;
            _now = _now.AddMinutes(1);
            return current;
        }
    }

    /// <summary>Never advances, every operation lands in the SAME wall-clock second, the
    /// worst case for second-resolution backup names (a real-clock stage + restore can and
    /// does hit this: proven by the live integration run of 2026-07-02).</summary>
    private sealed class FrozenClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
    }

    private CareerEnvironment Environment(bool withInstall = true) => new()
    {
        ContentLibrary = TestPackBuilder.Library(),
        LocateInstall = () => withInstall
            ? new Companion.Ams2.Ams2Installation { InstallDirectory = InstallDirectory }
            : null,
        DocumentsDirectory = Path.Combine(_root, "docs"),
        Clock = new SteppingClock(),
        RulesDirectory = ViewModelTestData.RulesDirectory, // Apply folds → needs the rules data
    };

    /// <summary>Two-round pack whose round 2 carries an aiOverride, so the round-2 grid
    /// genuinely diverges from the staged round-1 file.</summary>
    private static SeasonPack PackWithRound2Override()
    {
        var pack = TestPackBuilder.TwoRoundPack();
        var rounds = pack.Season.Rounds.ToList();
        rounds[1] = rounds[1] with
        {
            AiOverrides = new Dictionary<string, PackRatingsPatch>
            {
                ["driver.brabham"] = new() { RaceSkill = 0.95 },
            },
        };
        return pack with { Season = pack.Season with { Rounds = rounds } };
    }

    private CareerSessionService CreateCareer(CareerEnvironment environment, string? baselineXml = null)
    {
        return CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = PackDirectory,
            CareerFilePath = Path.Combine(_root, "careers", Guid.NewGuid().ToString("N") + ".ams2career"),
            CareerName = "Restore Career",
            MasterSeed = 42,
            PlayerLiveryName = TestPackBuilder.StockLivery2,
            CommunityBaselineXml = baselineXml,
        }, environment);
    }

    private static readonly ResultDraft Round1Draft = new()
    {
        Classified = ["driver.brabham", "driver.hulme"],
        DidNotFinish = new Dictionary<string, string>(),
        Disqualified = [],
    };

    // ---------- restore round-trip ----------

    [Fact]
    public void RestoreOriginalAiFile_ReBacksUpCurrentFirst_ThenRestoresTheUsersOriginal()
    {
        TestPackBuilder.Write(PackWithRound2Override(), PackDirectory);
        Directory.CreateDirectory(CustomAiDirectory);
        File.WriteAllText(LiveAiPath, UserFileXml);

        using var session = CreateCareer(Environment());

        // Round 1: the user's file diverges (their ratings, not the pack's), force-stage.
        var round1 = ((IForceStaging)session).StageCurrentGrid(force: true);
        Assert.True(round1.Success);
        Assert.False(round1.NoOpAlreadyMatches);
        Assert.NotNull(round1.BackupPath);
        Assert.Equal(UserFileXml, File.ReadAllText(round1.BackupPath!));
        string generatedRound1 = File.ReadAllText(LiveAiPath);

        // Round 2 diverges via aiOverride: a second, APP-GENERATED backup piles up.
        session.Apply(Round1Draft);
        var round2 = session.StageCurrentGrid();
        Assert.True(round2.Success);
        Assert.False(round2.NoOpAlreadyMatches);
        Assert.Equal(generatedRound1, File.ReadAllText(round2.BackupPath!));
        string generatedRound2 = File.ReadAllText(LiveAiPath);

        // Season-end restore.
        var outcome = ((IAiFileRestore)session).RestoreOriginalAiFile();

        Assert.True(outcome.Success);
        // The USER'S file is back, not the newer app-generated round-1 snapshot.
        Assert.Equal(UserFileXml, File.ReadAllText(LiveAiPath));
        Assert.Equal(round1.BackupPath, outcome.RestoredFromBackupPath);
        // The pre-restore live file (generated round 2) was re-backed-up FIRST.
        Assert.NotNull(outcome.CurrentFileBackupPath);
        Assert.Equal(generatedRound2, File.ReadAllText(outcome.CurrentFileBackupPath!));
        Assert.Contains(outcome.Messages, m => m.Contains("re-backed up"));
    }

    [Fact]
    public void RestoreImmediatelyAfterStaging_SameWallClockSecond_StillRoundTrips()
    {
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), PackDirectory);
        Directory.CreateDirectory(CustomAiDirectory);
        File.WriteAllText(LiveAiPath, UserFileXml);

        var environment = new CareerEnvironment
        {
            ContentLibrary = TestPackBuilder.Library(),
            LocateInstall = () => new Companion.Ams2.Ams2Installation { InstallDirectory = InstallDirectory },
            DocumentsDirectory = Path.Combine(_root, "docs"),
            Clock = new FrozenClock(),
            RulesDirectory = ViewModelTestData.RulesDirectory,
        };
        using var session = CreateCareer(environment);

        // Force-stage, then restore in the SAME second: the re-backup of the generated file
        // must uniquify against the just-taken user-file backup instead of colliding.
        var staged = ((IForceStaging)session).StageCurrentGrid(force: true);
        Assert.True(staged.Success);
        string generated = File.ReadAllText(LiveAiPath);

        var outcome = ((IAiFileRestore)session).RestoreOriginalAiFile();

        Assert.True(outcome.Success);
        Assert.Equal(UserFileXml, File.ReadAllText(LiveAiPath)); // the round-trip
        Assert.Equal(staged.BackupPath, outcome.RestoredFromBackupPath);
        Assert.NotNull(outcome.CurrentFileBackupPath);
        Assert.NotEqual(staged.BackupPath, outcome.CurrentFileBackupPath);
        Assert.Equal(generated, File.ReadAllText(outcome.CurrentFileBackupPath!));
    }

    [Fact]
    public void RestoreOriginalAiFile_WithNoBackups_FailsWithoutTouchingAnything()
    {
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), PackDirectory);
        Directory.CreateDirectory(CustomAiDirectory);
        File.WriteAllText(LiveAiPath, UserFileXml);

        using var session = CreateCareer(Environment());
        var outcome = ((IAiFileRestore)session).RestoreOriginalAiFile();

        Assert.False(outcome.Success);
        Assert.Contains(outcome.Messages, m => m.Contains("No backup"));
        Assert.Equal(UserFileXml, File.ReadAllText(LiveAiPath)); // untouched
    }

    [Fact]
    public void RestoreOriginalAiFile_WithNoInstall_FailsGracefully()
    {
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), PackDirectory);

        using var session = CreateCareer(Environment(withInstall: false));
        var outcome = ((IAiFileRestore)session).RestoreOriginalAiFile();

        Assert.False(outcome.Success);
        Assert.Contains(outcome.Messages, m => m.Contains("No AMS2 installation"));
    }

    // ---------- the NAMeS-first happy path, end to end ----------

    /// <summary>Installed file fully covering both entries with exactly the fields the
    /// generated grid writes (10 authored ratings + vehicle_reliability equal to the team's
    /// 0.93). After baseline import these ARE the pack values, so round-1 staging finds the
    /// installed file already equivalent and writes nothing.</summary>
    private const string FullCoverageXml = """
        <custom_ai_drivers>
        	<driver livery_name="Stock Livery #1">
        		<name>Jack B. Community</name>
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
                <avoidance_of_mistakes>0.71</avoidance_of_mistakes>
                <vehicle_reliability>0.93</vehicle_reliability>
        	</driver>
        	<driver livery_name="Stock Livery #2">
        		<name>Denny H. Community</name>
        		<country>NZL</country>
                <race_skill>0.93</race_skill>
                <qualifying_skill>0.93</qualifying_skill>
                <aggression>0.59</aggression>
                <defending>0.40</defending>
                <stamina>0.88</stamina>
                <consistency>0.90</consistency>
                <start_reactions>0.91</start_reactions>
                <wet_skill>0.81</wet_skill>
                <tyre_management>0.88</tyre_management>
                <avoidance_of_mistakes>0.75</avoidance_of_mistakes>
                <vehicle_reliability>0.93</vehicle_reliability>
        	</driver>
        </custom_ai_drivers>
        """;

    [Fact]
    public void ImportedBaseline_MakesRoundOneStagingANoOp_UsingTheInstalledFile()
    {
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), PackDirectory);
        Directory.CreateDirectory(CustomAiDirectory);
        File.WriteAllText(LiveAiPath, FullCoverageXml);

        using var session = CreateCareer(Environment(), baselineXml: FullCoverageXml);
        Assert.Equal(CareerSessionService.BaselineSourceInstalledAiFile, session.BaselineSource);

        var outcome = session.StageCurrentGrid();

        Assert.True(outcome.Success);
        Assert.True(outcome.NoOpAlreadyMatches);
        Assert.Null(outcome.BackupPath);
        Assert.Equal(LiveAiPath, outcome.WrittenPath);
        // The user's file is byte-identical, the app used it instead of overwriting it.
        Assert.Equal(FullCoverageXml, File.ReadAllText(LiveAiPath));
        Assert.Contains(outcome.Messages, m => m.Contains("already matches"));
    }
}
