using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Companion.Ams2;

/// <summary>Everything the app needs to know about a located AMS2 install.</summary>
public sealed record Ams2Installation
{
    public required string InstallDirectory { get; init; }

    /// <summary>Where generated custom-AI XML files go (created if missing).</summary>
    public string CustomAiDriversDirectory => Path.Combine(InstallDirectory, "UserData", "CustomAIDrivers");

    /// <summary>Install-side skin override root (how AMS2 Content Manager deploys packs).</summary>
    public string InstallOverridesDirectory =>
        Path.Combine(InstallDirectory, "Vehicles", "Textures", "CustomLiveries", "Overrides");
}

/// <summary>
/// Finds the AMS2 install: Steam root from the registry, library folders from
/// libraryfolders.vdf, then the app folder itself. Every step can fail on non-standard
/// setups, the UI keeps a manual folder picker as the fallback.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SteamLocator
{
    private const string Ams2AppId = "1066890";
    private const string Ams2FolderName = "Automobilista 2";

    public static Ams2Installation? FindAms2()
    {
        foreach (var library in FindLibraryRoots())
        {
            string installDir = Path.Combine(library, "steamapps", "common", Ams2FolderName);
            string manifest = Path.Combine(library, "steamapps", $"appmanifest_{Ams2AppId}.acf");
            if (Directory.Exists(installDir) && File.Exists(manifest))
                return new Ams2Installation { InstallDirectory = installDir };
        }
        return null;
    }

    public static string? FindSteamRoot()
    {
        string? path =
            Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string
            ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")?.GetValue("InstallPath") as string;

        if (path is null)
            return null;

        path = path.Replace('/', Path.DirectorySeparatorChar);
        return Directory.Exists(path) ? path : null;
    }

    /// <summary>The Steam root plus every library from libraryfolders.vdf ("path" entries).</summary>
    public static IReadOnlyList<string> FindLibraryRoots()
    {
        var roots = new List<string>();
        string? steamRoot = FindSteamRoot();
        if (steamRoot is null)
            return roots;

        roots.Add(steamRoot);

        string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
            return roots;

        // VDF is Valve's key-value text format; the "path" values are all we need, so a
        // targeted regex beats a full parser here.
        foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
        {
            string library = match.Groups[1].Value.Replace(@"\\", @"\");
            if (Directory.Exists(library) &&
                !roots.Contains(library, StringComparer.OrdinalIgnoreCase))
                roots.Add(library);
        }

        return roots;
    }
}
