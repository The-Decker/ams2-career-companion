using Companion.Ams2.CustomAi;

namespace Companion.Tests.Ams2;

/// <summary>
/// Backup naming under rapid-fire snapshots: the live integration check proved a force-stage
/// followed by a restore WITHIN THE SAME WALL-CLOCK SECOND collided on the second-resolution
/// backup name and failed the restore. Same-second snapshots must uniquify (-2, -3, …) and
/// ListBackups must order by when the backup was TAKEN, not by the copied file's write time.
/// </summary>
public sealed class CustomAiBackupTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("companion-backup-").FullName;

    private static readonly DateTimeOffset SameSecond = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private string LivePath => Path.Combine(_dir, "F-Vintage_Gen1.xml");

    [Fact]
    public void BackupIfPresent_SameSecondTwice_UniquifiesInsteadOfThrowing()
    {
        var backup = new CustomAiBackup(_dir);
        File.WriteAllText(LivePath, "<custom_ai_drivers>first</custom_ai_drivers>");

        string? first = backup.BackupIfPresent("F-Vintage_Gen1", SameSecond);
        File.WriteAllText(LivePath, "<custom_ai_drivers>second</custom_ai_drivers>");
        string? second = backup.BackupIfPresent("F-Vintage_Gen1", SameSecond);
        File.WriteAllText(LivePath, "<custom_ai_drivers>third</custom_ai_drivers>");
        string? third = backup.BackupIfPresent("F-Vintage_Gen1", SameSecond);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(third);
        Assert.Equal(3, new[] { first, second, third }.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.EndsWith("Z.xml", first);
        Assert.EndsWith("Z-2.xml", second);
        Assert.EndsWith("Z-3.xml", third);

        // Each snapshot preserved the exact bytes it saw.
        Assert.Contains("first", File.ReadAllText(first!));
        Assert.Contains("second", File.ReadAllText(second!));
        Assert.Contains("third", File.ReadAllText(third!));
    }

    [Fact]
    public void ListBackups_OrdersByTakenTimestampAndSameSecondSequence_NewestFirst()
    {
        var backup = new CustomAiBackup(_dir);

        // The copied file's WRITE time travels with the copy, so make the source "old":
        // ordering must come from the backup NAME's taken-time, never from file times.
        File.WriteAllText(LivePath, "<custom_ai_drivers>old source</custom_ai_drivers>");
        File.SetLastWriteTimeUtc(LivePath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        string? older = backup.BackupIfPresent("F-Vintage_Gen1", SameSecond.AddMinutes(-5));
        string? first = backup.BackupIfPresent("F-Vintage_Gen1", SameSecond);
        string? second = backup.BackupIfPresent("F-Vintage_Gen1", SameSecond);

        var listed = backup.ListBackups("F-Vintage_Gen1");

        Assert.Equal(3, listed.Count);
        // Same-second pair: the -2 suffix is NEWER than its unsuffixed base (naive string
        // order would invert them); the older timestamp lists last.
        Assert.Equal(second, listed[0]);
        Assert.Equal(first, listed[1]);
        Assert.Equal(older, listed[2]);
    }
}
