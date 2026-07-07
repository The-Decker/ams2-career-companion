using Companion.Ams2.ContentLibrary;
using Companion.Ams2.Preflight;
using Companion.Core.Grid;

namespace Companion.Ams2.Skins;

/// <summary>What the player will actually SEE on a car this round — the resolved end of the
/// AMS2 skin chain (driver → custom-AI <c>livery_name</c> → exact-match livery NAME → slot →
/// .dds texture). The status is entirely determined by whether the seat's livery NAME resolves
/// to an installed skin override, a stock livery, a name the installed NAMeS file declares, or
/// nothing at all — the same correlation the preflight validator makes, surfaced as a legible
/// per-car picture instead of buried in warning lines.</summary>
public enum SkinStatus
{
    /// <summary>A scanned <c>LIVERY_OVERRIDE</c> supplies a custom skin for this livery NAME —
    /// the car shows the community skin.</summary>
    CustomSkin,

    /// <summary>The NAME matches a known stock livery — the car shows the game's default livery
    /// for that slot (no custom skin installed, but it still looks right).</summary>
    StockDefault,

    /// <summary>The installed NAMeS/AI file declares this NAME (so the DRIVER binds in-game), but
    /// no matching skin was scanned — the car shows a default skin until the pack's skins are
    /// installed. Normal, never a failure.</summary>
    NameOnly,

    /// <summary>The NAME matches nothing installed (no override, no stock, no NAMeS entry) — the
    /// entry will not bind and the car falls back to a generic skin/driver. The one status worth
    /// a warning.</summary>
    Unbound,
}

/// <summary>One car's resolved skin picture for a round: who drives it, the livery NAME the
/// custom-AI file binds, what skin the player will see, and — for the player's own car — the
/// fact they must pick this livery themselves on AMS2's vehicle-selection screen (the player's
/// car has no custom-AI entry; its livery is a manual in-game pick).</summary>
public sealed record SkinAssignment
{
    public required string DriverName { get; init; }
    public required string TeamName { get; init; }
    public string? Number { get; init; }

    /// <summary>The exact livery display NAME (case-sensitive) — the join key the whole chain
    /// hangs off: the custom-AI <c>livery_name</c>, the <c>LIVERY_OVERRIDE NAME</c>, and the
    /// string shown on the in-game vehicle-selection screen.</summary>
    public required string LiveryName { get; init; }

    public required bool IsPlayer { get; init; }

    public required SkinStatus Status { get; init; }

    /// <summary>The Overrides vehicle folder the matched skin lives under (e.g.
    /// <c>brabham_bt26</c>), when <see cref="Status"/> is <see cref="SkinStatus.CustomSkin"/>;
    /// null otherwise.</summary>
    public string? VehicleFolder { get; init; }

    /// <summary>For an <see cref="SkinStatus.Unbound"/> seat only: the closest installed/known
    /// livery NAME that differs only by case or surrounding whitespace, when one exists — the
    /// skin-doctor hint that turns "won't bind" into "you meant this, the casing differs". Null
    /// when the name resolves cleanly or has no near match. (A mismatch is a hard defect: the
    /// game fails to display the custom driver for the class, it does NOT quietly fall back.)</summary>
    public string? NearMiss { get; init; }
}

/// <summary>The whole round's skin picture: one <see cref="SkinAssignment"/> per grid seat plus
/// roll-up counts and the player's own-car assignment surfaced for the "pick this livery in-game"
/// guidance. A pure read-only projection — it reads the resolved grid + the installed livery scan
/// and never writes anything, so it is safe to compute for any round at any time.</summary>
public sealed record SkinAssignmentPlan
{
    public static readonly SkinAssignmentPlan Empty = new()
    {
        Ams2Class = "",
        Assignments = [],
    };

    public required string Ams2Class { get; init; }

    public required IReadOnlyList<SkinAssignment> Assignments { get; init; }

    /// <summary>The player's own car this round, or null when the player has no seat (an all-AI
    /// round). The view uses it to tell the player exactly which livery to select in-game.</summary>
    public SkinAssignment? PlayerCar => Assignments.FirstOrDefault(a => a.IsPlayer);

    public int CustomSkinCount => Assignments.Count(a => a.Status == SkinStatus.CustomSkin);
    public int DefaultSkinCount =>
        Assignments.Count(a => a.Status is SkinStatus.StockDefault or SkinStatus.NameOnly);
    public int UnboundCount => Assignments.Count(a => a.Status == SkinStatus.Unbound);

    public bool IsEmpty => Assignments.Count == 0;

    /// <summary>One glanceable summary line for the panel header.</summary>
    public string Summary =>
        IsEmpty
            ? "No cars to resolve."
            : $"{CustomSkinCount} custom skin{(CustomSkinCount == 1 ? "" : "s")}, " +
              $"{DefaultSkinCount} default" +
              (UnboundCount > 0 ? $", {UnboundCount} unbound" : "") +
              $" of {Assignments.Count} cars.";
}

/// <summary>
/// Resolves what skin every car on a round's grid will show, correlating the resolved
/// <see cref="GridPlan"/> against the installed skin overrides (<see cref="InstalledLivery"/>),
/// the installed NAMeS file's declared names, and the stock livery library — the same ground
/// truth <see cref="Companion.Ams2.Packs.PackContentValidator"/> checks, projected into a
/// per-car picture for the briefing's Skins panel. Pure and read-only.
/// </summary>
public static class SkinAssignmentResolver
{
    public static SkinAssignmentPlan Resolve(
        GridPlan plan,
        IReadOnlyCollection<InstalledLivery> installedLiveries,
        Ams2ContentLibrary library,
        InstalledAiNameSet? installedAiNames = null)
    {
        // The vehicle folders this class's skins live under (a class can span several car
        // models). A scanned livery whose folder is one of these is unambiguously THIS class's
        // skin; used to disambiguate an identically-named livery on a different car.
        var classFolders = library.Vehicles.Values
            .Where(v => string.Equals(v.VehicleClass, plan.Ams2Class, StringComparison.Ordinal))
            .Select(v => v.Dir)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // NAME → the best installed override for it: prefer one in this class's folders, else any.
        var overrideByName = new Dictionary<string, InstalledLivery>(StringComparer.Ordinal);
        foreach (var livery in installedLiveries)
        {
            bool inClass = classFolders.Contains(livery.VehicleFolder);
            if (!overrideByName.TryGetValue(livery.Name, out var existing))
                overrideByName[livery.Name] = livery;
            else if (inClass && !classFolders.Contains(existing.VehicleFolder))
                overrideByName[livery.Name] = livery; // upgrade to the in-class match
        }

        var aiNames = installedAiNames is { LiveryNames.Count: > 0 }
            ? installedAiNames.LiveryNames.ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        var stock = library.Liveries.TryGetValue(plan.Ams2Class, out var entry)
            ? entry.StockLib1563.ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        // Every name that WOULD bind in-game — the near-miss search space for an unbound seat.
        var known = new HashSet<string>(overrideByName.Keys, StringComparer.Ordinal);
        known.UnionWith(stock);
        known.UnionWith(aiNames);

        var assignments = plan.Seats.Select(seat =>
        {
            string name = seat.Ams2LiveryName;

            SkinStatus status;
            string? folder = null;
            string? nearMiss = null;

            if (overrideByName.TryGetValue(name, out var skin))
            {
                status = SkinStatus.CustomSkin;
                folder = skin.VehicleFolder;
            }
            else if (stock.Contains(name))
            {
                status = SkinStatus.StockDefault;
            }
            else if (aiNames.Contains(name))
            {
                status = SkinStatus.NameOnly;
            }
            else
            {
                status = SkinStatus.Unbound;
                // Skin-doctor: a case/whitespace-only difference is the common authoring/typo
                // cause — surface the intended name so the fix is obvious.
                nearMiss = known.FirstOrDefault(k =>
                    string.Equals(k, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(k.Trim(), name.Trim(), StringComparison.Ordinal));
            }

            return new SkinAssignment
            {
                DriverName = seat.DriverName,
                TeamName = seat.TeamName,
                Number = seat.Number,
                LiveryName = name,
                IsPlayer = seat.IsPlayer,
                Status = status,
                VehicleFolder = folder,
                NearMiss = nearMiss,
            };
        }).ToList();

        return new SkinAssignmentPlan
        {
            Ams2Class = plan.Ams2Class,
            Assignments = assignments,
        };
    }
}
