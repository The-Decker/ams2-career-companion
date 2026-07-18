using Companion.Ams2.Skins;

namespace Companion.Tests.Ams2;

/// <summary>
/// The Skin Season Manager: two season packs for the same car model collide ONLY on the model's
/// active override pointer (<c>&lt;model&gt;.xml</c>), textures live in per-pack subfolders and
/// coexist. Swapping seasons = swapping pointer files, backup-first, all-or-nothing per set;
/// an installed file we cannot recognize as pack-managed content is held behind the force gate.
/// </summary>
public sealed class SkinSeasonManagerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-skin-season-").FullName;
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string Xml1983 = """
        <USER_OVERRIDES>
        	<LIVERY_OVERRIDE LIVERY="51" NAME="1983 Brabham #5 N. Piquet" BASELIVERY="Default">
        		<TEXTURE NAME="BODY" PATH="TAMS2SP\body5.dds" />
        	</LIVERY_OVERRIDE>
        </USER_OVERRIDES>
        """;

    private const string Xml1985 = """
        <USER_OVERRIDES>
        	<LIVERY_OVERRIDE LIVERY="51" NAME="Tyrrell Racing Organisation #3" BASELIVERY="Default">
        		<TEXTURE NAME="BODY" PATH="F1_1985\body3.dds" />
        	</LIVERY_OVERRIDE>
        </USER_OVERRIDES>
        """;

    private SkinSeasonLibrary Library() => new()
    {
        Sets = new Dictionary<string, SkinSeasonSet>(StringComparer.OrdinalIgnoreCase)
        {
            ["f1-1983"] = new SkinSeasonSet
            {
                Key = "f1-1983",
                ModelXml = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    { ["formula_retro_g3"] = Xml1983 },
            },
            ["f1-1985"] = new SkinSeasonSet
            {
                Key = "f1-1985",
                ModelXml = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    { ["formula_retro_g3"] = Xml1985 },
            },
        },
    };

    /// <summary>Creates the model folder with texture subfolders for BOTH seasons (the coexisting
    /// install) and an active pointer with the given content.</summary>
    private string InstallModel(string? activeContent, params string[] textureFolders)
    {
        string folder = Path.Combine(_root, "formula_retro_g3");
        Directory.CreateDirectory(folder);
        foreach (var t in textureFolders)
            Directory.CreateDirectory(Path.Combine(folder, t));
        if (activeContent is not null)
            File.WriteAllText(Path.Combine(folder, "formula_retro_g3.xml"), activeContent);
        return folder;
    }

    [Fact]
    public void Inspect_RecognizesActive_OtherSeason_AndUnrecognized()
    {
        var lib = Library();
        var set85 = lib.Sets["f1-1985"];

        InstallModel(Xml1983, "TAMS2SP", "F1_1985");
        var onOther = SkinSeasonManager.Inspect(set85, lib, _root);
        Assert.Equal(SkinSeasonModelState.OtherSeason, onOther.Models.Single().State);
        Assert.Contains("f1-1983", onOther.Models.Single().Detail);
        Assert.True(onOther.CanActivate);
        Assert.False(onOther.IsFullyActive);

        // CRLF vs LF must not defeat recognition (archive vs install line endings).
        InstallModel(Xml1985.Replace("\n", "\r\n"), "TAMS2SP", "F1_1985");
        Assert.True(SkinSeasonManager.Inspect(set85, lib, _root).IsFullyActive);

        InstallModel("<USER_OVERRIDES><!-- hand-edited by the user --></USER_OVERRIDES>", "TAMS2SP", "F1_1985");
        var unrecognized = SkinSeasonManager.Inspect(set85, lib, _root);
        Assert.Equal(SkinSeasonModelState.Unrecognized, unrecognized.Models.Single().State);
        Assert.False(unrecognized.CanActivate);
        Assert.True(unrecognized.RequiresForce);
    }

    [Fact]
    public void Inspect_ReportsMissingFolder_AndMissingTextures()
    {
        var lib = Library();
        var set85 = lib.Sets["f1-1985"];

        // No model folder at all → the skins are not installed.
        var missing = SkinSeasonManager.Inspect(set85, lib, _root);
        Assert.Equal(SkinSeasonModelState.FolderMissing, missing.Models.Single().State);
        Assert.False(missing.RequiresForce);

        // Folder there, but the season's referenced texture subfolder is not.
        InstallModel(Xml1983, "TAMS2SP");
        var noTextures = SkinSeasonManager.Inspect(set85, lib, _root);
        Assert.Equal(SkinSeasonModelState.TexturesMissing, noTextures.Models.Single().State);
        Assert.Contains("F1_1985", noTextures.Models.Single().Detail);
    }

    [Fact]
    public void Inspect_RecognizesAnActiveSiblingVariant()
    {
        var lib = Library();
        string folder = InstallModel(null, "TAMS2SP", "F1_1985");
        const string variant = "<USER_OVERRIDES><LIVERY_OVERRIDE LIVERY=\"51\" NAME=\"Imola alt\" /></USER_OVERRIDES>";
        File.WriteAllText(Path.Combine(folder, "formula_retro_g3_03Imola.xml"), variant);
        File.WriteAllText(Path.Combine(folder, "formula_retro_g3.xml"), variant);

        var status = SkinSeasonManager.Inspect(lib.Sets["f1-1985"], lib, _root);
        Assert.Equal(SkinSeasonModelState.Variant, status.Models.Single().State);
        Assert.True(status.CanActivate);
    }

    [Fact]
    public void Activate_SwapsSeasons_BackupFirst()
    {
        var lib = Library();
        string folder = InstallModel(Xml1983, "TAMS2SP", "F1_1985");

        var result = SkinSeasonManager.Activate(lib.Sets["f1-1985"], lib, _root, force: false, Now);

        Assert.True(result.Success);
        Assert.Equal(1, result.Applied);
        string backup = Assert.Single(result.Backups);
        Assert.True(File.Exists(backup));
        Assert.Contains("_companion-backups", backup);
        // The backup holds the 1983 pointer; the active file is now the 1985 pointer.
        Assert.Contains("TAMS2SP", File.ReadAllText(backup));
        Assert.Contains("F1_1985", File.ReadAllText(Path.Combine(folder, "formula_retro_g3.xml")));
        // And the manager now reports 1985 active / 1983 on the other season.
        Assert.True(SkinSeasonManager.Inspect(lib.Sets["f1-1985"], lib, _root).IsFullyActive);
        Assert.Equal(SkinSeasonModelState.OtherSeason,
            SkinSeasonManager.Inspect(lib.Sets["f1-1983"], lib, _root).Models.Single().State);
    }

    [Fact]
    public void Activate_IsANoOp_WhenAlreadyActive()
    {
        var lib = Library();
        InstallModel(Xml1985, "TAMS2SP", "F1_1985");

        var result = SkinSeasonManager.Activate(lib.Sets["f1-1985"], lib, _root, force: false, Now);

        Assert.True(result.Success);
        Assert.Equal(0, result.Applied);
        Assert.Empty(result.Backups);
    }

    [Fact]
    public void Activate_RefusesAnUnrecognizedFile_WithoutForce_ThenForceOverwritesBackupFirst()
    {
        var lib = Library();
        string folder = InstallModel("<USER_OVERRIDES><!-- hand-edited --></USER_OVERRIDES>", "TAMS2SP", "F1_1985");

        var refused = SkinSeasonManager.Activate(lib.Sets["f1-1985"], lib, _root, force: false, Now);
        Assert.False(refused.Success);
        Assert.True(refused.RequiresForce);
        Assert.Equal(0, refused.Applied);
        Assert.Contains("hand-edited",
            File.ReadAllText(Path.Combine(folder, "formula_retro_g3.xml"))); // untouched

        var forced = SkinSeasonManager.Activate(lib.Sets["f1-1985"], lib, _root, force: true, Now);
        Assert.True(forced.Success);
        Assert.Equal(1, forced.Applied);
        Assert.Contains("hand-edited", File.ReadAllText(Assert.Single(forced.Backups)));
    }

    [Fact]
    public void Activate_IsAllOrNothing_WhenAnyModelsSkinsAreMissing()
    {
        // A two-model set where only one model's folder exists: a partial swap would mix two
        // seasons' cars on the grid, so NOTHING may be written.
        var lib = new SkinSeasonLibrary
        {
            Sets = new Dictionary<string, SkinSeasonSet>(StringComparer.OrdinalIgnoreCase)
            {
                ["f1-1985"] = new SkinSeasonSet
                {
                    Key = "f1-1985",
                    ModelXml = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["formula_retro_g3"] = Xml1985,
                        ["formula_retro_g3_te"] = Xml1985,
                    },
                },
            },
        };
        InstallModel(Xml1983, "TAMS2SP", "F1_1985"); // only formula_retro_g3 exists

        var result = SkinSeasonManager.Activate(lib.Sets["f1-1985"], lib, _root, force: false, Now);

        Assert.False(result.Success);
        Assert.Equal(0, result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("formula_retro_g3_te"));
        // The existing model's pointer was NOT half-swapped.
        Assert.Contains("TAMS2SP",
            File.ReadAllText(Path.Combine(_root, "formula_retro_g3", "formula_retro_g3.xml")));
    }

    [Fact]
    public void Library_LoadsSets_SkipsDistTemplates_MissingDirectoryIsEmpty()
    {
        string dir = Path.Combine(_root, "skin-seasons");
        Directory.CreateDirectory(Path.Combine(dir, "f1-1985"));
        File.WriteAllText(Path.Combine(dir, "f1-1985", "formula_retro_g3.xml"), Xml1985);
        File.WriteAllText(Path.Combine(dir, "f1-1985", "formula_retro_g3_dist.xml"), "<template />");

        var library = SkinSeasonLibrary.Load(dir);

        var set = Assert.Single(library.Sets).Value;
        Assert.Equal("f1-1985", set.Key);
        Assert.Single(set.ModelXml); // the _dist template is not a model pointer
        Assert.True(set.ModelXml.ContainsKey("formula_retro_g3"));
        Assert.Same(SkinSeasonLibrary.Empty.Sets,
            SkinSeasonLibrary.Load(Path.Combine(_root, "nope")).Sets);
        Assert.Single(library.SetsForModel("formula_retro_g3"));
        Assert.Empty(library.SetsForModel("mclaren_mp4_1c"));
    }
}
