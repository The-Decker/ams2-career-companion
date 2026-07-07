using Companion.Ams2.ContentLibrary;
using Companion.Ams2.CustomAi;

namespace Companion.Ams2.Grid;

/// <summary>
/// Rebinds a staged custom-AI file's drivers onto real BASE-GAME liveries for the class (from
/// <c>official-liveries.json</c>) so the file is GUARANTEED to load in AMS2. Every <c>livery_name</c>
/// becomes a name the game actually ships, so AMS2 accepts the file and shows the real driver names
/// + ratings on the grid — instead of silently rejecting a file that references community skins the
/// player has not installed (the root cause of "nothing shows in game", confirmed on Mike's install).
///
/// Purely a STAGING transform: it changes which livery each AI driver is painted as, never the
/// resolved grid the career sim scores. Each base entry (no per-track scoping) gets a DISTINCT
/// base-game livery in grid order, up to the number the class ships; leftover drivers keep their
/// original livery (two drivers cannot share one livery cleanly). Confirmed in-game: an
/// F-Classic_Gen2 1988 grid bound this way loads and shows the real drivers.
/// </summary>
public static class BaseGameLiveryBinder
{
    /// <param name="installedActiveLiveryNames">The exact display names of community skins that are
    /// INSTALLED and ACTIVE on disk for this class (a real numeric slot, not a "##" placeholder). A
    /// driver already painted as one of these keeps that livery — the player installed the real skin,
    /// so AMS2 shows the historical paint. Every other base driver is floored onto a base-game livery
    /// the game always ships (guaranteed load). Null/empty = the original behavior: floor everyone.</param>
    public static CustomAiFile RebindToBaseGame(
        CustomAiFile file,
        IReadOnlyList<OfficialLivery>? officialLiveries,
        IReadOnlySet<string>? installedActiveLiveryNames = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (officialLiveries is not { Count: > 0 })
            return file;

        var names = officialLiveries
            .Select(l => l.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (names.Count == 0)
            return file;

        var installed = installedActiveLiveryNames ?? (IReadOnlySet<string>)EmptyNames;
        // Base-game names already consumed by a fall-back seat (or already occupied by a kept
        // community livery of the same string) must not be handed out twice.
        var used = new HashSet<string>(StringComparer.Ordinal);
        int next = 0;
        var drivers = new List<CustomAiDriver>(file.Drivers.Count);
        foreach (var driver in file.Drivers)
        {
            if (driver.Tracks.Count != 0)
            {
                drivers.Add(driver);                       // per-track entry — never touched
                continue;
            }
            if (installed.Contains(driver.LiveryName))
            {
                drivers.Add(driver);                       // real community skin installed → keep the paint
                used.Add(driver.LiveryName);
                continue;
            }
            while (next < names.Count && used.Contains(names[next]))
                next++;
            if (next < names.Count)
            {
                drivers.Add(driver with { LiveryName = names[next] });
                used.Add(names[next]);
                next++;
            }
            else
            {
                drivers.Add(driver);                       // pool exhausted (field > livery count) → as-is
            }
        }
        return file with { Drivers = drivers };
    }

    private static readonly HashSet<string> EmptyNames = new(StringComparer.Ordinal);

    /// <summary>Rebinds using the class's official liveries from the content library, or returns the
    /// file unchanged when the class is not in the dump (no ground truth to bind against). Pass the
    /// installed &amp; active community livery names to keep the player's real installed skins.</summary>
    public static CustomAiFile RebindToBaseGame(
        CustomAiFile file, Ams2ContentLibrary library,
        IReadOnlySet<string>? installedActiveLiveryNames = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(library);
        return RebindToBaseGame(
            file,
            library.OfficialLiveries.TryGetValue(file.VehicleClass, out var liveries) ? liveries : null,
            installedActiveLiveryNames);
    }
}
