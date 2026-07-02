using System.IO;
using System.Reflection;
using System.Windows;
using Companion.Ams2;
using Companion.Ams2.Preflight;

namespace Companion.App;

/// <summary>
/// Dev-preview shell: proves the published exe runs the real detection stack. Replaced by
/// the M4 career shell (wizard, briefing, result entry, standings).
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        VersionText.Text = $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)} dev preview · " +
                           "points engine oracle-verified 1950–2026";

        Loaded += (_, _) => DetectEnvironment();
    }

    private void DetectEnvironment()
    {
        try
        {
            var install = SteamLocator.FindAms2();
            if (install is null)
            {
                InstallText.Text = "⚠ AMS2 not found via Steam registry/library folders — " +
                                   "the full app will offer a manual folder picker.";
                return;
            }

            InstallText.Text = $"✔ AMS2 detected: {install.InstallDirectory}";

            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var (liveries, warnings) = LiveryOverrideScanner.Scan(
                LiveryOverrideScanner.CandidateOverrideRoots(install.InstallDirectory, documents));
            LiveriesText.Text = $"✔ {liveries.Count:N0} custom livery overrides installed " +
                                $"({liveries.DistinctBy(l => l.VehicleFolder).Count()} vehicle folders" +
                                (warnings.Count > 0 ? $", {warnings.Count} unreadable files" : "") + ")";

            int customAiFiles = Directory.Exists(install.CustomAiDriversDirectory)
                ? Directory.GetFiles(install.CustomAiDriversDirectory, "*.xml").Length
                : 0;
            CustomAiText.Text = $"✔ {customAiFiles} custom-AI files in UserData\\CustomAIDrivers " +
                                "(the app always backs these up before staging a generated grid)";
        }
        catch (Exception ex)
        {
            InstallText.Text = $"⚠ Environment detection failed: {ex.Message}";
        }
    }
}
