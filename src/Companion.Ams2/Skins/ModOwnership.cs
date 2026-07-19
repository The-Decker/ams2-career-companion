using System.Diagnostics;
using System.IO.Compression;
using Companion.Ams2.Scenarios;

namespace Companion.Ams2.Skins;

/// <summary>Per-model ownership state against the install.</summary>
public enum SkinModelOwnershipState
{
    /// <summary>Every owned payload folder is present on the install.</summary>
    Owned,

    /// <summary>The model's Overrides folder exists but owned payload folders are gone — the
    /// mod-manager strip signature (only the inert <c>_dist</c> template survives).</summary>
    PayloadMissing,

    /// <summary>The model's Overrides folder itself is gone.</summary>
    FolderMissing,
}

public sealed record SkinModelOwnershipStatus
{
    public required string Model { get; init; }
    public required SkinModelOwnershipState State { get; init; }

    /// <summary>Owned payload folders currently missing from the install (empty when Owned).</summary>
    public required IReadOnlyList<string> MissingFolders { get; init; }
    public string? Detail { get; init; }
}

/// <summary>Ownership health of one app-owned skin set against the install.</summary>
public sealed record SkinSetOwnershipStatus
{
    public required string Key { get; init; }
    public required IReadOnlyList<SkinModelOwnershipStatus> Models { get; init; }

    public bool IsHealthy => Models.Count > 0 && Models.All(m => m.State == SkinModelOwnershipState.Owned);

    public bool NeedsRepair => !IsHealthy;

    public int DegradedCount => Models.Count(m => m.State != SkinModelOwnershipState.Owned);

    public string Summary => IsHealthy
        ? $"The {Key} mod payload is intact."
        : $"{DegradedCount} of {Models.Count} {Key} model(s) lost payload (a mod-manager strip?).";
}

public sealed record SkinOwnershipRepairResult
{
    public required bool Success { get; init; }
    public required IReadOnlyList<string> Repaired { get; init; }
    public required IReadOnlyList<string> Skipped { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyList<string> Backups { get; init; }
    public required string Message { get; init; }
}

public sealed record SkinOwnershipCaptureResult
{
    public required bool Success { get; init; }
    public required IReadOnlyList<string> Captured { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// The mod-ownership service (Mike's direction after RCM stripped the SMGP + McLaren mods on
/// 2026-07-11: "reinstall + make the app OWN it"). The app keeps its own copy of the install-side
/// payload in a vault a mod manager cannot reach (<c>Documents\AMS2CareerCompanion\ModVault</c>),
/// detects the stripped state (<see cref="Inspect"/>), and re-lays it (<see cref="Repair"/>).
/// Pure file operations only: this never touches the career DB, the sim, or the oracle.
///
/// <para>Source precedence for a repair: the vault first (fast, always available once captured);
/// then the model's optional source archive (.zip natively, .rar/.7z via a 7-Zip CLI when
/// installed). The vault is seeded by <see cref="Capture"/> while the install is healthy, or by a
/// successful archive extraction.</para>
/// </summary>
public static class ModOwnership
{
    /// <summary>The app-owned vault root for a set: <c>&lt;documents&gt;\AMS2CareerCompanion\ModVault\&lt;setKey&gt;</c>.</summary>
    public static string VaultDirectoryFor(string documentsDirectory, string setKey) =>
        Path.Combine(documentsDirectory, "AMS2CareerCompanion", "ModVault", setKey);

    /// <summary>Inspects the set's owned payload against the install without touching anything.
    /// Null when the set carries no ownership manifest (inspect-only sets stay out of the feature).</summary>
    public static SkinSetOwnershipStatus? Inspect(SkinSeasonSet set, IReadOnlyList<string> overrideRoots)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (set.Ownership is not { } ownership)
            return null;

        var models = new List<SkinModelOwnershipStatus>();
        foreach (var (model, payload) in ownership.Payload.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            string? modelFolder = FindModelFolder(overrideRoots, model);
            if (modelFolder is null)
            {
                models.Add(new SkinModelOwnershipStatus
                {
                    Model = model,
                    State = SkinModelOwnershipState.FolderMissing,
                    MissingFolders = payload.Folders,
                    Detail = $"No Overrides\\{model} folder at any override root.",
                });
                continue;
            }

            var missing = payload.Folders
                .Where(folder => !FolderHasFiles(Path.Combine(modelFolder, folder)))
                .ToList();
            models.Add(new SkinModelOwnershipStatus
            {
                Model = model,
                State = missing.Count == 0
                    ? SkinModelOwnershipState.Owned
                    : SkinModelOwnershipState.PayloadMissing,
                MissingFolders = missing,
                Detail = missing.Count == 0
                    ? null
                    : $"Missing payload folder(s): {string.Join(", ", missing)}.",
            });
        }

        return new SkinSetOwnershipStatus { Key = set.Key, Models = models };
    }

    /// <summary>Adopts the install's CURRENT healthy payload into the app vault (the copy RCM can
    /// never touch). Missing payload is skipped with a note, never captured as good state.
    /// Overwrites vault content when the install's bytes differ, the vault mirrors the install's
    /// last healthy state.</summary>
    public static SkinOwnershipCaptureResult Capture(SkinSeasonSet set, IReadOnlyList<string> overrideRoots, string vaultRoot)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (set.Ownership is not { } ownership)
        {
            return new SkinOwnershipCaptureResult
            {
                Success = false,
                Captured = [],
                Errors = [$"The {set.Key} set carries no ownership manifest."],
                Message = $"Nothing to capture, the {set.Key} set is not app-owned.",
            };
        }

        var captured = new List<string>();
        var errors = new List<string>();
        foreach (var (model, payload) in ownership.Payload.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            string? modelFolder = FindModelFolder(overrideRoots, model);
            foreach (string folder in payload.Folders)
            {
                string source = modelFolder is null ? "" : Path.Combine(modelFolder, folder);
                if (modelFolder is null || !FolderHasFiles(source))
                {
                    errors.Add($"{model}\\{folder}: not on the install, nothing to capture.");
                    continue;
                }

                try
                {
                    int files = CopyTree(source, Path.Combine(vaultRoot, model, folder), backup: null, backups: null);
                    captured.Add($"{model}\\{folder} ({files} file(s))");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{model}\\{folder}: {ex.Message}");
                }
            }
        }

        return new SkinOwnershipCaptureResult
        {
            Success = errors.Count == 0,
            Captured = captured,
            Errors = errors,
            Message = errors.Count == 0
                ? $"Captured {captured.Count} payload folder(s) into the app vault, the mod files are safe now."
                : $"Captured {captured.Count} folder(s), {errors.Count} failed.",
        };
    }

    /// <summary>Re-lays stripped payload into the install: vault first, the model's source archive
    /// as the re-seed fallback (a successful extraction re-seeds the vault for next time), and the
    /// set's pointer XML is re-written when missing or different (backup-first, the AI-file
    /// staging contract). Pre-existing install files that DIFFER from the vault are backed up
    /// before replacement; identical files are left alone.</summary>
    public static SkinOwnershipRepairResult Repair(
        SkinSeasonSet set, IReadOnlyList<string> overrideRoots, string vaultRoot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (set.Ownership is not { } ownership)
        {
            return new SkinOwnershipRepairResult
            {
                Success = false,
                Repaired = [],
                Skipped = [],
                Errors = [$"The {set.Key} set carries no ownership manifest."],
                Backups = [],
                Message = $"Nothing to repair, the {set.Key} set is not app-owned.",
            };
        }
        if (overrideRoots.Count == 0)
        {
            return new SkinOwnershipRepairResult
            {
                Success = false,
                Repaired = [],
                Skipped = [],
                Errors = ["No AMS2 override root (no install found)."],
                Backups = [],
                Message = "Cannot repair, no AMS2 install was found.",
            };
        }

        var status = Inspect(set, overrideRoots)!;
        var repaired = new List<string>();
        var skipped = new List<string>();
        var errors = new List<string>();
        var backups = new List<string>();

        foreach (var (model, payload) in ownership.Payload.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            var modelStatus = status.Models.Single(m => string.Equals(m.Model, model, StringComparison.OrdinalIgnoreCase));
            // Repair target = the FIRST override root (the install-side root; the model folder is
            // created when the strip removed it entirely). Every model runs the reconcile, an
            // "owned" model can still hold corrupted bytes (the vault is the fidelity reference).
            string modelFolder = Path.Combine(overrideRoots[0], model);
            int restored = 0;
            foreach (string folder in payload.Folders)
            {
                string vaultFolder = Path.Combine(vaultRoot, model, folder);
                if (!FolderHasFiles(vaultFolder))
                {
                    // The vault cannot supply this folder. When the install is MISSING it, re-seed
                    // the vault from the model's source archive, then restore from it. When the
                    // install HAS it, there is simply nothing to reconcile (Capture is the adopt
                    // path that teaches the vault the install's current good state).
                    if (!modelStatus.MissingFolders.Contains(folder))
                        continue;

                    string? seedError = TrySeedVaultFromArchive(payload, model, folder, vaultFolder);
                    if (seedError is not null)
                    {
                        errors.Add($"{model}\\{folder}: {seedError}");
                        continue;
                    }
                }

                try
                {
                    // Restore missing folders and reconcile content fidelity in one pass: the
                    // whole point of the app owning a copy is that install bytes match the vault.
                    // Identical files skip; differing files are backed up before replacement (a
                    // hand-edit survives as a .bak; Capture is how it would go canonical).
                    restored += CopyTree(
                        vaultFolder, Path.Combine(modelFolder, folder), backup: now, backups: backups);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{model}\\{folder}: {ex.Message}");
                }
            }

            // The strip also removes the active pointer: re-lay the app-owned pointer XML when it
            // is missing or different (backup-first, the AI-file staging contract).
            if (set.ModelXml.TryGetValue(model, out string? pointerXml))
            {
                string pointerPath = Path.Combine(modelFolder, model + ".xml");
                try
                {
                    bool needsPointer = !File.Exists(pointerPath) ||
                        !SkinSeasonManager.SameContent(File.ReadAllText(pointerPath), pointerXml);
                    if (needsPointer)
                    {
                        Directory.CreateDirectory(modelFolder);
                        if (File.Exists(pointerPath))
                            backups.Add(ScenarioApplier.BackUp(pointerPath, now));
                        File.WriteAllText(pointerPath, pointerXml, new System.Text.UTF8Encoding(false));
                        restored++;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{model}: pointer rewrite failed, {ex.Message}");
                }
            }

            if (restored > 0)
                repaired.Add($"{model}: restored {restored} item(s)");
            else
                skipped.Add($"{model}: intact");
        }

        return new SkinOwnershipRepairResult
        {
            Success = errors.Count == 0,
            Repaired = repaired,
            Skipped = skipped,
            Errors = errors,
            Backups = backups,
            Message = (errors.Count, repaired.Count) switch
            {
                (0, 0) => $"The {set.Key} mod payload is already intact.",
                (0, _) => $"Repaired {repaired.Count} model(s) from the app vault, previous files backed up.",
                (_, 0) => $"Nothing could be repaired, {errors.Count} problem(s).",
                _ => $"Repaired {repaired.Count} model(s), {errors.Count} failed.",
            },
        };
    }

    // ---------- internals ----------

    private static string? FindModelFolder(IReadOnlyList<string> overrideRoots, string model) =>
        overrideRoots
            .Select(root => Path.Combine(root, model))
            .FirstOrDefault(Directory.Exists);

    private static bool FolderHasFiles(string path)
    {
        try
        {
            return Directory.Exists(path) && Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Copies a folder tree, returning the file count. Existing files with identical
    /// bytes are skipped; differing files are backed up first when <paramref name="backup"/> is
    /// set (the timestamped-backup contract) and then replaced.</summary>
    private static int CopyTree(
        string sourceDir, string targetDir, DateTimeOffset? backup, List<string>? backups)
    {
        int count = 0;
        foreach (string sourceFile in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, sourceFile);
            string targetFile = Path.Combine(targetDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

            if (File.Exists(targetFile))
            {
                if (SameBytes(sourceFile, targetFile))
                    continue;
                if (backup is { } when)
                    backups?.Add(ScenarioApplier.BackUp(targetFile, when));
            }

            File.Copy(sourceFile, targetFile, overwrite: true);
            count++;
        }

        return count;
    }

    private static bool SameBytes(string a, string b)
    {
        var ai = new FileInfo(a);
        var bi = new FileInfo(b);
        if (ai.Length != bi.Length)
            return false;
        return System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(a))
            .SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(b)));
    }

    /// <summary>Re-seeds the vault from the model's source archive: .zip natively, .rar/.7z via a
    /// 7-Zip CLI when one is installed. Returns null on success, else the reason it could not.</summary>
    private static string? TrySeedVaultFromArchive(
        SkinModelOwnership payload, string model, string folder, string vaultFolder)
    {
        if (payload.ArchivePath is null)
        {
            return "not in the app vault and no source archive is recorded for it. " +
                   "Run Capture while the install is healthy, or point ownership.json at the skin pack.";
        }
        if (!File.Exists(payload.ArchivePath))
        {
            return $"not in the app vault and the source archive is gone ({payload.ArchivePath}). " +
                   "Restore the archive or Capture from a healthy install.";
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), "companion-modseed-" + Guid.NewGuid().ToString("N"));
        try
        {
            string? extractError = ExtractOverridePayload(payload.ArchivePath, model, folder, tempRoot);
            return extractError ?? (FolderHasFiles(Path.Combine(tempRoot, model, folder))
                ? MoveTree(Path.Combine(tempRoot, model, folder), vaultFolder)
                : $"the archive holds no Overrides\\{model}\\{folder} payload.");
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); }
            catch (IOException) { }
        }

        string? MoveTree(string source, string target)
        {
            try
            {
                CopyTree(source, target, backup: null, backups: null);
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ex.Message;
            }
        }
    }

    /// <summary>Extracts every entry under <c>Overrides\&lt;model&gt;\&lt;folder&gt;\</c> from an
    /// archive into <paramref name="outDir"/> as <c>&lt;model&gt;\&lt;folder&gt;\...</c>. Native
    /// for .zip; .rar/.7z shell out to a 7-Zip CLI when found. Returns null on success.</summary>
    private static string? ExtractOverridePayload(string archivePath, string model, string folder, string outDir)
    {
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return ExtractZip(archivePath, model, folder, outDir);
        if (archivePath.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
            archivePath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractWith7Zip(archivePath, model, folder, outDir);
        }

        return $"unsupported archive type (need .zip, .rar or .7z): {Path.GetFileName(archivePath)}.";
    }

    private static string? ExtractZip(string archivePath, string model, string folder, string outDir)
    {
        try
        {
            string marker = $"/overrides/{model}/{folder}/";
            using var archive = ZipFile.OpenRead(archivePath);
            int written = 0;
            foreach (var entry in archive.Entries)
            {
                string normalized = entry.FullName.Replace('\\', '/');
                int at = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (at < 0 || entry.Length == 0)
                    continue;

                string tail = normalized[(at + marker.Length)..];
                if (tail.Length == 0)
                    continue;
                string target = Path.Combine(outDir, model, folder, tail.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
                written++;
            }

            return written > 0 ? null : $"no Overrides\\{model}\\{folder} entries in {Path.GetFileName(archivePath)}.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    private static string? ExtractWith7Zip(string archivePath, string model, string folder, string outDir)
    {
        string? sevenZip = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe"),
                "7z",
            }
            .FirstOrDefault(candidate =>
            {
                try
                {
                    if (candidate == "7z")
                    {
                        using var probe = Process.Start(new ProcessStartInfo(candidate, "i")
                        {
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        })!;
                        probe.WaitForExit(5000);
                        return true;
                    }

                    return File.Exists(candidate);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
                {
                    return false;
                }
            });
        if (sevenZip is null)
            return "a 7-Zip CLI is needed to read .rar/.7z archives and none was found.";

        // Extract the whole archive to a temp root, then keep only the wanted subtree. A filtered
        // include (-ir!) is version-sensitive; the full extract is one-shot and bounded by the pack.
        string tempExtract = Path.Combine(outDir, ".7z-tmp");
        try
        {
            using var process = Process.Start(new ProcessStartInfo(
                sevenZip, $"x \"{archivePath}\" -o\"{tempExtract}\" -y")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            process.WaitForExit(600_000);
            if (process.ExitCode != 0)
                return $"7-Zip exited {process.ExitCode} reading {Path.GetFileName(archivePath)}.";

            string marker = $"overrides{Path.DirectorySeparatorChar}{model}{Path.DirectorySeparatorChar}{folder}";
            string? source = Directory
                .EnumerateDirectories(tempExtract, marker, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (source is null)
                return $"no Overrides\\{model}\\{folder} entries in {Path.GetFileName(archivePath)}.";

            Directory.CreateDirectory(outDir);
            string target = Path.Combine(outDir, model, folder);
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
            Directory.Move(source, target);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return ex.Message;
        }
        finally
        {
            try { if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, recursive: true); }
            catch (IOException) { }
        }
    }
}
