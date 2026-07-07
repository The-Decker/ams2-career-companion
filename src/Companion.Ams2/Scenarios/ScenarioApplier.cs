using System.Globalization;

namespace Companion.Ams2.Scenarios;

/// <summary>Outcome of applying a round's override swaps: how many activated, any that were skipped
/// (variant file missing), the timestamped backups taken, and per-file errors.</summary>
public sealed record ScenarioApplyResult
{
    public required int Applied { get; init; }
    public required int Skipped { get; init; }
    public required IReadOnlyList<string> Backups { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }

    public bool AnyApplied => Applied > 0;
}

/// <summary>
/// Applies a round's <see cref="ScenarioSwap"/>s: for each, backs up the current active override
/// (<c>&lt;model&gt;.xml</c>) into a sibling <c>_companion-backups\</c> folder, then copies the round's
/// variant over it — exactly what the community scenario .bat does, but backup-first and from the app.
/// Purely a skin-file operation (livery overrides), so it never touches the career DB / sim / oracle.
/// </summary>
public static class ScenarioApplier
{
    private const string BackupFolder = "_companion-backups";

    public static ScenarioApplyResult Apply(
        string ams2RootDirectory, IReadOnlyList<ScenarioSwap> swaps, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(ams2RootDirectory);
        ArgumentNullException.ThrowIfNull(swaps);

        int applied = 0, skipped = 0;
        var backups = new List<string>();
        var errors = new List<string>();

        foreach (var swap in swaps)
        {
            string source = Path.Combine(ams2RootDirectory, swap.SourceRelativePath);
            string target = Path.Combine(ams2RootDirectory, swap.TargetRelativePath);

            if (!File.Exists(source))
            {
                skipped++;
                errors.Add($"Variant not installed: {swap.SourceRelativePath}");
                continue;
            }

            try
            {
                if (File.Exists(target))
                    backups.Add(BackUp(target, now));
                File.Copy(source, target, overwrite: true);
                applied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{swap.TargetRelativePath}: {ex.Message}");
            }
        }

        return new ScenarioApplyResult
        {
            Applied = applied,
            Skipped = skipped,
            Backups = backups,
            Errors = errors,
        };
    }

    private static string BackUp(string target, DateTimeOffset now)
    {
        string dir = Path.Combine(Path.GetDirectoryName(target)!, BackupFolder);
        Directory.CreateDirectory(dir);
        string stamp = now.UtcDateTime.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        string backup = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(target)}.{stamp}.xml");
        File.Copy(target, backup, overwrite: true);
        return backup;
    }
}
