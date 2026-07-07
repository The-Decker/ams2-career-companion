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

    // ---------- grid editor: cosmetic per-seat staging overrides ----------

    /// <summary>
    /// Applies the Skins grid editor's per-seat overrides to a built <see cref="CustomAiFile"/>:
    /// for each driver whose (original) <c>livery_name</c> is a key in <paramref name="overrides"/>,
    /// swaps in the custom driver name and/or rebinds the livery. The map is keyed by the seat's
    /// ORIGINAL livery, so it is applied AFTER the NAMeS-primary merge (which keys on the same
    /// original livery) — the player's explicit edit is the final authority over the installed
    /// community name. Null/empty overrides return the file unchanged (byte-identical), so a career
    /// with no edits stages exactly as before. Cosmetic only: this never touches the resolved grid
    /// the sim scores.
    /// </summary>
    public static CustomAiFile ApplyStagingOverrides(
        CustomAiFile file, IReadOnlyDictionary<string, SeatStagingOverride>? overrides)
    {
        if (overrides is null || overrides.Count == 0)
            return file;

        bool changed = false;
        var drivers = file.Drivers.Select(driver =>
        {
            if (!overrides.TryGetValue(driver.LiveryName, out var seat) || seat.IsEmpty)
                return driver;
            changed = true;
            return driver with
            {
                Name = seat.DriverName is { Length: > 0 } ? seat.DriverName : driver.Name,
                LiveryName = seat.LiveryName is { Length: > 0 } ? seat.LiveryName : driver.LiveryName,
            };
        }).ToList();

        // Nothing actually matched a seat — return the original file so a no-edit stage stays a
        // byte-identical no-op.
        return changed ? file with { Drivers = drivers } : file;
    }

    // ---------- NAMeS-primary merge ("found before overwritten") ----------

    /// <summary>
    /// Makes the INSTALLED AI file PRIMARY for every generated seat it already defines (Mike's
    /// requirement: the names/AI mod must be primary if found — "found before overwritten").
    /// For a seat whose <c>livery_name</c> has a BASE entry in <paramref name="installed"/>:
    /// the installed NAME, country and base AI ratings become the base, and only the
    /// career-specific DELTA the generated file carries (per-round aiOverrides, trackForm,
    /// career drift — everything the pinned pack layered on top of its own driver baseline) is
    /// applied over them. The delta is measured against <paramref name="packBaselineByLivery"/>
    /// (the pinned pack's own per-livery driver values, before round/career effects): when the
    /// generated value equals the pinned baseline (no career change) the installed value is kept
    /// verbatim — the user's curated name/ratings are never overwritten with stale pinned-pack
    /// values. Physics scalars and reliability are the team career layer and come from the
    /// generated seat. Seats the installed file does not define pass through unchanged, as does
    /// the whole file when <paramref name="installed"/> is null.
    /// </summary>
    public static CustomAiFile MergeInstalledPrimary(
        CustomAiFile generated,
        CommunityAiFile? installed,
        IReadOnlyDictionary<string, CustomAiDriver>? packBaselineByLivery = null)
    {
        // The merge is OPT-IN: it only runs when the caller supplies the pinned-pack baseline
        // (the delta reference). Without it there is no way to tell a genuine career/round
        // change from a stale pinned value, so the generated file is written verbatim — the
        // low-level dry-run / test path keeps its exact prior behaviour.
        if (installed is null || packBaselineByLivery is null)
            return generated;

        var installedByLivery = installed.BaseEntriesByLivery();
        if (installedByLivery.Count == 0)
            return generated;

        var merged = new List<CustomAiDriver>(generated.Drivers.Count);
        foreach (var seat in generated.Drivers)
        {
            if (installedByLivery.TryGetValue(seat.LiveryName, out var installedEntry))
            {
                packBaselineByLivery.TryGetValue(seat.LiveryName, out var baseline);
                merged.Add(MergeSeat(seat, installedEntry, baseline));
            }
            else
            {
                merged.Add(seat);
            }
        }

        return generated with { Drivers = merged };
    }

    /// <summary>Installed base entry is PRIMARY; the generated seat contributes only its
    /// career/round delta (vs <paramref name="baseline"/>) on the skill fields plus the team
    /// physics layer. Name/country: the installed file wins (it is the authority the user
    /// curates); the generated seat only fills a field the installed entry leaves empty.</summary>
    private static CustomAiDriver MergeSeat(
        CustomAiDriver generated, CustomAiDriver installed, CustomAiDriver? baseline) => new()
    {
        // Exact livery match is the merge key — keep it (ordinal-equal to installed's).
        LiveryName = generated.LiveryName,

        // The installed name/country are the user's curated authority; fall back to the
        // generated value only when the installed entry omits one.
        Name = installed.Name is { Length: > 0 } ? installed.Name : generated.Name,
        Country = string.IsNullOrEmpty(installed.Country) ? generated.Country : installed.Country,

        Tracks = generated.Tracks,

        // Skill fields: installed value + (generated - packBaseline). No career/round change
        // (generated == baseline) => the installed value survives untouched. Clamped to 0..1.
        RaceSkill = Skill(installed.RaceSkill, generated.RaceSkill, baseline?.RaceSkill),
        QualifyingSkill = Skill(installed.QualifyingSkill, generated.QualifyingSkill, baseline?.QualifyingSkill),
        Aggression = Skill(installed.Aggression, generated.Aggression, baseline?.Aggression),
        Defending = Skill(installed.Defending, generated.Defending, baseline?.Defending),
        Stamina = Skill(installed.Stamina, generated.Stamina, baseline?.Stamina),
        Consistency = Skill(installed.Consistency, generated.Consistency, baseline?.Consistency),
        StartReactions = Skill(installed.StartReactions, generated.StartReactions, baseline?.StartReactions),
        WetSkill = Skill(installed.WetSkill, generated.WetSkill, baseline?.WetSkill),
        TyreManagement = Skill(installed.TyreManagement, generated.TyreManagement, baseline?.TyreManagement),
        FuelManagement = Skill(installed.FuelManagement, generated.FuelManagement, baseline?.FuelManagement),
        BlueFlagConceding = Skill(installed.BlueFlagConceding, generated.BlueFlagConceding, baseline?.BlueFlagConceding),
        WeatherTyreChanges = Skill(installed.WeatherTyreChanges, generated.WeatherTyreChanges, baseline?.WeatherTyreChanges),
        AvoidanceOfMistakes = Skill(installed.AvoidanceOfMistakes, generated.AvoidanceOfMistakes, baseline?.AvoidanceOfMistakes),
        AvoidanceOfForcedMistakes = Skill(installed.AvoidanceOfForcedMistakes, generated.AvoidanceOfForcedMistakes, baseline?.AvoidanceOfForcedMistakes),

        // Reliability is a team career effect the generated seat carries; keep the installed
        // value when the generated seat did not author one.
        VehicleReliability = generated.VehicleReliability ?? installed.VehicleReliability,

        // Physics scalars + setup fields: the team career layer (generated), falling back to
        // the installed value when the generated seat is neutral/omitted.
        WeightScalar = generated.WeightScalar ?? installed.WeightScalar,
        PowerScalar = generated.PowerScalar ?? installed.PowerScalar,
        DragScalar = generated.DragScalar ?? installed.DragScalar,
        SetupDownforce = generated.SetupDownforce ?? installed.SetupDownforce,
        SetupDownforceRandomness = generated.SetupDownforceRandomness ?? installed.SetupDownforceRandomness,
    };

    /// <summary>installed + (generated - baseline), clamped to 0..1. When no career/round
    /// change is present (generated == baseline, or either is absent) the installed value is
    /// kept verbatim — never overwritten with a stale pinned-pack value.</summary>
    private static double? Skill(double? installed, double? generated, double? baseline)
    {
        // No baseline to diff against, or the generated round produced no career change:
        // the installed (primary) value stands.
        if (baseline is not { } b || generated is not { } g)
            return installed;

        double delta = g - b;
        if (Math.Abs(delta) <= CustomAiEquivalence.FloatTolerance)
            return installed;

        // A genuine career/round delta: apply it over the installed primary value. When the
        // installed entry omits the field, the generated value is the only signal we have.
        if (installed is not { } i)
            return generated;

        return Math.Clamp(i + delta, 0.0, 1.0);
    }

    // ---------- preflight ----------

    /// <summary>Delegates to <see cref="GridPreflight.Check"/>: class-name casing, livery
    /// bindings against the installed NAMeS/AI file (PRIMARY) then installed overrides + stock
    /// names, grid size vs the venue's AI cap.</summary>
    public static PreflightReport Preflight(
        CustomAiFile file,
        Ams2ContentLibrary library,
        IReadOnlyCollection<InstalledLivery> installedLiveries,
        string? trackId = null,
        int? gridSize = null,
        InstalledAiNameSet? installedAiNames = null) =>
        GridPreflight.Check(file, library, installedLiveries, trackId, gridSize, installedAiNames);

    // ---------- stage ----------

    /// <summary>
    /// Diff-aware staging (NAMeS-first, locked decision #7). BEFORE any write the currently
    /// installed class XML is lenient-parsed and, when <paramref name="packBaselineByLivery"/>
    /// is supplied, the installed file is made PRIMARY (<see cref="MergeInstalledPrimary"/>:
    /// installed name/ratings kept, only the career/round delta applied) — "found before
    /// overwritten". When every resulting seat already has an equivalent base entry
    /// (<see cref="CustomAiEquivalence"/>: ordinal names, floats within 1e-4, omitted scalar ==
    /// 1.0) the stage is a NO-OP — nothing written, nothing backed up, the user's installed
    /// file stays exactly as curated. Otherwise writes <c>&lt;VehicleClass&gt;.xml</c>, ALWAYS
    /// snapshotting any existing file first via <see cref="CustomAiBackup.BackupIfPresent"/>
    /// (never overwrite without backup). When the existing, genuinely-divergent file does not
    /// carry <see cref="GeneratedMarker"/> — i.e. it is the user's own file, not one we
    /// generated — the stage refuses unless <paramref name="force"/> is set.
    /// </summary>
    public static StageResult Stage(
        CustomAiFile file,
        string customAiDriversDirectory,
        DateTimeOffset now,
        bool force = false,
        IReadOnlyDictionary<string, CustomAiDriver>? packBaselineByLivery = null,
        IReadOnlyDictionary<string, SeatStagingOverride>? overrides = null,
        bool alwaysWrite = false)
    {
        var result = StageOrRefuse(file, customAiDriversDirectory, now, force, packBaselineByLivery, overrides, alwaysWrite);
        return result.RequiresForce
            ? throw new InvalidOperationException(result.Report)
            : result;
    }

    /// <summary>The non-throwing shape of <see cref="Stage"/>: a force-gate refusal comes
    /// back as <see cref="StageResult.RequiresForce"/> instead of an exception, because for
    /// a curated community file the gate is an EXPECTED state the UI explains calmly, not a
    /// failure. Every other behavior (NAMeS-primary merge, diff-aware no-op, backup-first
    /// write) is identical.</summary>
    public static StageResult StageOrRefuse(
        CustomAiFile file,
        string customAiDriversDirectory,
        DateTimeOffset now,
        bool force = false,
        IReadOnlyDictionary<string, CustomAiDriver>? packBaselineByLivery = null,
        IReadOnlyDictionary<string, SeatStagingOverride>? overrides = null,
        bool alwaysWrite = false)
    {
        string target = Path.Combine(customAiDriversDirectory, file.VehicleClass + ".xml");

        // Read the installed file ONCE, before any write — the authority that must be "found
        // before overwritten". Unreadable simply means "no installed file to defer to".
        var installed = File.Exists(target) ? CommunityAiReader.TryReadFile(target) : null;

        // NAMeS-primary merge: for every seat the FOREIGN installed file (the user's own
        // community NAMeS/AI file — NOT one we generated) already defines, keep its name + base
        // ratings and apply only this round's / the career's delta on top. We never merge over
        // our OWN prior output: re-staging is handled by the diff-aware no-op below, and merging
        // a delta onto an already-round-final file would double-count it.
        bool installedIsForeign = File.Exists(target) && !LooksGenerated(target);
        var toWrite = installedIsForeign
            ? MergeInstalledPrimary(file, installed, packBaselineByLivery)
            : file;

        // The grid editor's per-seat cosmetic overrides are applied LAST — after the NAMeS-primary
        // merge — so the player's explicit rename/rebind wins over the installed community value.
        toWrite = ApplyStagingOverrides(toWrite, overrides);

        // Diff-aware no-op: when the merged file matches the installed file, nothing is
        // written — the user's curated file stays in place. SKIPPED for an explicit "apply this
        // grid" (alwaysWrite): the user chose this grid, so we ALWAYS write an app-marked file
        // (backup-first) even when the content is diff-equal, so the write is verifiable on disk
        // (the AMS2-diagnosis "why nothing changes": in the default flow the app wrote 0 bytes).
        if (!alwaysWrite && installed is not null && CustomAiEquivalence.Compare(toWrite, installed).Matches)
        {
            return new StageResult
            {
                BackupPath = null,
                WrittenPath = target,
                NoOpAlreadyMatches = true,
                Report =
                    $"Installed {file.VehicleClass}.xml already matches this round's grid " +
                    $"({toWrite.Drivers.Count} drivers) — your installed names/AI are kept; nothing written.",
            };
        }

        // The community-file gate is bypassed by an explicit apply (alwaysWrite implies the user
        // confirmed) — a timestamped backup is still taken first.
        if (!force && !alwaysWrite && File.Exists(target) && !LooksGenerated(target))
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

        CustomAiXmlWriter.WriteToDirectory(toWrite, customAiDriversDirectory);

        string report =
            $"Staged {toWrite.VehicleClass}.xml ({toWrite.Drivers.Count} drivers) to '{target}'. " +
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
