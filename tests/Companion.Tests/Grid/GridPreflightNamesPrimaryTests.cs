using Companion.Ams2.ContentLibrary;
using Companion.Ams2.CustomAi;
using Companion.Ams2.Preflight;

namespace Companion.Tests.Grid;

/// <summary>
/// NAMeS-primary preflight (Mike's requirement: "the names mod and ai changes must always be
/// primary if found; it has to be found before overwritten"). The installed CustomAIDrivers
/// class file is the AUTHORITY for which livery names are valid: a name it defines is valid
/// with NO warning even when zero skin overrides are installed (the exact Dallara-Ford case) —
/// at most an INFO note when no skin is deployed. Only a name in NEITHER the AI file NOR skins
/// NOR stock is a real Warning.
/// </summary>
public class GridPreflightNamesPrimaryTests
{
    private const string VehicleClass = "F-Classic_Gen3";

    // The exact ground-truth names from Mike's installed F-Classic_Gen3.xml (1990 pack).
    private const string Morbidelli = "Dallara-Ford #21 G. Morbidelli";
    private const string DeCesaris = "Dallara-Ford #22 A. de Cesaris";

    // ---------- the exact Dallara case: AI-file name is valid with zero skins ----------

    [Fact]
    public void NameInInstalledAiFile_IsValid_NoWarning_EvenWithZeroSkinOverrides()
    {
        var file = FileWith(Morbidelli, DeCesaris);
        var aiNames = AiNames(Morbidelli, DeCesaris);

        // No skin overrides, no stock library entry — the ONLY source is the installed AI file.
        var report = GridPreflight.Check(
            file, LibraryWithClassButNoStockLiveries(), installedLiveries: [],
            installedAiNames: aiNames);

        // The Dallara names must NOT produce any "won't bind" warning...
        Assert.DoesNotContain(report.Issues, i =>
            i.Severity == PreflightSeverity.Warning && i.Message.Contains(Morbidelli));
        Assert.DoesNotContain(report.Issues, i =>
            i.Severity == PreflightSeverity.Warning && i.Message.Contains(DeCesaris));
        Assert.False(report.HasErrors);

        // ...and never an Error either.
        Assert.DoesNotContain(report.Issues, i =>
            i.Severity == PreflightSeverity.Error && i.Message.Contains("Dallara"));
    }

    [Fact]
    public void NameInAiFileButNoSkin_IsAtMostInfo_NeverWarning()
    {
        var file = FileWith(Morbidelli);
        var report = GridPreflight.Check(
            file, LibraryWithClassButNoStockLiveries(), installedLiveries: [],
            installedAiNames: AiNames(Morbidelli));

        var dallara = Assert.Single(report.Issues, i => i.Message.Contains(Morbidelli));
        Assert.Equal(PreflightSeverity.Info, dallara.Severity);
        Assert.Contains("binds", dallara.Message);
        // Info never counts as an error (never gates staging).
        Assert.False(report.HasErrors);
    }

    [Fact]
    public void NameInAiFileWithMatchingSkin_ProducesNoIssueAtAll()
    {
        var file = FileWith(Morbidelli);
        var skin = new InstalledLivery
        {
            Name = Morbidelli,
            VehicleFolder = "formula_classic_gen3",
            SourceFile = "override.xml",
        };

        var report = GridPreflight.Check(
            file, LibraryWithClassButNoStockLiveries(), installedLiveries: [skin],
            installedAiNames: AiNames(Morbidelli));

        // Name binds AND a skin is deployed — not even an Info note.
        Assert.DoesNotContain(report.Issues, i => i.Message.Contains(Morbidelli));
    }

    // ---------- a name in NEITHER the AI file NOR skins NOR stock still warns ----------

    [Fact]
    public void NameInNeitherAiFileNorSkinsNorStock_StillWarns()
    {
        const string bogus = "Nonexistent-Team #99 Nobody";
        var file = FileWith(Morbidelli, bogus);

        var report = GridPreflight.Check(
            file, LibraryWithClassButNoStockLiveries(), installedLiveries: [],
            // The AI file defines Morbidelli but NOT the bogus livery.
            installedAiNames: AiNames(Morbidelli));

        // The Dallara name is silently valid...
        Assert.DoesNotContain(report.Issues, i =>
            i.Severity == PreflightSeverity.Warning && i.Message.Contains(Morbidelli));

        // ...but the genuinely-unknown livery is a real Warning.
        var warning = Assert.Single(report.Issues, i =>
            i.Severity == PreflightSeverity.Warning && i.Message.Contains(bogus));
        Assert.Contains("will not bind", warning.Message);
    }

    [Fact]
    public void NoInstalledAiNames_FallsBackToSkinsAndStock_AsBefore()
    {
        var file = FileWith(Morbidelli);

        // No AI file scanned and nothing else known: the legacy "cannot be verified" warning.
        var report = GridPreflight.Check(
            file, LibraryWithClassButNoStockLiveries(), installedLiveries: [],
            installedAiNames: null);

        Assert.Contains(report.Issues, i => i.Severity == PreflightSeverity.Warning);
    }

    // ---------- helpers ----------

    private static InstalledAiNameSet AiNames(params string[] names) => new()
    {
        VehicleClass = VehicleClass,
        LiveryNames = names,
        SourceFile = @"Y:\...\UserData\CustomAIDrivers\F-Classic_Gen3.xml",
    };

    private static CustomAiFile FileWith(params string[] liveryNames) => new()
    {
        VehicleClass = VehicleClass,
        Drivers = liveryNames.Select(n => new CustomAiDriver
        {
            LiveryName = n,
            RaceSkill = 0.8,
            QualifyingSkill = 0.8,
        }).ToList(),
    };

    /// <summary>A library where the class EXISTS (so no class-name error) but there is no stock
    /// livery entry for it — isolating the installed-AI-file name source, exactly like Mike's
    /// F-Classic_Gen3 whose names come only from the community 1990 AI file.</summary>
    private static Ams2ContentLibrary LibraryWithClassButNoStockLiveries() => new()
    {
        Classes = new Dictionary<string, Ams2Class>(StringComparer.Ordinal)
        {
            [VehicleClass] = new Ams2Class { XmlName = VehicleClass },
        },
        Vehicles = new Dictionary<string, Ams2Vehicle>(StringComparer.Ordinal),
        Tracks = new Dictionary<string, Ams2Track>(StringComparer.Ordinal),
        Liveries = new Dictionary<string, Ams2LiveryClassEntry>(StringComparer.Ordinal),
        ExtractedFrom = "unit-test",
    };
}
