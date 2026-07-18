namespace Companion.Ams2.CustomAi;

/// <summary>
/// Timestamped backup/restore for files under <c>UserData\CustomAIDrivers\</c>. The user's
/// folder typically holds community NAMeS files they actively curate, the app NEVER
/// overwrites a class XML without snapshotting the existing file first, and restore puts
/// back exactly what was there before the app touched it.
/// </summary>
public sealed class CustomAiBackup
{
    private const string BackupDirectoryName = "_companion-backups";

    private readonly string _customAiDriversDirectory;

    public CustomAiBackup(string customAiDriversDirectory)
    {
        _customAiDriversDirectory = customAiDriversDirectory;
    }

    public string BackupDirectory => Path.Combine(_customAiDriversDirectory, BackupDirectoryName);

    /// <summary>
    /// Snapshots the current <c>&lt;vehicleClass&gt;.xml</c> (if present) before the app
    /// writes its own. Returns the backup path, or null when there was nothing to back up.
    /// Backup names embed a sortable UTC timestamp: <c>F-Vintage_Gen1.20260702T153000Z.xml</c>.
    /// Two snapshots within the same second (a force-stage immediately followed by a restore)
    /// get a <c>-2</c>/<c>-3</c>… suffix instead of colliding, a backup must never fail
    /// because the previous one was recent.
    /// </summary>
    public string? BackupIfPresent(string vehicleClass, DateTimeOffset now)
    {
        string source = Path.Combine(_customAiDriversDirectory, vehicleClass + ".xml");
        if (!File.Exists(source))
            return null;

        Directory.CreateDirectory(BackupDirectory);
        string stem = Path.Combine(
            BackupDirectory,
            $"{vehicleClass}.{now.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}");
        string backupPath = stem + ".xml";
        for (int n = 2; File.Exists(backupPath); n++)
            backupPath = $"{stem}-{n}.xml";
        File.Copy(source, backupPath, overwrite: false);
        return backupPath;
    }

    /// <summary>All backups for a class, newest first, by when the backup was TAKEN, read
    /// from the name's embedded timestamp plus the same-second <c>-n</c> suffix (plain
    /// string order would put <c>…Z-2.xml</c> BEFORE <c>…Z.xml</c>, inverting a same-second
    /// pair; file times are no better, the copy preserves the source's write time).</summary>
    public IReadOnlyList<string> ListBackups(string vehicleClass)
    {
        if (!Directory.Exists(BackupDirectory))
            return [];
        return Directory.GetFiles(BackupDirectory, vehicleClass + ".*.xml")
            .OrderByDescending(path => TakenKey(vehicleClass, path).Stamp, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(path => TakenKey(vehicleClass, path).Sequence)
            .ToList();
    }

    /// <summary>Splits a backup file name into its taken-time key: the timestamp text and
    /// the same-second sequence (no suffix = 1). Unparseable names sort by their whole
    /// middle part, sequence 1, degraded but stable.</summary>
    private static (string Stamp, int Sequence) TakenKey(string vehicleClass, string path)
    {
        string middle = Path.GetFileNameWithoutExtension(path);
        if (middle.Length > vehicleClass.Length + 1 &&
            middle.StartsWith(vehicleClass + ".", StringComparison.OrdinalIgnoreCase))
            middle = middle[(vehicleClass.Length + 1)..];

        int dash = middle.IndexOf('-');
        if (dash > 0 && int.TryParse(middle[(dash + 1)..], out int sequence))
            return (middle[..dash], sequence);
        return (middle, 1);
    }

    /// <summary>Restores one specific backup over the live class file (callers that snapshot
    /// the current file first use this to restore a backup OLDER than that snapshot).
    /// Returns the live target path.</summary>
    public string Restore(string backupPath, string vehicleClass)
    {
        string target = Path.Combine(_customAiDriversDirectory, vehicleClass + ".xml");
        File.Copy(backupPath, target, overwrite: true);
        return target;
    }

    /// <summary>Restores the newest backup over the live class file. Returns false when no
    /// backup exists. The overwritten app-generated file is not preserved, it can always be
    /// regenerated, the user's original cannot.</summary>
    public bool RestoreLatest(string vehicleClass)
    {
        var backups = ListBackups(vehicleClass);
        if (backups.Count == 0)
            return false;

        string target = Path.Combine(_customAiDriversDirectory, vehicleClass + ".xml");
        File.Copy(backups[0], target, overwrite: true);
        return true;
    }
}
