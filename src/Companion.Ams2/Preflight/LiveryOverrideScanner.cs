using System.Xml;

namespace Companion.Ams2.Preflight;

/// <summary>A livery display name declared by an installed skin-pack override XML.</summary>
public sealed record InstalledLivery
{
    /// <summary>The LIVERY_OVERRIDE NAME attribute — the display name a custom-AI
    /// livery_name must match exactly (case- and whitespace-sensitive).</summary>
    public required string Name { get; init; }

    /// <summary>The vehicle folder the override lives under (e.g. formula_vintage_g1m1).</summary>
    public required string VehicleFolder { get; init; }

    public required string SourceFile { get; init; }
}

/// <summary>
/// Scans installed skin packs for the livery display names they define, the ground truth the
/// preflight validator checks generated custom-AI files against. Overrides live under
/// <c>Vehicles\Textures\CustomLiveries\Overrides\</c> in EITHER the game install directory
/// (how AMS2 Content Manager deploys — verified on this machine) or
/// <c>Documents\Automobilista 2\</c> (manual installs per community guides): scan both.
/// </summary>
public static class LiveryOverrideScanner
{
    private const string OverridesRelativePath = @"Vehicles\Textures\CustomLiveries\Overrides";

    public static IReadOnlyList<string> CandidateOverrideRoots(string gameInstallDirectory, string documentsDirectory) =>
    [
        Path.Combine(gameInstallDirectory, OverridesRelativePath),
        Path.Combine(documentsDirectory, "Automobilista 2", OverridesRelativePath),
    ];

    /// <summary>Parses every override XML under the given roots (roots that don't exist are
    /// skipped). Unreadable or malformed files are reported, not thrown — one broken community
    /// file must not hide every other installed livery.</summary>
    public static (IReadOnlyList<InstalledLivery> Liveries, IReadOnlyList<string> Warnings) Scan(
        IEnumerable<string> overrideRoots)
    {
        var liveries = new List<InstalledLivery>();
        var warnings = new List<string>();

        foreach (var root in overrideRoots)
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var file in Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories))
            {
                try
                {
                    ScanFile(root, file, liveries);
                }
                catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"{file}: {ex.Message}");
                }
            }
        }

        return (liveries, warnings);
    }

    private static void ScanFile(string root, string file, List<InstalledLivery> liveries)
    {
        string relative = Path.GetRelativePath(root, file);
        string vehicleFolder = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)[0];

        using var reader = XmlReader.Create(file, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        });

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element ||
                !reader.Name.Equals("LIVERY_OVERRIDE", StringComparison.OrdinalIgnoreCase))
                continue;

            if (reader.GetAttribute("NAME") is { Length: > 0 } name)
            {
                liveries.Add(new InstalledLivery
                {
                    Name = name,
                    VehicleFolder = vehicleFolder,
                    SourceFile = file,
                });
            }
        }
    }
}
