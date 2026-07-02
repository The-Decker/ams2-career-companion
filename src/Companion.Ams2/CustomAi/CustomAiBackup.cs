namespace Companion.Ams2.CustomAi;

/// <summary>
/// Timestamped backup/restore for files under <c>UserData\CustomAIDrivers\</c>. The user's
/// folder typically holds community NAMeS files they actively curate — the app NEVER
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
    /// </summary>
    public string? BackupIfPresent(string vehicleClass, DateTimeOffset now)
    {
        string source = Path.Combine(_customAiDriversDirectory, vehicleClass + ".xml");
        if (!File.Exists(source))
            return null;

        Directory.CreateDirectory(BackupDirectory);
        string backupPath = Path.Combine(
            BackupDirectory,
            $"{vehicleClass}.{now.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}.xml");
        File.Copy(source, backupPath, overwrite: false);
        return backupPath;
    }

    /// <summary>All backups for a class, newest first.</summary>
    public IReadOnlyList<string> ListBackups(string vehicleClass)
    {
        if (!Directory.Exists(BackupDirectory))
            return [];
        return Directory.GetFiles(BackupDirectory, vehicleClass + ".*.xml")
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Restores the newest backup over the live class file. Returns false when no
    /// backup exists. The overwritten app-generated file is not preserved — it can always be
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
