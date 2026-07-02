using Companion.Ams2.ContentLibrary;
using Companion.Ams2.Packs;
using Companion.Ams2.Preflight;
using Companion.Core.Packs;

namespace Companion.Tests.Packs;

/// <summary>
/// Living validation of the shipped reference packs (packs/f1-1967, packs/f1-1988): every pack
/// in the repo must load through <see cref="PackLoader"/>, pass
/// <see cref="PackStructuralValidator"/> with no errors, and pass
/// <see cref="PackContentValidator"/> against the REAL extracted content library
/// (data/ams2/*.json, linked into the test output) with an empty installed-livery set.
/// Stock-library livery misses are warnings by design — the skin packs the manifests require
/// are not assumed installed — so only errors fail the suite.
/// </summary>
public class ReferencePackTests
{
    private static string PacksDirectory =>
        Path.Combine(AppContext.BaseDirectory, "packs");

    private static string Ams2DataDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "ams2");

    private static readonly Lazy<Ams2ContentLibrary> Library = new(() =>
    {
        if (!File.Exists(Path.Combine(Ams2DataDirectory, "classes.json")))
            throw new FileNotFoundException(
                $"Content library not found under '{Ams2DataDirectory}'. The test project links " +
                "data/ams2/*.json into the output — rebuild tests/Companion.Tests.");
        return Ams2ContentLibrary.Load(Ams2DataDirectory);
    });

    public static TheoryData<string> ReferencePackIds() => new() { "f1-1967", "f1-1988" };

    // ---------- loading ----------

    private static SeasonPack LoadPack(string packId)
    {
        string dir = Path.Combine(PacksDirectory, packId);
        Assert.True(Directory.Exists(dir),
            $"Reference pack folder '{dir}' was not copied to the test output — " +
            "check the packs None-Include in Companion.Tests.csproj and rebuild.");

        return PackLoader.Parse(
            Read(dir, "pack.json"),
            Read(dir, "season.json"),
            Read(dir, "teams.json"),
            Read(dir, "drivers.json"),
            Read(dir, "entries.json"));
    }

    private static string Read(string dir, string filePart)
    {
        string path = Path.Combine(dir, filePart);
        Assert.True(File.Exists(path), $"Pack file '{path}' is missing — a season pack is five files.");
        return File.ReadAllText(path);
    }

    // ---------- the repo must actually contain both reference packs ----------

    [Fact]
    public void PacksFolder_ContainsBothReferencePacks()
    {
        Assert.True(Directory.Exists(PacksDirectory),
            $"'{PacksDirectory}' missing — the packs None-Include did not copy anything.");

        var found = Directory.GetDirectories(PacksDirectory).Select(Path.GetFileName).ToHashSet();
        Assert.Contains("f1-1967", found);
        Assert.Contains("f1-1988", found);
    }

    // ---------- identity: folder name is the packId ----------

    [Theory]
    [MemberData(nameof(ReferencePackIds))]
    public void ReferencePack_ManifestIdentityMatchesFolder(string packId)
    {
        var manifest = LoadPack(packId).Manifest;

        Assert.Equal(packId, manifest.PackId);
        Assert.Equal(1, manifest.FormatVersion);
    }

    // ---------- structural validation (no content library needed) ----------

    [Theory]
    [MemberData(nameof(ReferencePackIds))]
    public void ReferencePack_PassesStructuralValidationWithNoErrors(string packId)
    {
        var report = PackStructuralValidator.Validate(LoadPack(packId));

        Assert.False(report.HasErrors,
            $"{packId} failed structural validation:\n{FormatStructural(report)}");
    }

    /// <summary>Reference packs are the format's exemplars: they must not even warn.</summary>
    [Theory]
    [MemberData(nameof(ReferencePackIds))]
    public void ReferencePack_StructuralValidationIsWarningFreeToo(string packId)
    {
        var report = PackStructuralValidator.Validate(LoadPack(packId));

        Assert.True(report.Issues.Count == 0,
            $"{packId} structural validation is not clean:\n{FormatStructural(report)}");
    }

    // ---------- content validation against the real library, nothing installed ----------

    [Theory]
    [MemberData(nameof(ReferencePackIds))]
    public void ReferencePack_PassesContentValidationAgainstRealLibrary(string packId)
    {
        var pack = LoadPack(packId);

        var report = PackContentValidator.Validate(pack, Library.Value, installedLiveries: []);

        // With no skin packs installed, livery misses against the stock library are warnings
        // by design. Anything the validator calls an ERROR (unknown class/track/vehicle,
        // AI-cap overflow) is a data bug in the pack.
        var errors = report.Issues.Where(i => i.Severity == PreflightSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            $"{packId} failed content validation against the real library:\n{FormatContent(report)}");
    }

    [Theory]
    [MemberData(nameof(ReferencePackIds))]
    public void ReferencePack_NoContentErrorMentionsTheClassOrAnyTrackId(string packId)
    {
        var pack = LoadPack(packId);

        var report = PackContentValidator.Validate(pack, Library.Value, installedLiveries: []);
        var errorMessages = report.Issues
            .Where(i => i.Severity == PreflightSeverity.Error)
            .Select(i => i.Message)
            .ToList();

        // The contract's item-2/item-3 failure modes, asserted by name: the pack's ams2Class
        // and every track id (primaries and fallbacks) resolved against the real library.
        Assert.DoesNotContain(errorMessages, m => m.Contains(pack.Season.Ams2Class));

        var trackIds = pack.Season.Rounds
            .SelectMany(r => r.Track.Fallbacks.Prepend(r.Track.Id))
            .Distinct(StringComparer.Ordinal);
        foreach (var trackId in trackIds)
        {
            Assert.DoesNotContain(errorMessages, m => m.Contains($"'{trackId}'"));
        }
    }

    // ---------- formatting ----------

    private static string FormatStructural(PackValidationReport report) =>
        report.Issues.Count == 0
            ? "(no issues)"
            : string.Join("\n", report.Issues.Select(i => $"  {i.Severity}: {i.Message}"));

    private static string FormatContent(PreflightReport report) =>
        report.Issues.Count == 0
            ? "(no issues)"
            : string.Join("\n", report.Issues.Select(i => $"  {i.Severity}: {i.Message}"));
}
