using System.Text;

namespace Companion.Ams2.Skins;

/// <summary>The result of planning one model's override file for a round: the edited XML plus what
/// changed. <see cref="Changed"/> is false when the file already matches the round (an idempotent
/// re-stage writes nothing).</summary>
public sealed record RoundLiveryPlan
{
    public required string Xml { get; init; }
    public int Activated { get; init; }
    public int Deactivated { get; init; }

    /// <summary>Qualifier liveries that could NOT be switched on (the class livery cap was reached).
    /// The car then falls back to a base-game livery downstream, so it still loads.</summary>
    public IReadOnlyList<string> Skipped { get; init; } = [];

    public bool Changed => Activated > 0 || Deactivated > 0;
}

/// <summary>The result of applying a round across every model override file.</summary>
public sealed record RoundLiveryResult
{
    public int Activated { get; init; }
    public int Deactivated { get; init; }
    public int ModelsChanged { get; init; }
    public IReadOnlyList<string> Backups { get; init; } = [];
    public IReadOnlyList<string> Skipped { get; init; } = [];
    public bool AnyChanged => Activated > 0 || Deactivated > 0;
}

/// <summary>
/// Per-race skin activation for a rotating field (the SMGP replica: 34 painted cars, ≤26 racing per
/// round via pre-qualifying). At staging it makes the community override files show EXACTLY the
/// round's grid: every one of the round's qualifier liveries is switched ON (assigned a real numeric
/// slot), and every OTHER pack livery is parked OFF (a <c>##</c> placeholder). So when the smart
/// binder scans active liveries next, all 26 grid cars read active and keep their real SMGP paint —
/// instead of the six installed-but-inactive second cars being floored to base-game liveries (which
/// AMS2 then pool-fills with random stock drivers).
///
/// <para>Deactivations run FIRST (freeing slots), then activations fill the freed slots, so the file
/// never exceeds the class livery cap. Only the pack's OWN liveries are touched, base-game and other
/// packs' liveries in the same file are never moved. Every edit is the minimal in-place textual swap
/// <see cref="LiveryOverrideWriter"/> performs, backup-first, and idempotent (a re-stage of the same
/// round writes nothing).</para>
///
/// <para>NOTE: AMS2 reads the override files when it builds a session's car pool. If a car still shows
/// a wrong/random driver after staging, the game may need a full restart to reload the newly-activated
/// slots, the staging message says so.</para>
/// </summary>
public static class RoundLiveryActivator
{
    /// <summary>Plans one override file: switch on the round's qualifiers, park the pack's
    /// non-qualifiers. Pure (no I/O) so the activation logic is fully unit-tested. Only liveries whose
    /// NAME is in <paramref name="packLiveryNames"/> are managed; <paramref name="roundLiveryNames"/>
    /// is the round's grid (a subset of the pack). <paramref name="maxSlot"/> caps activation.</summary>
    public static RoundLiveryPlan PlanFile(
        string xml,
        IReadOnlySet<string> roundLiveryNames,
        IReadOnlySet<string> packLiveryNames,
        int? maxSlot)
    {
        var liveries = LiveryOverrideWriter.Liveries(xml);

        // Park every ACTIVE pack livery that is NOT in this round's grid, frees its slot first.
        var toDeactivate = liveries
            .Where(l => l.Active && packLiveryNames.Contains(l.Name) && !roundLiveryNames.Contains(l.Name))
            .Select(l => l.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Switch on every INACTIVE qualifier (round names are already a subset of the pack).
        var toActivate = liveries
            .Where(l => !l.Active && roundLiveryNames.Contains(l.Name))
            .Select(l => l.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        int deactivated = 0, activated = 0;
        var skipped = new List<string>();

        foreach (var name in toDeactivate)
        {
            string? edited = LiveryOverrideWriter.Deactivate(xml, name);
            if (edited is not null)
            {
                xml = edited;
                deactivated++;
            }
        }

        foreach (var name in toActivate)
        {
            int slot = LiveryOverrideWriter.NextFreeSlot(xml);
            if (maxSlot is { } max && slot > max)
            {
                skipped.Add(name); // class livery cap reached, the car floors to base-game downstream
                continue;
            }
            string? edited = LiveryOverrideWriter.Activate(xml, name, slot);
            if (edited is not null)
            {
                xml = edited;
                activated++;
            }
            else
            {
                skipped.Add(name); // no matching placeholder in this file (shouldn't happen, guard)
            }
        }

        return new RoundLiveryPlan { Xml = xml, Activated = activated, Deactivated = deactivated, Skipped = skipped };
    }

    /// <summary>Applies the round to every model override file under <paramref name="overridesRoot"/>
    /// (<c>&lt;dir&gt;/&lt;dir&gt;.xml</c>), backup-first, writing each changed file once. A missing or
    /// unreadable file is skipped (never blocks staging). Idempotent, files already matching the round
    /// are left untouched (no needless backups).</summary>
    public static RoundLiveryResult ApplyRound(
        string overridesRoot,
        IEnumerable<string> modelDirs,
        IReadOnlySet<string> roundLiveryNames,
        IReadOnlySet<string> packLiveryNames,
        int? maxSlot,
        DateTimeOffset now)
    {
        int activated = 0, deactivated = 0, modelsChanged = 0;
        var backups = new List<string>();
        var skipped = new List<string>();

        foreach (var dir in modelDirs)
        {
            string path = Path.Combine(overridesRoot, dir, dir + ".xml");
            if (!File.Exists(path))
                continue;

            string xml;
            try
            {
                xml = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var plan = PlanFile(xml, roundLiveryNames, packLiveryNames, maxSlot);
            skipped.AddRange(plan.Skipped);
            if (!plan.Changed)
                continue;

            try
            {
                backups.Add(LiveryOverrideWriter.Backup(path, now));
                File.WriteAllText(path, plan.Xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            activated += plan.Activated;
            deactivated += plan.Deactivated;
            modelsChanged++;
        }

        return new RoundLiveryResult
        {
            Activated = activated,
            Deactivated = deactivated,
            ModelsChanged = modelsChanged,
            Backups = backups,
            Skipped = skipped.Distinct(StringComparer.Ordinal).ToList(),
        };
    }
}
