using System.Text.Json;
using Companion.Core.Packs;

namespace Companion.ViewModels.Services;

/// <summary>A pack folder found by discovery: its manifest when readable, or the load error.</summary>
public sealed record DiscoveredPack
{
    public required string Directory { get; init; }

    public PackManifest? Manifest { get; init; }

    /// <summary>Set when pack.json was present but unreadable/invalid.</summary>
    public string? LoadError { get; init; }

    public string DisplayName => Manifest is null
        ? System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Directory))
        : $"{Manifest.Name} ({Manifest.Version})";
}

/// <summary>
/// Finds season packs for the wizard's season-pick step: every direct subfolder of the
/// search roots that contains a pack.json. Default roots per the contract: the bundled
/// packs\ folder beside the exe, then Documents\AMS2CareerCompanion\Packs\.
/// </summary>
public static class PackDiscovery
{
    public static IReadOnlyList<string> DefaultSearchRoots(string documentsDirectory) =>
    [
        Path.Combine(AppContext.BaseDirectory, "packs"),
        Path.Combine(documentsDirectory, "AMS2CareerCompanion", "Packs"),
    ];

    public static IReadOnlyList<DiscoveredPack> Discover(IEnumerable<string> searchRoots)
    {
        var packs = new List<DiscoveredPack>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string root in searchRoots)
        {
            if (!Directory.Exists(root))
                continue;

            foreach (string directory in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(Path.Combine(directory, "pack.json")))
                    continue;
                if (!seen.Add(Path.GetFullPath(directory)))
                    continue;

                packs.Add(Inspect(directory));
            }
        }

        return packs;
    }

    private static DiscoveredPack Inspect(string directory)
    {
        try
        {
            var manifest = PackLoader.ParseManifest(File.ReadAllText(Path.Combine(directory, "pack.json")));
            return new DiscoveredPack { Directory = directory, Manifest = manifest };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new DiscoveredPack { Directory = directory, LoadError = ex.Message };
        }
    }
}
