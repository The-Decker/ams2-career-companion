using System.Text.Json;
using Companion.Core.Packs;
using Companion.Core.Smgp;

namespace Companion.ViewModels.Services;

/// <summary>A pack folder found by discovery: its manifest when readable, or the load error.</summary>
public sealed record DiscoveredPack
{
    public required string Directory { get; init; }

    public PackManifest? Manifest { get; init; }

    /// <summary>The pack's season year, peeked from season.json (M6 era transitions pick the
    /// next pack by year). Null when season.json is missing/unreadable or has no year.</summary>
    public int? SeasonYear { get; init; }

    /// <summary>Set when pack.json was present but unreadable/invalid.</summary>
    public string? LoadError { get; init; }

    public string DisplayName => Manifest is null
        ? System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Directory))
        : $"{Manifest.Name} ({Manifest.Version})";

    /// <summary>The season name WITHOUT the pack version ("Formula One 1988"), for the season-pick
    /// cards. Falls back to the folder name when the manifest is unreadable.</summary>
    public string Title => Manifest?.Name is { Length: > 0 } name
        ? name
        : System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Directory));
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

    /// <summary>The v1 next-pack rule (PLAN M6 / ROADMAP sign-and-continue): among readable
    /// discovered packs, the one with the SMALLEST season year strictly greater than
    /// <paramref name="currentYear"/>; year ties break by pack id, then directory (ordinal),
    /// so the pick is deterministic. Null when no later-year pack exists.</summary>
    public static DiscoveredPack? NextAfter(IEnumerable<DiscoveredPack> packs, int currentYear) =>
        packs
            .Where(p => p is { Manifest: not null, LoadError: null, SeasonYear: { } year } &&
                        year > currentYear)
            .OrderBy(p => p.SeasonYear!.Value)
            .ThenBy(p => p.Manifest!.PackId, StringComparer.Ordinal)
            .ThenBy(p => p.Directory, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>The next historical pack. SMGP is a separate career entity and must never enter
    /// a historical changeover merely because its authored season year sorts next. Other style
    /// values remain eligible for forward compatibility; only the locked SMGP discriminator is
    /// excluded here.</summary>
    public static DiscoveredPack? NextHistoricalAfter(
        IEnumerable<DiscoveredPack> packs,
        int currentYear) =>
        NextAfter(
            packs.Where(p => p.Manifest is { } manifest &&
                             !string.Equals(
                                 manifest.CareerStyle,
                                 SmgpRules.CareerStyle,
                                 StringComparison.Ordinal)),
            currentYear);

    /// <summary>Pure continuation policy over already-discovered packs. SMGP continues on its
    /// own pinned pack through season 17 and then terminates; historical careers consider only
    /// ordinary historical packs and otherwise carry their current car forward one year.</summary>
    public static NextSeasonInfo? PlanNextSeason(
        PackManifest currentPack,
        int currentYear,
        int seasonOrdinal,
        IEnumerable<DiscoveredPack> discoveredPacks)
    {
        ArgumentNullException.ThrowIfNull(currentPack);
        ArgumentNullException.ThrowIfNull(discoveredPacks);

        int nextYear = checked(currentYear + 1);
        if (string.Equals(currentPack.CareerStyle, SmgpRules.CareerStyle, StringComparison.Ordinal))
        {
            if (seasonOrdinal >= SmgpRules.CampaignSeasons)
                return null;

            return Carryover(currentPack, nextYear);
        }

        var changeover = NextHistoricalAfter(discoveredPacks, currentYear);
        if (changeover?.Manifest is not null && changeover.SeasonYear == nextYear)
        {
            return new NextSeasonInfo
            {
                IsCarryover = false,
                PackDirectory = changeover.Directory,
                PackId = changeover.Manifest.PackId,
                PackName = changeover.Manifest.Name,
                SeasonYear = nextYear,
                BridgedYears = [],
            };
        }

        return Carryover(currentPack, nextYear);
    }

    private static NextSeasonInfo Carryover(PackManifest currentPack, int nextYear) => new()
    {
        IsCarryover = true,
        PackDirectory = "",
        PackId = currentPack.PackId,
        PackName = currentPack.Name,
        SeasonYear = nextYear,
        BridgedYears = [],
    };

    private static DiscoveredPack Inspect(string directory)
    {
        try
        {
            var manifest = PackLoader.ParseManifest(File.ReadAllText(Path.Combine(directory, "pack.json")));
            return new DiscoveredPack
            {
                Directory = directory,
                Manifest = manifest,
                SeasonYear = PeekSeasonYear(directory),
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new DiscoveredPack { Directory = directory, LoadError = ex.Message };
        }
    }

    /// <summary>Reads just the "year" property out of the pack's season.json, discovery must
    /// stay cheap (the wizard and the season review both scan whole folders), so no full
    /// five-file parse happens here. Null on any problem; a broken season.json only surfaces
    /// when the pack is actually selected.</summary>
    private static int? PeekSeasonYear(string packDirectory)
    {
        string path = Path.Combine(packDirectory, "season.json");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("year", out var year) &&
                   year.TryGetInt32(out int value)
                ? value
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
