using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Companion.Ams2.CustomAi;

namespace Companion.Ams2.Preflight;

/// <summary>A livery display name declared by an installed skin-pack override XML.</summary>
public sealed record InstalledLivery
{
    /// <summary>The LIVERY_OVERRIDE NAME attribute, the display name a custom-AI
    /// livery_name must match exactly (case- and whitespace-sensitive).</summary>
    public required string Name { get; init; }

    /// <summary>The vehicle folder the override lives under (e.g. formula_vintage_g1m1).</summary>
    public required string VehicleFolder { get; init; }

    public required string SourceFile { get; init; }

    /// <summary>The raw LIVERY slot attribute (e.g. "53", or "##"/"" for a placeholder). AMS2 only
    /// loads a livery whose slot is a real number, a "##" placeholder is shipped on disk but NOT
    /// switched on in-game until a selector assigns it a real slot. This is why a skin pack can
    /// list a livery the game never shows.</summary>
    public string Slot { get; init; } = "";

    /// <summary>True when this override is ACTIVE in-game: its <see cref="Slot"/> is a real
    /// positive number (not a "##" placeholder / catalog-only entry). Only active liveries appear
    /// on the vehicle-selection screen and bind an AI's livery_name to a skin.</summary>
    public bool IsActive =>
        int.TryParse(Slot, System.Globalization.NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int slot) && slot > 0;
}

/// <summary>Structured outcome of one livery-override scan: the liveries plus the aggregate
/// counts the UI reports as ONE summary line instead of per-file warning rows.</summary>
public sealed record LiveryScanResult
{
    public required IReadOnlyList<InstalledLivery> Liveries { get; init; }

    /// <summary>Every override XML the scan visited (readable or not).</summary>
    public required int FilesScanned { get; init; }

    /// <summary>Files that failed strict XML parsing but yielded their liveries through the
    /// lenient pass (comment/ampersand repair) or the regex scrape, the normal state of
    /// community skin packs, worth a count but never a warning.</summary>
    public required int FilesRecoveredLeniently { get; init; }

    /// <summary>"path: reason" per file that yielded NOTHING even via the regex scrape —
    /// the only files that still warrant a warning.</summary>
    public required IReadOnlyList<string> UnreadableFiles { get; init; }

    /// <summary>The one-line report for the wizard verification screen and the staging
    /// messages, e.g. "Livery scan: 1,057 liveries from 806 files; 84 recovered leniently;
    /// 0 unreadable".</summary>
    public string Summary =>
        $"Livery scan: {Count(Liveries.Count)} " + (Liveries.Count == 1 ? "livery" : "liveries") +
        $" from {Count(FilesScanned)} " + (FilesScanned == 1 ? "file" : "files") +
        $"; {Count(FilesRecoveredLeniently)} recovered leniently" +
        $"; {Count(UnreadableFiles.Count)} unreadable";

    private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}

/// <summary>
/// Scans installed skin packs for the livery display names they define, the ground truth the
/// preflight validator checks generated custom-AI files against. Overrides live under
/// <c>Vehicles\Textures\CustomLiveries\Overrides\</c> in EITHER the game install directory
/// (how AMS2 Content Manager deploys, verified on this machine) or
/// <c>Documents\Automobilista 2\</c> (manual installs per community guides): scan both.
///
/// Community override files are frequently not well-formed XML ('--' runs inside comments,
/// raw '&amp;' in texture paths, mismatched OUTFIT/HELMET tags, all live on this machine),
/// yet the game reads them. The scan therefore degrades per file: strict parse → lenient
/// parse (<see cref="LenientXml.Clean"/>) → regex scrape of the LIVERY_OVERRIDE NAME
/// attributes (the only thing the scanner needs). A file counts as unreadable ONLY when even
/// the regex finds nothing.
/// </summary>
public static class LiveryOverrideScanner
{
    private const string OverridesRelativePath = @"Vehicles\Textures\CustomLiveries\Overrides";

    private const string OverrideElement = "LIVERY_OVERRIDE";
    private const string NameAttribute = "NAME";
    private const string LiveryAttribute = "LIVERY";

    public static IReadOnlyList<string> CandidateOverrideRoots(string gameInstallDirectory, string documentsDirectory) =>
    [
        Path.Combine(gameInstallDirectory, OverridesRelativePath),
        Path.Combine(documentsDirectory, "Automobilista 2", OverridesRelativePath),
    ];

    /// <summary>Tuple-shaped convenience over <see cref="ScanDetailed"/> for callers that
    /// only need liveries + flat warnings (one per unreadable file).</summary>
    public static (IReadOnlyList<InstalledLivery> Liveries, IReadOnlyList<string> Warnings) Scan(
        IEnumerable<string> overrideRoots)
    {
        var result = ScanDetailed(overrideRoots);
        return (result.Liveries, result.UnreadableFiles);
    }

    /// <summary>Parses every override XML under the given roots (roots that don't exist are
    /// skipped). Malformed files degrade strict → lenient → regex; a file is reported
    /// unreadable, never thrown, only when all three passes yield nothing, so one broken
    /// community file cannot hide any other installed livery.</summary>
    public static LiveryScanResult ScanDetailed(IEnumerable<string> overrideRoots)
    {
        var liveries = new List<InstalledLivery>();
        var unreadable = new List<string>();
        int filesScanned = 0;
        int recovered = 0;

        foreach (var root in overrideRoots)
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var file in Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories))
            {
                filesScanned++;

                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    unreadable.Add($"{file}: {ex.Message}");
                    continue;
                }

                var entries = TryStrict(text);
                if (entries is null)
                {
                    entries = TryLenient(text);
                    if (entries is null)
                    {
                        // Strip <!-- --> spans BEFORE scraping: AMS2 ignores commented
                        // LIVERY_OVERRIDE entries (the "##" placeholder examples + Reiza's _dist
                        // template live entirely inside comments), so they must never be reported as
                        // installed liveries. The strict/lenient passes already skip comments; the
                        // regex fallback must too.
                        var scraped = LenientXml.ExtractElementAttributePairs(
                            LenientXml.StripComments(text), OverrideElement, LiveryAttribute, NameAttribute);
                        if (scraped.Count == 0)
                        {
                            unreadable.Add(
                                $"{file}: not readable as XML, and no {OverrideElement} {NameAttribute} attributes found.");
                            continue;
                        }
                        entries = scraped.Select(p => (p.Optional, p.Required)).ToList();
                    }
                    recovered++;
                }

                string vehicleFolder = VehicleFolderOf(root, file);
                foreach (var (slot, name) in entries)
                {
                    liveries.Add(new InstalledLivery
                    {
                        Name = name,
                        VehicleFolder = vehicleFolder,
                        SourceFile = file,
                        Slot = slot,
                    });
                }
            }
        }

        return new LiveryScanResult
        {
            Liveries = liveries,
            FilesScanned = filesScanned,
            FilesRecoveredLeniently = recovered,
            UnreadableFiles = unreadable,
        };
    }

    private static string VehicleFolderOf(string root, string file)
    {
        string relative = Path.GetRelativePath(root, file);
        return relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)[0];
    }

    /// <summary>Strict streaming parse; null when the markup is not well-formed XML. Captures the
    /// LIVERY slot alongside NAME so the caller can tell an active (numeric slot) livery from a
    /// "##" placeholder.</summary>
    private static List<(string Slot, string Name)>? TryStrict(string text)
    {
        var entries = new List<(string, string)>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                IgnoreComments = true,
                IgnoreWhitespace = true,
            });

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element ||
                    !reader.Name.Equals(OverrideElement, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (reader.GetAttribute(NameAttribute) is { Length: > 0 } name)
                    entries.Add((reader.GetAttribute(LiveryAttribute) ?? "", name));
            }
        }
        catch (XmlException)
        {
            return null; // partial finds are discarded, the lenient pass re-reads everything
        }
        return entries;
    }

    /// <summary>Comment-stripped, ampersand-repaired parse; null when the markup is broken
    /// beyond what cleaning fixes (mismatched tags, multiple roots, stray declarations).</summary>
    private static List<(string Slot, string Name)>? TryLenient(string text)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(LenientXml.Clean(text));
        }
        catch (XmlException)
        {
            return null;
        }

        return document.Descendants()
            .Where(e => e.Name.LocalName.Equals(OverrideElement, StringComparison.OrdinalIgnoreCase))
            .Select(e => (
                Name: Attribute(e, NameAttribute),
                Slot: Attribute(e, LiveryAttribute)))
            .Where(pair => pair.Name is { Length: > 0 })
            .Select(pair => (pair.Slot ?? "", pair.Name!))
            .ToList();
    }

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes()
            .FirstOrDefault(a => a.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
            ?.Value;
}
