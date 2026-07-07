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
    public static CustomAiFile RebindToBaseGame(
        CustomAiFile file, IReadOnlyList<OfficialLivery>? officialLiveries)
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

        int next = 0;
        var drivers = new List<CustomAiDriver>(file.Drivers.Count);
        foreach (var driver in file.Drivers)
        {
            if (driver.Tracks.Count == 0 && next < names.Count)
                drivers.Add(driver with { LiveryName = names[next++] });
            else
                drivers.Add(driver);
        }
        return file with { Drivers = drivers };
    }

    /// <summary>Rebinds using the class's official liveries from the content library, or returns the
    /// file unchanged when the class is not in the dump (no ground truth to bind against).</summary>
    public static CustomAiFile RebindToBaseGame(CustomAiFile file, Ams2ContentLibrary library)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(library);
        return RebindToBaseGame(
            file,
            library.OfficialLiveries.TryGetValue(file.VehicleClass, out var liveries) ? liveries : null);
    }
}
