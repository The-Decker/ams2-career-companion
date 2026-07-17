using System.Text.Json;
using Companion.Core.Json;
using Microsoft.Data.Sqlite;

namespace Companion.Data;

/// <summary>One save slot's display metadata (character-death plan §4). Stored as a sidecar JSON next
/// to the snapshot so the restore UI can list slots without opening each career DB.</summary>
public sealed record SaveSlotInfo
{
    /// <summary>Stable slot id and file stem — <c>manual-001</c> / <c>autosave-season-3</c>.</summary>
    public required string SlotId { get; init; }

    /// <summary>The human label shown in the restore UI.</summary>
    public required string Label { get; init; }

    /// <summary>The season year the snapshot was taken in.</summary>
    public required int SeasonYear { get; init; }

    /// <summary>The round the career was on when the snapshot was taken (1-based).</summary>
    public required int Round { get; init; }

    /// <summary>ISO-8601 UTC the snapshot was taken (also the newest-first sort key).</summary>
    public required string CreatedUtc { get; init; }

    /// <summary>True for an automatic season-start snapshot, false for a manual save.</summary>
    public required bool IsAutosave { get; init; }

    /// <summary>True for a snapshot whose metadata sidecar is missing or unreadable: the save is
    /// still restorable (only the snapshot file matters to <see cref="SaveSlotStore.Restore"/>),
    /// but its label/year/round are reconstructed from the file itself. Restorable data is
    /// surfaced as degraded, never silently hidden. Not <c>required</c> — older sidecars omit it.</summary>
    public bool IsDegraded { get; init; }
}

/// <summary>
/// FILE-level save &amp; reload for Normal-mode careers (docs/dev/character-death-injury.md §4). Because
/// the journal is append-only, reload is NOT a fold operation — it is restoring an entire earlier
/// career-DB snapshot. Each snapshot is a complete, self-consistent, replay-verifiable career file, so
/// this whole surface sits OUTSIDE the fold/replay contract and can never affect a re-simulation.
///
/// Snapshots live in a sibling <c>Saves/&lt;careerStem&gt;/</c> folder next to the working
/// <c>.ams2career</c>: <c>&lt;slotId&gt;.ams2save</c> is the SQLite snapshot (taken via the online-backup
/// API — a consistent copy even mid-play) and <c>&lt;slotId&gt;.json</c> is its <see cref="SaveSlotInfo"/>.
/// </summary>
public static class SaveSlotStore
{
    /// <summary>Extension of a snapshot file (a whole career DB copy).</summary>
    public const string SnapshotExtension = ".ams2save";

    private const string MetadataExtension = ".json";

    /// <summary>The <c>Saves/&lt;stem&gt;/</c> folder that holds this career's snapshots — a sibling of
    /// the working file, derived purely from its path (there is no stored "saves root").</summary>
    public static string SavesDirectoryFor(string careerFilePath)
    {
        string directory = Path.GetDirectoryName(careerFilePath) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(careerFilePath);
        return Path.Combine(directory, "Saves", stem);
    }

    /// <summary>
    /// Snapshots the LIVE career into a slot: backs up <paramref name="liveConnection"/> (the session's
    /// own open connection, so committed WAL data is captured too) into
    /// <c>Saves/&lt;stem&gt;/&lt;slotId&gt;.ams2save</c> and writes the sidecar metadata. Overwrites an
    /// existing slot of the same id. Returns the created slot's <see cref="SaveSlotInfo"/>.
    /// </summary>
    public static SaveSlotInfo Save(
        SqliteConnection liveConnection,
        string careerFilePath,
        string slotId,
        string label,
        int seasonYear,
        int round,
        string createdUtc,
        bool isAutosave)
    {
        ArgumentException.ThrowIfNullOrEmpty(slotId);
        string savesDir = SavesDirectoryFor(careerFilePath);
        Directory.CreateDirectory(savesDir);
        string snapshotPath = Path.Combine(savesDir, slotId + SnapshotExtension);

        // Back up into a TEMP file first, then swap it over the slot with a single move — so a failed
        // backup (or an overwrite of an existing slot) can never destroy a prior good snapshot.
        string writingPath = snapshotPath + ".writing";
        DeleteFileAndWalSiblings(writingPath);

        var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = writingPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        try
        {
            destination.Open();
            liveConnection.BackupDatabase(destination);
        }
        catch (SqliteException ex)
        {
            throw new InvalidOperationException($"Could not snapshot the career — {ex.Message}", ex);
        }
        finally
        {
            destination.Dispose();
            SqliteConnection.ClearPool(destination);
        }
        File.Move(writingPath, snapshotPath, overwrite: true);

        var info = new SaveSlotInfo
        {
            SlotId = slotId,
            Label = label,
            SeasonYear = seasonYear,
            Round = round,
            CreatedUtc = createdUtc,
            IsAutosave = isAutosave,
        };
        File.WriteAllText(
            Path.Combine(savesDir, slotId + MetadataExtension),
            JsonSerializer.Serialize(info, CoreJson.Options));
        return info;
    }

    /// <summary>Every restorable slot, NEWEST FIRST. A snapshot with readable metadata lists
    /// normally; a snapshot whose sidecar is corrupt or missing lists as a DEGRADED entry (label
    /// from the file stem, timestamp from the file clock) — restorable data is surfaced, never
    /// silently hidden. A sidecar with no surviving snapshot is stale and is not offered.</summary>
    public static IReadOnlyList<SaveSlotInfo> List(string careerFilePath)
    {
        string savesDir = SavesDirectoryFor(careerFilePath);
        if (!Directory.Exists(savesDir))
            return [];

        var slots = new List<SaveSlotInfo>();
        var described = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string metaPath in Directory.EnumerateFiles(savesDir, "*" + MetadataExtension))
        {
            SaveSlotInfo? info;
            try
            {
                info = JsonSerializer.Deserialize<SaveSlotInfo>(File.ReadAllText(metaPath), CoreJson.Options);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                continue;
            }
            if (info is null)
                continue;
            // A metadata sidecar with no surviving snapshot is stale — do not offer it.
            if (File.Exists(Path.Combine(savesDir, info.SlotId + SnapshotExtension)))
            {
                slots.Add(info);
                described.Add(info.SlotId);
            }
        }

        // Orphaned snapshots (damaged/missing sidecar) are still complete career files that
        // Restore can use — list them as degraded instead of hiding a restorable save.
        foreach (string snapshotPath in Directory.EnumerateFiles(savesDir, "*" + SnapshotExtension))
        {
            string slotId = Path.GetFileNameWithoutExtension(snapshotPath);
            if (described.Contains(slotId))
                continue;
            string createdUtc;
            try
            {
                createdUtc = File.GetLastWriteTimeUtc(snapshotPath).ToString(
                    "yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                createdUtc = "";
            }
            slots.Add(new SaveSlotInfo
            {
                SlotId = slotId,
                Label = $"{slotId} (recovered — details unreadable)",
                SeasonYear = 0,
                Round = 0,
                CreatedUtc = createdUtc,
                IsAutosave = slotId.StartsWith("autosave", StringComparison.OrdinalIgnoreCase),
                IsDegraded = true,
            });
        }

        return slots
            .OrderByDescending(s => s.CreatedUtc, StringComparer.Ordinal)
            .ThenBy(s => s.SlotId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Restores the career WHOLESALE to a snapshot — copies the slot's DB over the working file and
    /// clears the working file's stale WAL/SHM siblings so the next open reads the restored state
    /// cleanly. The caller MUST have closed the working DB first (disposed the session) so the file is
    /// replaceable; after this the working file IS the snapshot and should be reopened.
    /// </summary>
    public static void Restore(string careerFilePath, string slotId)
    {
        ArgumentException.ThrowIfNullOrEmpty(slotId);
        string savesDir = SavesDirectoryFor(careerFilePath);
        string snapshotPath = Path.Combine(savesDir, slotId + SnapshotExtension);
        if (!File.Exists(snapshotPath))
            throw new InvalidOperationException($"Save slot '{slotId}' has no snapshot to restore.");

        // Drop the (closed) working DB's stale WAL/SHM FIRST, so no leftover WAL frame can be recovered
        // onto the restored file. Then copy the snapshot to a temp sibling and swap it in with a single
        // move — minimizing the window in which the working file is only partially written.
        DeleteFileIfExists(careerFilePath + "-wal");
        DeleteFileIfExists(careerFilePath + "-shm");
        string restoringPath = careerFilePath + ".restoring";
        DeleteFileIfExists(restoringPath);
        File.Copy(snapshotPath, restoringPath, overwrite: true);
        File.Move(restoringPath, careerFilePath, overwrite: true);
    }

    /// <summary>True when the given slot has a restorable snapshot on disk — checked before a
    /// destructive restore so a bad/unknown slot id fails without disturbing the live career.</summary>
    public static bool SnapshotExists(string careerFilePath, string slotId) =>
        File.Exists(Path.Combine(SavesDirectoryFor(careerFilePath), slotId + SnapshotExtension));

    /// <summary>Deletes one slot (its snapshot + metadata). A no-op for an unknown slot.</summary>
    public static void Delete(string careerFilePath, string slotId)
    {
        ArgumentException.ThrowIfNullOrEmpty(slotId);
        string savesDir = SavesDirectoryFor(careerFilePath);
        DeleteFileAndWalSiblings(Path.Combine(savesDir, slotId + SnapshotExtension));
        DeleteFileIfExists(Path.Combine(savesDir, slotId + MetadataExtension));
    }

    /// <summary>Deletes EVERY snapshot — this career's whole Saves folder — while leaving the career
    /// file itself intact. (<see cref="DeleteCareerAndAllSaves"/> additionally deletes the career file.)</summary>
    public static void DeleteAllSaves(string careerFilePath)
    {
        string savesDir = SavesDirectoryFor(careerFilePath);
        if (Directory.Exists(savesDir))
            Directory.Delete(savesDir, recursive: true);
    }

    /// <summary>
    /// The Hardcore DEATH file op (docs/dev/character-death-injury.md §2/§4): physically DELETE the
    /// career file (and its WAL/SHM siblings) AND every one of its snapshots — the career is gone for
    /// good. Genuinely irreversible: the CALLER must gate it on a real folded death in a
    /// <see cref="Companion.Core.Career.MortalityMode.Hardcore"/> career.
    ///
    /// ⚠ STUB WIRING (Slice 1): death does not exist yet, so nothing calls this. Slice 3 hooks it to
    /// the <c>Deceased</c> fold transition. Kept here — tested — so the destructive path is written
    /// once and reviewed in isolation before it is ever wired.
    /// </summary>
    public static void DeleteCareerAndAllSaves(string careerFilePath)
    {
        DeleteAllSaves(careerFilePath);
        DeleteFileAndWalSiblings(careerFilePath);
    }

    private static void DeleteFileAndWalSiblings(string path)
    {
        DeleteFileIfExists(path);
        DeleteFileIfExists(path + "-wal");
        DeleteFileIfExists(path + "-shm");
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
