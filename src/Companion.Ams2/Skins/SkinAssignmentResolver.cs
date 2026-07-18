using System.Globalization;
using Companion.Ams2.ContentLibrary;
using Companion.Ams2.Preflight;
using Companion.Core.Grid;

namespace Companion.Ams2.Skins;

/// <summary>What the player will actually SEE on a car this round, the resolved end of the
/// AMS2 skin chain (driver → custom-AI <c>livery_name</c> → exact-match livery NAME → slot →
/// .dds texture). The status is entirely determined by whether the seat's livery NAME resolves
/// to an installed skin override, a stock livery, a name the installed NAMeS file declares, or
/// nothing at all, the same correlation the preflight validator makes, surfaced as a legible
/// per-car picture instead of buried in warning lines.</summary>
public enum SkinStatus
{
    /// <summary>A scanned <c>LIVERY_OVERRIDE</c> with a real numeric slot supplies a custom skin
    /// for this livery NAME, the car shows the community skin in-game.</summary>
    CustomSkin,

    /// <summary>A skin override for this NAME is installed on disk but its slot is a "##"
    /// placeholder, the livery is NOT switched on in-game (a selector never assigned it a real
    /// slot), so the car falls back to a default skin even though the .dds files exist. This is
    /// the "the app lists it but AMS2 doesn't show it" case, fixable by activating the livery.</summary>
    InstalledInactive,

    /// <summary>The NAME matches a known stock livery, the car shows the game's default livery
    /// for that slot (no custom skin installed, but it still looks right).</summary>
    StockDefault,

    /// <summary>The installed NAMeS/AI file declares this NAME (so the DRIVER binds in-game), but
    /// no matching skin was scanned, the car shows a default skin until the pack's skins are
    /// installed. Normal, never a failure.</summary>
    NameOnly,

    /// <summary>The NAME matches nothing installed (no override, no stock, no NAMeS entry), the
    /// entry will not bind and the car falls back to a generic skin/driver. The one status worth
    /// a warning.</summary>
    Unbound,
}

/// <summary>One car's resolved skin picture for a round: who drives it, the livery NAME the
/// custom-AI file binds, what skin the player will see, and, for the player's own car, the
/// fact they must pick this livery themselves on AMS2's vehicle-selection screen (the player's
/// car has no custom-AI entry; its livery is a manual in-game pick).</summary>
public sealed record SkinAssignment
{
    /// <summary>The resolved grid seat's stable driver id. This is identity, not display text.</summary>
    public string DriverId { get; init; } = "";

    public required string DriverName { get; init; }

    /// <summary>The resolved grid seat's stable team id. This is identity, not display text.</summary>
    public string TeamId { get; init; } = "";

    public required string TeamName { get; init; }
    public string? Number { get; init; }

    /// <summary>The exact livery display NAME (case-sensitive), the join key the whole chain
    /// hangs off: the custom-AI <c>livery_name</c>, the <c>LIVERY_OVERRIDE NAME</c>, and the
    /// string shown on the in-game vehicle-selection screen.</summary>
    public required string LiveryName { get; init; }

    public required bool IsPlayer { get; init; }

    public required SkinStatus Status { get; init; }

    /// <summary>The real in-game livery slot: the raw numeric custom-override slot, or the
    /// official stock slot when the content library knows it. Empty when inactive or unknown.</summary>
    public string SkinSlot { get; init; } = "";

    /// <summary>The Overrides vehicle folder the matched skin lives under (e.g.
    /// <c>brabham_bt26</c>), when <see cref="Status"/> is <see cref="SkinStatus.CustomSkin"/>;
    /// null otherwise.</summary>
    public string? VehicleFolder { get; init; }

    /// <summary>For an <see cref="SkinStatus.Unbound"/> seat only: the closest installed/known
    /// livery NAME that differs only by case or surrounding whitespace, when one exists, the
    /// skin-doctor hint that turns "won't bind" into "you meant this, the casing differs". Null
    /// when the name resolves cleanly or has no near match. (A mismatch is a hard defect: the
    /// game fails to display the custom driver for the class, it does NOT quietly fall back.)</summary>
    public string? NearMiss { get; init; }
}

/// <summary>The whole round's skin picture: one <see cref="SkinAssignment"/> per grid seat plus
/// roll-up counts and the player's own-car assignment surfaced for the "pick this livery in-game"
/// guidance. A pure read-only projection, it reads the resolved grid + the installed livery scan
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

    /// <summary>The livery NAMEs that are ACTIVE in-game for this class (a real slot in an installed
    /// override, OR a stock name), sorted, the pool the grid editor's livery picker offers, and
    /// what an AI can actually be bound to. Empty on <see cref="Empty"/>.</summary>
    public IReadOnlyList<string> ActiveLiveries { get; init; } = [];

    /// <summary>Exact active livery NAME to real numeric in-game slot. Values are empty only for
    /// legacy stock names whose official slot is not present in the content library.</summary>
    public IReadOnlyDictionary<string, string> ActiveLiverySlots { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Active CUSTOM livery NAME to the exact vehicle-folder/car-model it belongs to.
    /// Stock liveries are deliberately absent: the replacement editor must never offer a vanilla
    /// skin, and a custom skin may only be rebound onto another seat using this same model.</summary>
    public IReadOnlyDictionary<string, string> ActiveCustomLiveryModels { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The livery NAMEs installed for this class as "##" placeholders but NOT switched on
    /// in-game (no real slot), the pool the livery activator can turn on. Sorted. Empty on
    /// <see cref="Empty"/>.</summary>
    public IReadOnlyList<string> InactiveLiveries { get; init; } = [];

    /// <summary>The class's livery-slot CAP (max distinct liveries the game can show), or null when
    /// unknown for this class. Custom slots run 51..(50+cap); the activator will not exceed it, and
    /// a grid needing more distinct liveries than the cap cannot show them all.</summary>
    public int? LiveryCap { get; init; }

    /// <summary>How many liveries are active in-game for this class (the "used" side of the budget).</summary>
    public int ActiveLiveryCount => ActiveLiveries.Count;

    /// <summary>How many DISTINCT liveries this round's grid asks for (one per unique livery NAME) —
    /// compared against <see cref="LiveryCap"/>, more than the cap means some cars must duplicate or
    /// show a default skin.</summary>
    public int DistinctLiveriesOnGrid =>
        Assignments.Select(a => a.LiveryName).Distinct(StringComparer.Ordinal).Count();

    /// <summary>True when this round's grid needs more distinct liveries than the class can show —
    /// the historical field is bigger than the livery cap, so it cannot be fully represented.</summary>
    public bool ExceedsCap => LiveryCap is { } cap && DistinctLiveriesOnGrid > cap;

    /// <summary>The player's own car this round, or null when the player has no seat (an all-AI
    /// round). The view uses it to tell the player exactly which livery to select in-game.</summary>
    public SkinAssignment? PlayerCar => Assignments.FirstOrDefault(a => a.IsPlayer);

    public int CustomSkinCount => Assignments.Count(a => a.Status == SkinStatus.CustomSkin);
    public int DefaultSkinCount =>
        Assignments.Count(a => a.Status is SkinStatus.StockDefault or SkinStatus.NameOnly);
    public int InactiveCount => Assignments.Count(a => a.Status == SkinStatus.InstalledInactive);
    public int UnboundCount => Assignments.Count(a => a.Status == SkinStatus.Unbound);

    public bool IsEmpty => Assignments.Count == 0;

    /// <summary>One glanceable summary line for the panel header.</summary>
    public string Summary =>
        IsEmpty
            ? "No cars to resolve."
            : $"{CustomSkinCount} custom skin{(CustomSkinCount == 1 ? "" : "s")}, " +
              $"{DefaultSkinCount} default" +
              (InactiveCount > 0 ? $", {InactiveCount} not active" : "") +
              (UnboundCount > 0 ? $", {UnboundCount} unbound" : "") +
              $" of {Assignments.Count} cars.";
}

/// <summary>
/// Resolves what skin every car on a round's grid will show, correlating the resolved
/// <see cref="GridPlan"/> against the installed skin overrides (<see cref="InstalledLivery"/>),
/// the installed NAMeS file's declared names, and the stock livery library, the same ground
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

        // NAME → the best installed override for it. Preference order: ACTIVE (real slot) beats a
        // "##" placeholder, and within the same active-ness an in-class folder beats an out-of-class
        // one, so a livery that IS switched on in-game always wins the lookup.
        var overrideByName = new Dictionary<string, InstalledLivery>(StringComparer.Ordinal);
        foreach (var livery in installedLiveries)
        {
            if (!overrideByName.TryGetValue(livery.Name, out var existing) ||
                OverrideScore(livery, classFolders) > OverrideScore(existing, classFolders))
                overrideByName[livery.Name] = livery;
        }

        var aiNames = installedAiNames is { LiveryNames.Count: > 0 }
            ? installedAiNames.LiveryNames.ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        var stock = library.Liveries.TryGetValue(plan.Ams2Class, out var entry)
            ? entry.StockLib1563.ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var official = library.OfficialLiveries.TryGetValue(plan.Ams2Class, out var officialLiveries)
            ? officialLiveries
            : [];
        var officialByName = official
            .GroupBy(livery => livery.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        stock.UnionWith(officialByName.Keys);

        // Every name that WOULD bind in-game, the near-miss search space for an unbound seat.
        var known = new HashSet<string>(overrideByName.Keys, StringComparer.Ordinal);
        known.UnionWith(stock);
        known.UnionWith(aiNames);

        var assignments = plan.Seats.Select(seat =>
        {
            string name = seat.Ams2LiveryName;

            SkinStatus status;
            string? folder = null;
            string? nearMiss = null;
            string skinSlot = "";

            if (overrideByName.TryGetValue(name, out var skin))
            {
                status = skin.IsActive ? SkinStatus.CustomSkin : SkinStatus.InstalledInactive;
                folder = skin.VehicleFolder;
                if (skin.IsActive)
                    skinSlot = skin.Slot;
            }
            else if (stock.Contains(name))
            {
                status = SkinStatus.StockDefault;
                if (officialByName.TryGetValue(name, out var stockLivery) && stockLivery.Slot > 0)
                    skinSlot = stockLivery.Slot.ToString(CultureInfo.InvariantCulture);
            }
            else if (aiNames.Contains(name))
            {
                status = SkinStatus.NameOnly;
            }
            else
            {
                status = SkinStatus.Unbound;
                // Skin-doctor: a case/whitespace-only difference is the common authoring/typo
                // cause, surface the intended name so the fix is obvious.
                nearMiss = known.FirstOrDefault(k =>
                    string.Equals(k, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(k.Trim(), name.Trim(), StringComparison.Ordinal));
            }

            return new SkinAssignment
            {
                DriverId = seat.DriverId,
                DriverName = seat.DriverName,
                TeamId = seat.TeamId,
                TeamName = seat.TeamName,
                Number = seat.Number,
                LiveryName = name,
                IsPlayer = seat.IsPlayer,
                Status = status,
                SkinSlot = skinSlot,
                VehicleFolder = folder,
                NearMiss = nearMiss,
            };
        }).ToList();

        // The livery pools the grid editor + activator draw from. ACTIVE = a real-slot override for
        // an in-class folder, plus stock names (both bind in-game). INACTIVE = a "##" placeholder for
        // an in-class folder that has no active entry, the activator's candidates.
        bool anyClassFolders = classFolders.Count > 0;
        var inClass = installedLiveries
            .Where(l => !anyClassFolders || classFolders.Contains(l.VehicleFolder))
            .ToList();
        var activeNames = new HashSet<string>(StringComparer.Ordinal);
        var activeSlots = new Dictionary<string, string>(StringComparer.Ordinal);
        var activeCustomModels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var l in inClass.Where(l => l.IsActive))
        {
            activeNames.Add(l.Name);
            activeSlots.TryAdd(l.Name, l.Slot);
            activeCustomModels.TryAdd(l.Name, l.VehicleFolder);
        }
        activeNames.UnionWith(stock);
        foreach (string name in stock)
        {
            string slot = officialByName.TryGetValue(name, out var stockLivery) && stockLivery.Slot > 0
                ? stockLivery.Slot.ToString(CultureInfo.InvariantCulture)
                : "";
            activeSlots.TryAdd(name, slot);
        }
        var inactiveNames = inClass
            .Where(l => !l.IsActive && !activeNames.Contains(l.Name))
            .Select(l => l.Name)
            .ToHashSet(StringComparer.Ordinal);

        int? liveryCap = library.LiveryCaps.TryGetValue(plan.Ams2Class, out int cap) ? cap : null;

        return new SkinAssignmentPlan
        {
            Ams2Class = plan.Ams2Class,
            Assignments = assignments,
            ActiveLiveries = activeNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
            ActiveLiverySlots = activeSlots,
            ActiveCustomLiveryModels = activeCustomModels,
            InactiveLiveries = inactiveNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
            LiveryCap = liveryCap,
        };
    }

    /// <summary>Ranks an override candidate for the NAME → best-override lookup: active beats
    /// placeholder, and within the same active-ness an in-class folder beats out-of-class. Higher
    /// wins.</summary>
    private static int OverrideScore(InstalledLivery livery, IReadOnlySet<string> classFolders)
    {
        int score = livery.IsActive ? 2 : 0;
        if (classFolders.Contains(livery.VehicleFolder))
            score += 1;
        return score;
    }
}
