using Companion.Ams2.ContentLibrary;
using Companion.Ams2.CustomAi;

namespace Companion.Ams2.Preflight;

public enum PreflightSeverity
{
    /// <summary>The grid will not work as intended in-game.</summary>
    Error,

    /// <summary>Something looks off but the game will still run (proceed-anyway territory).</summary>
    Warning,

    /// <summary>A neutral, non-gating note (never blocks, never an amber "proceed-anyway"
    /// warning). Used for NAMeS-primary observations the user manages themselves, e.g. a
    /// name the installed AI file defines but has no deployed skin, which binds a name and
    /// falls back to the default skin (managed via the pack's own selector, not this app).</summary>
    Info,
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
/// library (exact case, the filename IS the binding), livery names against the installed
/// NAMeS/AI file (PRIMARY, "found before overwritten") then installed skin overrides + known
/// stock names, grid size against the venue's AI cap, per-track override ids against the track
/// library.
/// </summary>
public static class GridPreflight
{
    public static PreflightReport Check(
        CustomAiFile file,
        Ams2ContentLibrary library,
        IReadOnlyCollection<InstalledLivery> installedLiveries,
        string? trackId = null,
        int? gridSize = null,
        InstalledAiNameSet? installedAiNames = null)
    {
        var issues = new List<PreflightIssue>();

        CheckClassName(file, library, issues);
        CheckLiveryNameHygiene(file, issues);
        CheckLiveryNames(file, library, installedLiveries, installedAiNames, issues);
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
                ? $"Vehicle class '{file.VehicleClass}' does not match the game's casing '{caseInsensitive}', " +
                  "the file name is case-sensitive and the game will ignore it."
                : $"Vehicle class '{file.VehicleClass}' is not in the content library " +
                  $"(extracted from {library.ExtractedFrom}).",
        });
    }

    /// <summary>
    /// Livery validity, NAMeS-primary (Mike's requirement: the installed names/AI mod must be
    /// PRIMARY if found, "found before overwritten"). Precedence:
    /// <list type="number">
    /// <item>A name the INSTALLED AI FILE defines is VALID, no issue at all, whatever the skin
    /// state, because that file is the authority for what binds in-game.</item>
    /// <item>A name in an installed SKIN OVERRIDE or the STOCK library also binds, no issue.</item>
    /// <item>A name the AI file defines but with NO deployed skin is at most an INFO note (the
    /// name binds; the skin falls back to default, managed with the pack's own selector),
    /// never a Warning.</item>
    /// <item>A name in NEITHER the AI file NOR skins NOR stock is the only real Warning.</item>
    /// </list>
    /// </summary>
    private static void CheckLiveryNames(
        CustomAiFile file,
        Ams2ContentLibrary library,
        IReadOnlyCollection<InstalledLivery> installedLiveries,
        InstalledAiNameSet? installedAiNames,
        List<PreflightIssue> issues)
    {
        // PRIMARY: names the installed NAMeS/AI file for this class defines. Found before
        // overwritten, if the user's own AI file declares the livery, it is valid, period.
        var aiNames = installedAiNames is { LiveryNames.Count: > 0 }
            ? installedAiNames.LiveryNames.ToHashSet(StringComparer.Ordinal)
            : [];

        var skins = installedLiveries.Select(l => l.Name).ToHashSet(StringComparer.Ordinal);
        var stock = library.Liveries.TryGetValue(file.VehicleClass, out var entry)
            ? entry.StockLib1563.ToHashSet(StringComparer.Ordinal)
            : [];

        // "known" = valid names across every source. The AI file is one of them (and takes
        // precedence for the INFO-vs-Warning decision below).
        var known = new HashSet<string>(aiNames, StringComparer.Ordinal);
        known.UnionWith(skins);
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
                Message = $"Livery '{duplicate.Key}' has {duplicate.Count()} base entries, only one driver can bind to a livery.",
            });
        }

        foreach (var driver in file.Drivers.DistinctBy(d => d.LiveryName, StringComparer.Ordinal))
        {
            // (1) The installed AI file defines this name, it binds. The skin may or may not
            // be deployed; either way this is never a Warning.
            if (aiNames.Contains(driver.LiveryName))
            {
                if (!skins.Contains(driver.LiveryName) && !stock.Contains(driver.LiveryName))
                {
                    issues.Add(new PreflightIssue
                    {
                        Severity = PreflightSeverity.Info,
                        Message = $"Livery '{driver.LiveryName}' is defined by your installed {file.VehicleClass} " +
                                  "AI file, the name binds. No matching skin was scanned, so it may fall back to " +
                                  "the default skin; manage skins with the pack's own selector.",
                    });
                }
                continue;
            }

            // (2) A skin override or stock name also binds, no issue.
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
                              $"'{nearMiss}' (case or whitespace differs, the binding is exact-match).",
                });
            }
            else if (known.Count > 0)
            {
                // (4) In NEITHER the AI file NOR skins NOR stock, the only real Warning.
                issues.Add(new PreflightIssue
                {
                    Severity = PreflightSeverity.Warning,
                    Message = $"Livery '{driver.LiveryName}' was not found in your installed {file.VehicleClass} AI file, " +
                              "installed skin overrides, or known stock names, the entry will not bind unless the pack is installed.",
                });
            }
        }

        if (known.Count == 0)
        {
            issues.Add(new PreflightIssue
            {
                Severity = PreflightSeverity.Warning,
                Message = $"No livery reference data for class {file.VehicleClass} (no installed AI file, no installed " +
                          "overrides scanned, no stock library entry), livery bindings cannot be verified.",
            });
        }
    }

    /// <summary>
    /// Livery-name HYGIENE (AMS2 diagnosis #7): the binding is byte-exact, so a leading/trailing
    /// space or a non-ASCII byte in the <c>livery_name</c> (Reiza dev "hook issues") silently breaks
    /// the match, and a mismatch reverts the WHOLE class to stock names, not just one car. These are
    /// invisible in a UI, so flag them explicitly as warnings (we never auto-mutate the name, the
    /// real in-game livery might genuinely carry the odd byte).
    /// </summary>
    private static void CheckLiveryNameHygiene(CustomAiFile file, List<PreflightIssue> issues)
    {
        foreach (var livery in file.Drivers.Select(d => d.LiveryName).Distinct(StringComparer.Ordinal))
        {
            if (livery.Length != livery.Trim().Length)
                issues.Add(new PreflightIssue
                {
                    Severity = PreflightSeverity.Warning,
                    Message = $"Livery name '{livery}' has leading/trailing whitespace, the binding is byte-exact, " +
                              "so this likely won't match the in-game livery and can revert the whole class to stock names.",
                });

            if (livery.Any(c => c > 127))
                issues.Add(new PreflightIssue
                {
                    Severity = PreflightSeverity.Warning,
                    Message = $"Livery name '{livery}' contains non-ASCII characters, a known AMS2 match ('hook') issue; " +
                              "if this class shows stock names in-game, the accented byte is the likely cause.",
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
                          $"{track.MaxAiParticipants}, the game will fill fewer cars than the entry list.",
            });
        }
    }
}
