using Companion.Ams2.CustomAi;
using Companion.ViewModels.Services;
using Companion.ViewModels.Wizard;

namespace Companion.Tests.ViewModels;

/// <summary>
/// NAMeS-first baseline import (locked decision #7a): per-livery mapping from entries.json
/// onto the installed file's base entries, the wizard's opt-in step (default ON when the
/// installed class XML parses), the summary diff, and — the load-bearing invariant — that the
/// IMPORTED result is pinned: deleting the community file after creation changes nothing.
/// </summary>
public sealed class BaselineImportTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-baseline-").FullName;
    private readonly FakeCareerFactory _factory = new();

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

    /// <summary>Installed community file overriding TestPackBuilder's first entry
    /// (Stock Livery #1 = driver.brabham), jusk quirks included: dashed comment and a
    /// track-scoped entry that must stay OUT of the baseline.</summary>
    private const string InstalledXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!--Community AI file
        ----------------------------------------------------------------
        -->
        <custom_ai_drivers>
        	<driver livery_name="Stock Livery #1">
        		<name>Jack B. Community</name>
        		<country>AUS</country>
                <race_skill>0.93</race_skill>
                <qualifying_skill>0.94</qualifying_skill>
                <blue_flag_conceding>0.88</blue_flag_conceding>
        	</driver>
        	<driver livery_name="Stock Livery #1" tracks="Kyalami_Historic">
                <race_skill>0.99</race_skill>
        	</driver>
        	<driver livery_name="Not In The Pack #9">
                <race_skill>0.10</race_skill>
        	</driver>
        </custom_ai_drivers>
        """;

    // ---------- the mapper ----------

    [Fact]
    public void Apply_OverridesMatchedDriversFieldByField_AndCountsTheDiff()
    {
        var pack = TestPackBuilder.TwoRoundPack();
        var installed = CommunityAiReader.Parse(InstalledXml);

        var result = CommunityBaselineImport.Apply(pack, installed);

        var brabham = Assert.Single(result.Drivers, d => d.Id == "driver.brabham");
        Assert.Equal("Jack B. Community", brabham.Name);       // name imported
        Assert.Equal("AUS", brabham.Country);                  // country imported
        Assert.Equal(0.93, brabham.Ratings.RaceSkill);         // present fields override...
        Assert.Equal(0.94, brabham.Ratings.QualifyingSkill);
        Assert.Equal(0.88, brabham.Ratings.BlueFlagConceding); // ...even pack-optional ones
        Assert.Equal(0.5, brabham.Ratings.Aggression);         // absent fields keep pack values
        Assert.Equal(0.8, brabham.Ratings.Stamina);

        // The track-scoped 0.99 stayed round-level — NOT the baseline.
        Assert.NotEqual(0.99, brabham.Ratings.RaceSkill);

        // driver.hulme's livery has no community entry: pack-only fallback, untouched.
        var hulme = Assert.Single(result.Drivers, d => d.Id == "driver.hulme");
        Assert.Equal("driver.hulme", hulme.Name);
        Assert.Equal(0.8, hulme.Ratings.RaceSkill);

        Assert.Equal(1, result.ImportedDriverCount);
        Assert.Equal(1, result.PackOnlyDriverCount);
        Assert.Contains("1 drivers imported", result.Summary);
        Assert.Contains("1 pack-only", result.Summary);
    }

    // ---------- the wizard step ----------

    private string PacksRoot => Path.Combine(_root, "packs");

    private string InstallDirectory => Path.Combine(_root, "install");

    private string InstalledAiPath => Path.Combine(
        InstallDirectory, "UserData", "CustomAIDrivers", TestPackBuilder.VintageClass + ".xml");

    private NewCareerWizardViewModel Wizard(bool withInstall)
    {
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            installDirectory: withInstall ? InstallDirectory : null,
            library: TestPackBuilder.Library());
        return new NewCareerWizardViewModel(
            environment,
            _factory,
            packSearchRoots: [PacksRoot],
            careersDirectory: Path.Combine(_root, "careers"),
            seedSource: new Random(1234));
    }

    private NewCareerWizardViewModel WizardAtConfirm(bool withInstall, string? installedXml = InstalledXml)
    {
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), Path.Combine(PacksRoot, "pack"));
        if (withInstall && installedXml is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(InstalledAiPath)!);
            File.WriteAllText(InstalledAiPath, installedXml);
        }

        var wizard = Wizard(withInstall);
        wizard.SelectedPack = Assert.Single(wizard.Packs);
        wizard.NextCommand.Execute(null);                       // -> Verification
        Assert.Equal(WizardStep.Verification, wizard.Step);
        if (wizard.HasWarnings)
            wizard.ProceedAnyway = true;
        wizard.NextCommand.Execute(null);                       // -> SeatPick
        wizard.SelectedSeat = wizard.Seats.First(s => s.LiveryName == TestPackBuilder.StockLivery2);
        wizard.NextCommand.Execute(null);                       // -> Grid (choose the field)
        wizard.NextCommand.Execute(null);                       // -> Character (rules are loaded)
        Assert.Equal(WizardStep.Character, wizard.Step);
        Assert.NotNull(wizard.Character);                       // an archetype preset is pre-selected → valid
        wizard.NextCommand.Execute(null);                       // -> Confirm
        Assert.Equal(WizardStep.Confirm, wizard.Step);
        return wizard;
    }

    [Fact]
    public void Wizard_ParseableInstalledFile_OffersImport_DefaultOn_WithSummaryDiff()
    {
        var wizard = WizardAtConfirm(withInstall: true);

        Assert.True(wizard.BaselineImportAvailable);
        Assert.True(wizard.UseInstalledAiBaseline); // DEFAULT ON when parseable
        Assert.Null(wizard.BaselineImportError);
        Assert.Equal(InstalledAiPath, wizard.InstalledAiFilePath);
        Assert.Equal(1, wizard.BaselineImportedCount);
        Assert.Equal(1, wizard.BaselinePackOnlyCount);
        Assert.Contains("1 drivers imported", wizard.BaselineImportSummary);

        wizard.NextCommand.Execute(null); // Create
        var request = _factory.LastRequest!;
        Assert.Equal(InstalledXml, request.CommunityBaselineXml);
        Assert.Equal(InstalledAiPath, request.CommunityBaselineSourcePath);
    }

    [Fact]
    public void Wizard_ImportToggledOff_CreatesWithPackBaseline()
    {
        var wizard = WizardAtConfirm(withInstall: true);

        wizard.UseInstalledAiBaseline = false;
        wizard.NextCommand.Execute(null); // Create

        Assert.Null(_factory.LastRequest!.CommunityBaselineXml);
        Assert.Null(_factory.LastRequest!.CommunityBaselineSourcePath);
    }

    [Fact]
    public void Wizard_UnparseableInstalledFile_DisablesImportWithAnError()
    {
        var wizard = WizardAtConfirm(withInstall: true,
            installedXml: "<custom_ai_drivers><driver livery_name=");

        Assert.False(wizard.BaselineImportAvailable);
        Assert.False(wizard.UseInstalledAiBaseline);
        Assert.NotNull(wizard.BaselineImportError);
        Assert.Equal(InstalledAiPath, wizard.InstalledAiFilePath); // the file WAS found

        wizard.NextCommand.Execute(null); // Create still works — pack baseline
        Assert.Null(_factory.LastRequest!.CommunityBaselineXml);
    }

    [Fact]
    public void Wizard_NoInstallOrNoClassFile_ImportUnavailable()
    {
        var wizard = WizardAtConfirm(withInstall: false);

        Assert.False(wizard.BaselineImportAvailable);
        Assert.False(wizard.UseInstalledAiBaseline);
        Assert.Null(wizard.BaselineImportError);
        Assert.Null(wizard.InstalledAiFilePath);
    }

    // ---------- pinned independence (the real service) ----------

    private CareerEnvironment ServiceEnvironment() => ViewModelTestData.Environment(
        documentsDirectory: Path.Combine(_root, "docs"),
        installDirectory: InstallDirectory,
        library: TestPackBuilder.Library());

    private CareerCreationRequest Request(string careerFile, string? baselineXml) => new()
    {
        PackDirectory = Path.Combine(PacksRoot, "pack"),
        CareerFilePath = Path.Combine(_root, "careers", careerFile),
        CareerName = "Pinned Import Career",
        MasterSeed = 42,
        PlayerLiveryName = TestPackBuilder.StockLivery2,
        CommunityBaselineXml = baselineXml,
        CommunityBaselineSourcePath = baselineXml is null ? null : InstalledAiPath,
    };

    [Fact]
    public void CreateCareer_PinsTheImportedBaseline_IndependentOfTheMutableFile()
    {
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), Path.Combine(PacksRoot, "pack"));
        Directory.CreateDirectory(Path.GetDirectoryName(InstalledAiPath)!);
        File.WriteAllText(InstalledAiPath, InstalledXml);
        var environment = ServiceEnvironment();

        string careerPath;
        using (var session = CareerSessionService.CreateCareer(Request("imported.ams2career", InstalledXml), environment))
        {
            careerPath = session.CareerFilePath;
            Assert.Equal(CareerSessionService.BaselineSourceInstalledAiFile, session.BaselineSource);

            var brabham = Assert.Single(session.Pack.Drivers, d => d.Id == "driver.brabham");
            Assert.Equal("Jack B. Community", brabham.Name);
            Assert.Equal(0.93, brabham.Ratings.RaceSkill);
        }

        // The career must NOT depend on the mutable installed file: delete it, reopen.
        File.Delete(InstalledAiPath);

        using var reopened = CareerSessionService.OpenCareer(careerPath, environment);
        Assert.Equal(CareerSessionService.BaselineSourceInstalledAiFile, reopened.BaselineSource);
        var pinned = Assert.Single(reopened.Pack.Drivers, d => d.Id == "driver.brabham");
        Assert.Equal("Jack B. Community", pinned.Name);
        Assert.Equal(0.93, pinned.Ratings.RaceSkill);
        Assert.Equal(0.94, pinned.Ratings.QualifyingSkill);
        Assert.Equal(0.88, pinned.Ratings.BlueFlagConceding);
        // Pack-only driver untouched, then and now.
        Assert.Equal("driver.hulme", Assert.Single(reopened.Pack.Drivers, d => d.Id == "driver.hulme").Name);
    }

    [Fact]
    public void CreateCareer_WithoutBaselineXml_RecordsPackSource()
    {
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), Path.Combine(PacksRoot, "pack"));
        var environment = ServiceEnvironment();

        string careerPath;
        using (var session = CareerSessionService.CreateCareer(Request("pack-baseline.ams2career", null), environment))
        {
            careerPath = session.CareerFilePath;
            Assert.Equal(CareerSessionService.BaselineSourcePack, session.BaselineSource);
            Assert.Equal("driver.brabham", Assert.Single(session.Pack.Drivers, d => d.Id == "driver.brabham").Name);
        }

        using var reopened = CareerSessionService.OpenCareer(careerPath, environment);
        Assert.Equal(CareerSessionService.BaselineSourcePack, reopened.BaselineSource);
    }
}
