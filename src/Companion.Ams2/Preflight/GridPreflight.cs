using Companion.Ams2.ContentLibrary;
using Companion.Ams2.CustomAi;

namespace Companion.Ams2.Preflight;

public enum PreflightSeverity
{
    /// <summary>The grid will not work as intended in-game.</summary>
    Error,

    /// <summary>Something looks off but the game will still run (proceed-anyway territory).</summary>
    Warning,
}

public sealed record PreflightIssue
{
    public required PreflightSeverity Severity { get; init; }
    public required string Message { get; init; }
}

public sealed record PreflightReport
{
    public required IReadOnlyList<PreflightIssue> Issues { get; init; }
    public bool HasErrors => Issues.Any(i => i.Severity == PreflightSeverity.Error);
}

/// <summary>
/// Validates a generated custom-AI grid before it is staged: class name against the content
/// library (exact case — the filename IS the binding), livery names against installed skin
/// overrides + known stock names (the #1 community failure mode), grid size against the
/// venue's AI cap, per-track override ids against the track library.
/// </summary>
public static class GridPreflight
{
    public static PreflightReport Check(
        CustomAiFile file,
        Ams2ContentLibrary library,
        IReadOnlyCollection<InstalledLivery> installedLiveries,
        string? trackId = null,
        int? gridSize = null)
    {
        var issues = new List<PreflightIssue>();

        CheckClassName(file, library, issues);
        CheckLiveryNames(file, library, installedLiveries, issues);
        CheckTrackReferences(file, library, issues);
        CheckGridSize(library, trackId, gridSize ?? file.Drivers.Count(d => d.Tracks.Count == 0), issues);

        return new PreflightReport { Issues = issues };
    }

    private static void CheckClassName(CustomAiFile file, Ams2ContentLibrary library, List<PreflightIssue> issues)
    {
        if (library.Classes.ContainsKey(file.VehicleClass))
            return;

        var caseInsensitive = library.Classes.Keys.FirstOrDefault(k =>
            string.Equals(k, file.VehicleClass, StringComparison.OrdinalIgnoreCase));

        issues.Add(new PreflightIssue
        {
            Severity = PreflightSeverity.Error,
            Message = caseInsensitive is not null
                ? $"Vehicle class '{file.VehicleClass}' does not match the game's casing '{caseInsensitive}' — " +
                  "the file name is case-sensitive and the game will ignore it."
                : $"Vehicle class '{file.VehicleClass}' is not in the content library " +
                  $"(extracted from {library.ExtractedFrom}).",
        });
    }

    private static void CheckLiveryNames(
        CustomAiFile file,
        Ams2ContentLibrary library,
        IReadOnlyCollection<InstalledLivery> installedLiveries,
        List<PreflightIssue> issues)
    {
        var installed = installedLiveries.Select(l => l.Name).ToHashSet(StringComparer.Ordinal);
        var stock = library.Liveries.TryGetValue(file.VehicleClass, out var entry)
            ? entry.StockLib1563.ToHashSet(StringComparer.Ordinal)
            : [];

        var known = new HashSet<string>(installed, StringComparer.Ordinal);
        known.UnionWith(stock);

        var duplicates = file.Drivers
            .Where(d => d.Tracks.Count == 0)
            .GroupBy(d => d.LiveryName, StringComparer.Ordinal)
            .Where(g => g.Count() > 1);
        foreach (var duplicate in duplicates)
        {
            issues.Add(new PreflightIssue
            {
                Severity = PreflightSeverity.Error,
                Message = $"Livery '{duplicate.Key}' has {duplicate.Count()} base entries — only one driver can bind to a livery.",
            });
        }

        foreach (var driver in file.Drivers.DistinctBy(d => d.LiveryName, StringComparer.Ordinal))
        {
            if (known.Count > 0 && known.Contains(driver.LiveryName))
                continue;

            var nearMiss = known.FirstOrDefault(k =>
                string.Equals(k, driver.LiveryName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(k.Trim(), driver.LiveryName.Trim(), StringComparison.Ordinal));

            if (nearMiss is not null)
            {
                issues.Add(new PreflightIssue
                {
                    Severity = PreflightSeverity.Error,
                    Message = $"Livery '{driver.LiveryName}' does not exactly match installed/known livery " +
                              $"'{nearMiss}' (case or whitespace differs — the binding is exact-match).",
                });
            }
            else if (known.Count > 0)
            {
                issues.Add(new PreflightIssue
                {
                    Severity = PreflightSeverity.Warning,
                    Message = $"Livery '{driver.LiveryName}' was not found among installed skin overrides or known " +
                              $"stock names for {file.VehicleClass} — the entry will not bind unless the pack is installed.",
                });
            }
        }

        if (known.Count == 0)
        {
            issues.Add(new PreflightIssue
            {
                Severity = PreflightSeverity.Warning,
                Message = $"No livery reference data for class {file.VehicleClass} (no installed overrides scanned, " +
                          "no stock library entry) — livery bindings cannot be verified.",
            });
        }
    }

    private static void CheckTrackReferences(CustomAiFile file, Ams2ContentLibrary library, List<PreflightIssue> issues)
    {
        foreach (var trackId in file.Drivers.SelectMany(d => d.Tracks).Distinct(StringComparer.Ordinal))
        {
            if (!library.Tracks.ContainsKey(trackId))
            {
                var caseInsensitive = library.Tracks.Keys.FirstOrDefault(k =>
                    string.Equals(k, trackId, StringComparison.OrdinalIgnoreCase));
                issues.Add(new PreflightIssue
                {
                    Severity = PreflightSeverity.Warning,
                    Message = caseInsensitive is not null
                        ? $"Per-track override id '{trackId}' differs from library id '{caseInsensitive}' in case."
                        : $"Per-track override id '{trackId}' is not in the track library.",
                });
            }
        }
    }

    private static void CheckGridSize(
        Ams2ContentLibrary library,
        string? trackId,
        int gridSize,
        List<PreflightIssue> issues)
    {
        if (trackId is null)
            return;

        if (!library.Tracks.TryGetValue(trackId, out var track))
        {
            issues.Add(new PreflightIssue
            {
                Severity = PreflightSeverity.Warning,
                Message = $"Track '{trackId}' is not in the track library; grid-size cap cannot be checked.",
            });
            return;
        }

        if (gridSize > track.MaxAiParticipants)
        {
            issues.Add(new PreflightIssue
            {
                Severity = PreflightSeverity.Error,
                Message = $"Grid of {gridSize} exceeds {track.TrackName ?? track.Id}'s AI cap of " +
                          $"{track.MaxAiParticipants} — the game will fill fewer cars than the entry list.",
            });
        }
    }
}
