using System.IO.Compression;
using System.Text;
using Companion.Ams2.Skins;

namespace Companion.Tests.Ams2;

/// <summary>
/// The mod-ownership service (Mike's "make the app OWN it" after the 2026-07-11 RCM strip):
/// strip detection (<see cref="ModOwnership.Inspect"/>), the app vault adopt
/// (<see cref="ModOwnership.Capture"/>), and the one-click re-lay (<see cref="ModOwnership.Repair"/>)
/// with vault-first sourcing, archive re-seed fallback, and backup-first pointer/file replacement.
/// All synthetic: a fake Overrides root, a fake vault, tiny DDS stand-ins. Pure file ops, no sim.
/// </summary>
public sealed class ModOwnershipTests : IDisposable
{
    private const string Model = "formula_classic_g3m1";
    private const string Folder = "SMGP";
    private const string PointerXml = "<LIVERY_OVERRIDES><!-- the smgp pointer --></LIVERY_OVERRIDES>";

    private readonly string _root = Directory.CreateTempSubdirectory("companion-modown-").FullName;

    private string OverridesRoot => Path.Combine(_root, "install", "Overrides");
    private string VaultRoot => Path.Combine(_root, "vault");
    private string ModelFolder => Path.Combine(OverridesRoot, Model);
    private string PayloadFolder => Path.Combine(ModelFolder, Folder);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ---------- Inspect ----------

    [Fact]
    public void Inspect_AHealthyInstall_IsOwned()
    {
        PlantHealthyInstall();

        var status = ModOwnership.Inspect(Set(), [OverridesRoot])!;

        Assert.True(status.IsHealthy);
        Assert.False(status.NeedsRepair);
        var model = Assert.Single(status.Models);
        Assert.Equal(SkinModelOwnershipState.Owned, model.State);
        Assert.Empty(model.MissingFolders);
    }

    [Fact]
    public void Inspect_TheRcmStripSignature_IsPayloadMissing()
    {
        PlantHealthyInstall();
        Directory.Delete(PayloadFolder, recursive: true);   // RCM wipes the textures...
        File.Delete(Path.Combine(ModelFolder, Model + ".xml")); // ...and the active pointer

        var status = ModOwnership.Inspect(Set(), [OverridesRoot])!;

        Assert.True(status.NeedsRepair);
        var model = Assert.Single(status.Models);
        Assert.Equal(SkinModelOwnershipState.PayloadMissing, model.State);
        Assert.Equal([Folder], model.MissingFolders);
        Assert.Contains("1 of 1", status.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_AFolderWhollyGone_IsFolderMissing()
    {
        var status = ModOwnership.Inspect(Set(), [OverridesRoot])!;

        var model = Assert.Single(status.Models);
        Assert.Equal(SkinModelOwnershipState.FolderMissing, model.State);
        Assert.Equal([Folder], model.MissingFolders);
    }

    [Fact]
    public void Inspect_ASetWithoutOwnership_IsNull()
    {
        var set = Set() with { Ownership = null };

        Assert.Null(ModOwnership.Inspect(set, [OverridesRoot]));
    }

    [Fact]
    public void Inspect_FindsTheModelAtTheSecondOverrideRoot()
    {
        string secondRoot = Path.Combine(_root, "documents", "Overrides");
        PlantPayload(Path.Combine(secondRoot, Model, Folder), ("a.dds", [1, 2, 3]));

        var status = ModOwnership.Inspect(Set(), [OverridesRoot, secondRoot])!;

        Assert.True(status.IsHealthy);
    }

    // ---------- Capture ----------

    [Fact]
    public void Capture_AdoptsTheHealthyPayloadIntoTheVault()
    {
        PlantHealthyInstall();

        var result = ModOwnership.Capture(Set(), [OverridesRoot], VaultRoot);

        Assert.True(result.Success);
        var captured = Assert.Single(result.Captured);
        Assert.Contains("2 file(s)", captured, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(VaultRoot, Model, Folder, "body.dds")));
        Assert.True(File.Exists(Path.Combine(VaultRoot, Model, Folder, "helmets", "h.dds")));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(PayloadFolder, "body.dds")),
            File.ReadAllBytes(Path.Combine(VaultRoot, Model, Folder, "body.dds")));
    }

    [Fact]
    public void Capture_SkipsMissingPayload_NeverCapturesAnEmptyStateAsGood()
    {
        var result = ModOwnership.Capture(Set(), [OverridesRoot], VaultRoot);

        Assert.False(result.Success);
        Assert.Contains("nothing to capture", result.Errors[0], StringComparison.Ordinal);
        Assert.False(Directory.Exists(VaultRoot));
    }

    // ---------- Repair ----------

    [Fact]
    public void Repair_AfterTheStrip_ReLaysPayloadAndPointer_FromTheVault()
    {
        PlantHealthyInstall();
        ModOwnership.Capture(Set(), [OverridesRoot], VaultRoot);
        Directory.Delete(PayloadFolder, recursive: true);   // the strip
        File.Delete(Path.Combine(ModelFolder, Model + ".xml"));

        var result = ModOwnership.Repair(Set(), [OverridesRoot], VaultRoot, DateTimeOffset.UnixEpoch);

        Assert.True(result.Success);
        Assert.Single(result.Repaired);
        Assert.Empty(result.Errors);
        Assert.True(File.Exists(Path.Combine(PayloadFolder, "body.dds")));
        Assert.True(File.Exists(Path.Combine(PayloadFolder, "helmets", "h.dds")));
        Assert.Equal(PointerXml, File.ReadAllText(Path.Combine(ModelFolder, Model + ".xml")));
        Assert.Empty(result.Backups); // nothing pre-existing survived the strip, nothing to back up
        Assert.True(ModOwnership.Inspect(Set(), [OverridesRoot])!.IsHealthy);
    }

    [Fact]
    public void Repair_ReplacesADifferingFile_BackupFirst()
    {
        PlantHealthyInstall();
        ModOwnership.Capture(Set(), [OverridesRoot], VaultRoot);
        File.WriteAllBytes(Path.Combine(PayloadFolder, "body.dds"), [9, 9, 9]); // corrupted after capture

        var result = ModOwnership.Repair(Set(), [OverridesRoot], VaultRoot, DateTimeOffset.UnixEpoch);

        Assert.True(result.Success);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(PayloadFolder, "body.dds")));
        var backup = Assert.Single(result.Backups);
        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(backup)); // the corrupt file was preserved
    }

    [Fact]
    public void Repair_LeavesAnIntactInstallUntouched()
    {
        PlantHealthyInstall();

        var result = ModOwnership.Repair(Set(), [OverridesRoot], VaultRoot, DateTimeOffset.UnixEpoch);

        Assert.True(result.Success);
        Assert.Empty(result.Repaired);
        Assert.Single(result.Skipped);
        Assert.Equal("The smgp mod payload is already intact.", result.Message);
    }

    [Fact]
    public void Repair_ReSeedsTheVaultFromAZipArchive_WhenTheVaultIsEmpty()
    {
        PlantHealthyInstall();
        Directory.Delete(PayloadFolder, recursive: true);
        string zipPath = BuildSourceZip();
        var set = Set() with
        {
            Ownership = Set().Ownership! with
            {
                Payload = new Dictionary<string, SkinModelOwnership>(StringComparer.OrdinalIgnoreCase)
                {
                    [Model] = new SkinModelOwnership { Folders = [Folder], ArchivePath = zipPath },
                },
            },
        };

        var result = ModOwnership.Repair(set, [OverridesRoot], VaultRoot, DateTimeOffset.UnixEpoch);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.True(File.Exists(Path.Combine(PayloadFolder, "car1_body.dds")));
        // The extraction re-seeded the vault for next time.
        Assert.True(File.Exists(Path.Combine(VaultRoot, Model, Folder, "car1_body.dds")));
    }

    [Fact]
    public void Repair_WithoutVaultOrArchive_ReportsSourceUnavailable()
    {
        PlantHealthyInstall();
        Directory.Delete(PayloadFolder, recursive: true);

        var set = Set() with
        {
            Ownership = Set().Ownership! with
            {
                Payload = new Dictionary<string, SkinModelOwnership>(StringComparer.OrdinalIgnoreCase)
                {
                    [Model] = new SkinModelOwnership { Folders = [Folder] }, // no archive recorded
                },
            },
        };

        var result = ModOwnership.Repair(set, [OverridesRoot], VaultRoot, DateTimeOffset.UnixEpoch);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("no source archive", StringComparison.Ordinal));
    }

    [Fact]
    public void OwnershipManifest_LoadsAndTolerates()
    {
        string setDir = Path.Combine(_root, "library", "smgp");
        Directory.CreateDirectory(setDir);
        Assert.Null(SkinSeasonOwnership.Load(setDir)); // absent = inspect-only, never an error

        File.WriteAllText(Path.Combine(setDir, "ownership.json"), """
            { "schemaVersion": 1,
              "payload": {
                "formula_classic_g3m1": { "folders": ["SMGP"] },
                "mclaren_mp45b": { "folders": ["skins"], "archive": { "path": "Z:/pack.zip" } }
              } }
            """);
        var ownership = SkinSeasonOwnership.Load(setDir)!;

        Assert.Equal(2, ownership.Payload.Count);
        Assert.Equal(["SMGP"], ownership.Payload[Model].Folders);
        Assert.Equal("Z:/pack.zip", ownership.Payload["mclaren_mp45b"].ArchivePath);

        File.WriteAllText(Path.Combine(setDir, "ownership.json"), "{ not json");
        Assert.Null(SkinSeasonOwnership.Load(setDir)); // corrupt = no ownership, not a crash
    }

    // ---------- scaffolding ----------

    private SkinSeasonSet Set() => new()
    {
        Key = "smgp",
        ModelXml = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Model] = PointerXml,
        },
        Ownership = new SkinSeasonOwnership
        {
            Payload = new Dictionary<string, SkinModelOwnership>(StringComparer.OrdinalIgnoreCase)
            {
                [Model] = new SkinModelOwnership { Folders = [Folder], ArchivePath = null },
            },
        },
    };

    private void PlantHealthyInstall()
    {
        PlantPayload(PayloadFolder, ("body.dds", [1, 2, 3]));
        PlantPayload(Path.Combine(PayloadFolder, "helmets"), ("h.dds", [4, 5, 6]));
        File.WriteAllText(Path.Combine(ModelFolder, Model + ".xml"), PointerXml, new UTF8Encoding(false));
    }

    private static void PlantPayload(string directory, params (string Name, byte[] Bytes)[] files)
    {
        Directory.CreateDirectory(directory);
        foreach (var (name, bytes) in files)
            File.WriteAllBytes(Path.Combine(directory, name), bytes);
    }

    /// <summary>A tiny .zip mirroring the install tree (the McLaren pack's shape), holding one
    /// payload file under <c>Overrides/&lt;model&gt;/&lt;folder&gt;/</c>.</summary>
    private string BuildSourceZip()
    {
        string zipPath = Path.Combine(_root, "source.zip");
        using (var stream = File.Create(zipPath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry(
                $"Pack/Automobilista 2/Vehicles/Textures/CustomLiveries/Overrides/{Model}/{Folder}/car1_body.dds");
            using var writer = new BinaryWriter(entry.Open());
            writer.Write([7, 7, 7]);
        }

        return zipPath;
    }
}
