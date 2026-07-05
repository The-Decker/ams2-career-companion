using Companion.ViewModels.Services;
using Companion.ViewModels.Wizard;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Warning aggregation (fix round): the livery scan reports as ONE summary line on the
/// wizard verification screen and in the staging messages — never a wall of per-file rows.
/// Leniently recovered community files are a count, not warnings; only files that yield
/// nothing even via the regex scrape warn, with the per-file list behind a collapsed
/// details section.
/// </summary>
public sealed class LiveryScanAggregationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-scan-agg-").FullName;
    private readonly FakeCareerFactory _factory = new();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string DocumentsDirectory => Path.Combine(_root, "docs");

    // ---------- override-file fixtures (the shapes live on this machine) ----------

    private const string CleanOverride = """
        <USER_OVERRIDES>
          <LIVERY_OVERRIDE LIVERY="livery_51" NAME="Stock Livery #1" />
        </USER_OVERRIDES>
        """;

    private const string CommunityOverride = """
        <USER_OVERRIDES>
          <!-- ---- calendar table drawn with dashes ---- -->
          <LIVERY_OVERRIDE LIVERY="livery_52" NAME="Community Livery #2" />
        </USER_OVERRIDES>
        """;

    private const string HopelessOverride = "<< not xml, and nothing to scrape >>";

    /// <summary>Writes an override XML under the DOCUMENTS-side scan root (the root the
    /// wizard reaches without an install).</summary>
    private string WriteDocumentsOverride(string vehicleFolder, string fileName, string content)
    {
        string directory = Path.Combine(
            DocumentsDirectory, "Automobilista 2", @"Vehicles\Textures\CustomLiveries\Overrides", vehicleFolder);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    // ---------- wizard verification ----------

    private NewCareerWizardViewModel WizardAtVerification()
    {
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), Path.Combine(_root, "packs", "clean"));
        var environment = new CareerEnvironment
        {
            ContentLibrary = TestPackBuilder.Library(),
            LocateInstall = () => null,
            DocumentsDirectory = DocumentsDirectory,
        };
        var wizard = new NewCareerWizardViewModel(
            environment,
            _factory,
            packSearchRoots: [Path.Combine(_root, "packs")],
            careersDirectory: Path.Combine(_root, "careers"),
            seedSource: new Random(1234));
        wizard.SelectedPack = Assert.Single(wizard.Packs);
        wizard.NextCommand.Execute(null);
        Assert.Equal(WizardStep.Verification, wizard.Step);
        return wizard;
    }

    [Fact]
    public void Wizard_AllFilesReadable_OneInfoLine_NoGate()
    {
        WriteDocumentsOverride("car_a", "a.xml", CleanOverride);
        WriteDocumentsOverride("car_b", "b.xml", CommunityOverride); // lenient recovery

        var wizard = WizardAtVerification();

        // ONE aggregate line — info, so the proceed-anyway gate never appears.
        var scanItems = wizard.VerificationItems.Where(i => i.Message.StartsWith("Livery scan:")).ToList();
        var item = Assert.Single(scanItems);
        Assert.Equal(
            "Livery scan: 2 liveries from 2 files; 1 recovered leniently; 0 unreadable",
            item.Message);
        Assert.True(item.IsInfo);
        Assert.Equal("Info", item.Severity);

        Assert.False(wizard.HasErrors);
        Assert.False(wizard.HasWarnings);
        Assert.True(wizard.CanGoNext); // no ProceedAnyway needed
        Assert.False(wizard.HasLiveryScanDetails);
    }

    [Fact]
    public void Wizard_UnreadableFiles_OneWarningLine_DetailsCollapsedByDefault()
    {
        WriteDocumentsOverride("car_a", "a.xml", CleanOverride);
        string hopeless = WriteDocumentsOverride("car_b", "broken.xml", HopelessOverride);

        var wizard = WizardAtVerification();

        // Still exactly ONE scan line — now a warning, so the gate applies.
        var scanItems = wizard.VerificationItems.Where(i => i.Message.StartsWith("Livery scan:")).ToList();
        var item = Assert.Single(scanItems);
        Assert.StartsWith("Livery scan: 1 livery from 2 files; 0 recovered leniently; 1 unreadable", item.Message);
        Assert.False(item.IsInfo);
        Assert.False(item.IsError);
        Assert.Equal("Warning", item.Severity);
        Assert.True(wizard.HasWarnings);
        Assert.False(wizard.CanGoNext);
        wizard.ProceedAnyway = true;
        Assert.True(wizard.CanGoNext);

        // The per-file list lives behind the collapsed-by-default details section.
        Assert.True(wizard.HasLiveryScanDetails);
        Assert.False(wizard.LiveryScanDetailsExpanded);
        Assert.Contains(wizard.LiveryScanDetails, d => d.Contains(hopeless));
        wizard.ToggleLiveryScanDetailsCommand.Execute(null);
        Assert.True(wizard.LiveryScanDetailsExpanded);
    }

    [Fact]
    public void Wizard_NoOverrideFilesAnywhere_NoScanLineAtAll()
    {
        var wizard = WizardAtVerification();

        Assert.Empty(wizard.VerificationItems); // preserves the "all clear" state
        Assert.False(wizard.HasLiveryScanDetails);
    }

    [Fact]
    public void Wizard_ManyCommunityFiles_NeverProducePerFileRows()
    {
        for (int i = 0; i < 40; i++)
            WriteDocumentsOverride($"car_{i}", $"skin_{i}.xml", CommunityOverride);

        var wizard = WizardAtVerification();

        // The old behavior was 40 warning rows; now it is ONE info line.
        var item = Assert.Single(wizard.VerificationItems, i => i.Message.StartsWith("Livery scan:"));
        Assert.Contains("40 recovered leniently", item.Message);
        Assert.True(item.IsInfo);
        Assert.False(wizard.HasWarnings);
    }

    // ---------- staging messages (briefing banner's message list) ----------

    private string FakeInstallDirectory => Path.Combine(_root, "install");

    private string WriteInstallOverride(string vehicleFolder, string fileName, string content)
    {
        string directory = Path.Combine(
            FakeInstallDirectory, @"Vehicles\Textures\CustomLiveries\Overrides", vehicleFolder);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private CareerSessionService RealSession()
    {
        var environment = ViewModelTestData.Environment(DocumentsDirectory, FakeInstallDirectory);
        return CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = ViewModelTestData.RealPackDirectory,
            CareerFilePath = Path.Combine(_root, "career.ams2career"),
            CareerName = "Test 1967",
            MasterSeed = 42,
            PlayerLiveryName = "Brabham-Repco #2 D. Hulme",
        }, environment);
    }

    [Fact]
    public void Staging_MessagesCarryOneScanSummary_DetailsListTheUnreadableFiles()
    {
        WriteInstallOverride("car_a", "clean.xml", CleanOverride);
        WriteInstallOverride("car_b", "community.xml", CommunityOverride);
        string hopeless = WriteInstallOverride("car_c", "broken.xml", HopelessOverride);
        using var session = RealSession();

        var outcome = session.StageCurrentGrid();

        Assert.True(outcome.Success); // livery findings are warnings — staging proceeds

        var scanLines = outcome.Messages.Where(m => m.Contains("Livery scan", StringComparison.Ordinal)).ToList();
        Assert.Single(scanLines);
        Assert.Equal("Livery scan: 2 liveries from 3 files; 1 recovered leniently; 1 unreadable", scanLines[0]);

        // No per-file warning rows in the messages; the file list rides in Details.
        Assert.DoesNotContain(outcome.Messages, m => m.Contains(hopeless));
        Assert.Contains(outcome.Details, d => d.Contains(hopeless));
    }

    [Fact]
    public void Staging_CommunityFilesOnly_RecoverSilently_NoDetails()
    {
        WriteInstallOverride("car_b", "community.xml", CommunityOverride);
        using var session = RealSession();

        var outcome = session.StageCurrentGrid();

        Assert.True(outcome.Success);
        Assert.Contains(
            "Livery scan: 1 livery from 1 file; 1 recovered leniently; 0 unreadable",
            outcome.Messages);
        Assert.Empty(outcome.Details);
    }
}
