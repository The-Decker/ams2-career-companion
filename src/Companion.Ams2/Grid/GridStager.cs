using Companion.Ams2.ContentLibrary;
using Companion.Ams2.CustomAi;
using Companion.Ams2.Preflight;
using Companion.Core.Grid;

namespace Companion.Ams2.Grid;

/// <summary>The outcome of staging one class XML into a CustomAIDrivers folder.</summary>
public sealed record StageResult
{
    /// <summary>Timestamped snapshot of whatever was at the target path before the write,
    /// or null when there was nothing to back up. NEVER null when a file was overwritten.</summary>
    public string? BackupPath { get; init; }

    public required string WrittenPath { get; init; }

    /// <summary>True when NOTHING was written because the installed file already matches the
    /// generated grid (NAMeS-first diff-aware staging): every seat's livery has a base entry
    /// with equivalent effective fields, so the user's file stays untouched — no backup either.
    /// <see cref="WrittenPath"/> then points at the installed file that satisfies the round.</summary>
    public bool NoOpAlreadyMatches { get; init; }

    /// <summary>True when NOTHING was written because the installed file is the user's own
    /// (no <see cref="GridStager.GeneratedMarker"/>) and diverges from the generated grid —
    /// staging over it is an explicit choice (<c>force</c>), not a failure.
    /// <see cref="WrittenPath"/> points at the protected file; <see cref="Report"/> explains
    /// the gate. Only <see cref="GridStager.StageOrRefuse"/> returns this state; the
    /// throwing <see cref="GridStager.Stage"/> wrapper converts it to an exception.</summary>
    public bool RequiresForce { get; init; }

    /// <summary>Human-readable summary (what was written, how many drivers, backup taken).</summary>
    public required string Report { get; init; }
}

/// <summary>
/// Turns a resolved <see cref="GridPlan"/> into an AMS2 custom-AI file and stages it. v1
/// regenerates the file before every round, so no per-track <c>tracks=</c> override entries
/// are emitted — the base entries already carry the round-final merged ratings.
/// </summary>
public static class GridStager
{
    /// <summary>Marker embedded in every generated file's header comment. Staging over a file
    /// WITHOUT this marker (the user's own community NAMeS file) requires force — a backup is
    /// taken either way, but clobbering curated files must be an explicit choice.</summary>
    public const string GeneratedMarker = "AMS2 Career Companion";

    // ---------- build ----------

    public static CustomAiFile Build(GridPlan plan, string? headerComment = null) => new()
    {
        VehicleClass = plan.Ams2Class,
        Drivers = plan.Seats.Select(ToCustomAiDriver).ToList(),
        HeaderComment = headerComment is { Length: > 0 }
            ? $" {GeneratedMarker} | {headerComment} "
            : $" {GeneratedMarker} ",
    };

    private static CustomAiDriver ToCustomAiDriver(GridSeat seat) => new()
    {
        LiveryName = seat.Ams2LiveryName,
        Name = seat.DriverName,
        Country = seat.Country,

        RaceSkill = seat.Ratings.RaceSkill,
        QualifyingSkill = seat.Ratings.QualifyingSkill,
        Aggression = seat.Ratings.Aggression,
        Defending = seat.Ratings.Defending,
        Stamina = seat.Ratings.Stamina,
        Consistency = seat.Ratings.Consistency,
        StartReactions = seat.Ratings.StartReactions,
        WetSkill = seat.Ratings.WetSkill,
        TyreManagement = seat.Ratings.TyreManagement,
        AvoidanceOfMistakes = seat.Ratings.AvoidanceOfMistakes,
        BlueFlagConceding = seat.Ratings.BlueFlagConceding,
        WeatherTyreChanges = seat.Ratings.WeatherTyreChanges,
        AvoidanceOfForcedMistakes = seat.Ratings.AvoidanceOfForcedMistakes,
        FuelManagement = seat.Ratings.FuelManagement,
        VehicleReliability = seat.Reliability,

        // Physics scalars are written only when they deviate from neutral: an entry-less field
        // means "stock car", and the scalars also affect the PLAYER when driving the livery.
        WeightScalar = ScalarOrNull(seat.WeightScalar),
        PowerScalar = ScalarOrNull(seat.PowerScalar),
        DragScalar = ScalarOrNull(seat.DragScalar),
    };

    private static double? ScalarOrNull(double scalar) => scalar == 1.0 ? null : scalar;

    // ---------- preflight ----------

    /// <summary>Delegates to <see cref="GridPreflight.Check"/>: class-name casing, livery
    /// bindings against installed overrides + stock names, grid size vs the venue's AI cap.</summary>
    public static PreflightReport Preflight(
        CustomAiFile file,
        Ams2ContentLibrary library,
        IReadOnlyCollection<InstalledLivery> installedLiveries,
        string? trackId = null,
        int? gridSize = null) =>
        GridPreflight.Check(file, library, installedLiveries, trackId, gridSize);

    // ---------- stage ----------

    /// <summary>
    /// Diff-aware staging (NAMeS-first, locked decision #7). BEFORE any write the currently
    /// installed class XML is lenient-parsed; when every generated seat already has an
    /// equivalent base entry (<see cref="CustomAiEquivalence"/>: ordinal names, floats within
    /// 1e-4, omitted scalar == 1.0) the stage is a NO-OP — nothing written, nothing backed up,
    /// the user's installed file stays exactly as curated. Otherwise writes
    /// <c>&lt;VehicleClass&gt;.xml</c>, ALWAYS snapshotting any existing file first via
    /// <see cref="CustomAiBackup.BackupIfPresent"/> (never overwrite without backup). When the
    /// existing, genuinely-divergent file does not carry <see cref="GeneratedMarker"/> — i.e.
    /// it is the user's own file, not one we generated — the stage refuses unless
    /// <paramref name="force"/> is set.
    /// </summary>
    public static StageResult Stage(
        CustomAiFile file,
        string customAiDriversDirectory,
        DateTimeOffset now,
        bool force = false)
    {
        var result = StageOrRefuse(file, customAiDriversDirectory, now, force);
        return result.RequiresForce
            ? throw new InvalidOperationException(result.Report)
            : result;
    }

    /// <summary>The non-throwing shape of <see cref="Stage"/>: a force-gate refusal comes
    /// back as <see cref="StageResult.RequiresForce"/> instead of an exception, because for
    /// a curated community file the gate is an EXPECTED state the UI explains calmly, not a
    /// failure. Every other behavior (diff-aware no-op, backup-first write) is identical.</summary>
    public static StageResult StageOrRefuse(
        CustomAiFile file,
        string customAiDriversDirectory,
        DateTimeOffset now,
        bool force = false)
    {
        string target = Path.Combine(customAiDriversDirectory, file.VehicleClass + ".xml");

        // Diff-aware no-op: an unreadable installed file simply means "no match" and falls
        // through to the normal (force-gated, backup-first) staging path.
        if (File.Exists(target) &&
            CommunityAiReader.TryReadFile(target) is { } installed &&
            CustomAiEquivalence.Compare(file, installed).Matches)
        {
            return new StageResult
            {
                BackupPath = null,
                WrittenPath = target,
                NoOpAlreadyMatches = true,
                Report =
                    $"Installed {file.VehicleClass}.xml already matches this round's grid " +
                    $"({file.Drivers.Count} drivers) — nothing written, your file stays in place.",
            };
        }

        if (!force && File.Exists(target) && !LooksGenerated(target))
        {
            return new StageResult
            {
                BackupPath = null,
                WrittenPath = target,
                RequiresForce = true,
                Report =
                    $"'{target}' exists and was not generated by this app (community NAMeS file?). " +
                    "Staging over it requires force — a timestamped backup is still taken first.",
            };
        }

        // Backup FIRST, before any write touches the target.
        string? backupPath = new CustomAiBackup(customAiDriversDirectory)
            .BackupIfPresent(file.VehicleClass, now);

        CustomAiXmlWriter.WriteToDirectory(file, customAiDriversDirectory);

        string report =
            $"Staged {file.VehicleClass}.xml ({file.Drivers.Count} drivers) to '{target}'. " +
            (backupPath is null
                ? "No existing file — nothing to back up."
                : $"Previous file backed up to '{backupPath}'.");

        return new StageResult
        {
            BackupPath = backupPath,
            WrittenPath = target,
            Report = report,
        };
    }

    /// <summary>Scratch write with no backup and no force gate — for dry-run output folders
    /// only, NEVER a live CustomAIDrivers directory. Returns the written path.</summary>
    public static string DryRun(CustomAiFile file, string targetDirectory)
    {
        CustomAiXmlWriter.WriteToDirectory(file, targetDirectory);
        return Path.Combine(targetDirectory, file.VehicleClass + ".xml");
    }

    /// <summary>True when the file at <paramref name="path"/> carries the app's
    /// <see cref="GeneratedMarker"/> header — i.e. this app wrote it and it is regenerable,
    /// as opposed to the user's own curated community file.</summary>
    public static bool LooksGenerated(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            var buffer = new char[4096];
            int read = reader.ReadBlock(buffer, 0, buffer.Length);
            return new string(buffer, 0, read).Contains(GeneratedMarker, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }
}
