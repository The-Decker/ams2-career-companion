using Companion.Ams2.CustomAi;
using Companion.Core.Packs;

namespace Companion.ViewModels.Services;

/// <summary>The imported drivers list plus the summary diff the wizard shows.</summary>
public sealed record BaselineImportResult
{
    /// <summary>The pack's drivers with the community file's values folded in — this is what
    /// gets serialized into the pinned drivers.json.</summary>
    public required IReadOnlyList<PackDriver> Drivers { get; init; }

    /// <summary>Drivers whose ratings/name/country came from the installed file.</summary>
    public required int ImportedDriverCount { get; init; }

    /// <summary>Drivers that kept the pack-authored values (no matching livery entry).</summary>
    public required int PackOnlyDriverCount { get; init; }

    public string Summary =>
        $"{ImportedDriverCount} drivers imported from your installed AI file, {PackOnlyDriverCount} pack-only.";
}

/// <summary>
/// NAMeS-first baseline import (locked decision #7a): the user's INSTALLED class XML is the
/// preferred season baseline, the pack's drivers.json values the fallback. Mapping is
/// per-livery: each entries.json <c>ams2LiveryName</c> is looked up among the community
/// file's BASE entries (track-scoped entries stay round-level and are never part of the
/// baseline); a hit overrides that driver's ratings, name and country field-by-field
/// (fields the community entry omits keep the pack value). The pack keeps everything else —
/// calendar, entries, teams, scoring; team-level reliability/scalars are deliberately NOT
/// imported (they are not drivers.json data).
/// </summary>
public static class CommunityBaselineImport
{
    public static BaselineImportResult Apply(SeasonPack pack, CommunityAiFile installed)
    {
        var byLivery = installed.BaseEntriesByLivery();

        // First entries.json hit wins per driver: a driver with multiple liveries
        // (mid-season swaps) imports from their earliest-listed entry.
        var overridesByDriverId = new Dictionary<string, CustomAiDriver>(StringComparer.Ordinal);
        foreach (var entry in pack.Entries)
        {
            if (byLivery.TryGetValue(entry.Ams2LiveryName, out var communityEntry))
                overridesByDriverId.TryAdd(entry.DriverId, communityEntry);
        }

        var drivers = new List<PackDriver>(pack.Drivers.Count);
        int imported = 0;
        foreach (var driver in pack.Drivers)
        {
            if (overridesByDriverId.TryGetValue(driver.Id, out var communityEntry))
            {
                drivers.Add(Merge(driver, communityEntry));
                imported++;
            }
            else
            {
                drivers.Add(driver);
            }
        }

        return new BaselineImportResult
        {
            Drivers = drivers,
            ImportedDriverCount = imported,
            PackOnlyDriverCount = pack.Drivers.Count - imported,
        };
    }

    private static PackDriver Merge(PackDriver driver, CustomAiDriver entry) => driver with
    {
        Name = entry.Name is { Length: > 0 } name ? name : driver.Name,
        Country = entry.Country ?? driver.Country,
        Ratings = driver.Ratings with
        {
            RaceSkill = entry.RaceSkill ?? driver.Ratings.RaceSkill,
            QualifyingSkill = entry.QualifyingSkill ?? driver.Ratings.QualifyingSkill,
            Aggression = entry.Aggression ?? driver.Ratings.Aggression,
            Defending = entry.Defending ?? driver.Ratings.Defending,
            Stamina = entry.Stamina ?? driver.Ratings.Stamina,
            Consistency = entry.Consistency ?? driver.Ratings.Consistency,
            StartReactions = entry.StartReactions ?? driver.Ratings.StartReactions,
            WetSkill = entry.WetSkill ?? driver.Ratings.WetSkill,
            TyreManagement = entry.TyreManagement ?? driver.Ratings.TyreManagement,
            AvoidanceOfMistakes = entry.AvoidanceOfMistakes ?? driver.Ratings.AvoidanceOfMistakes,
            BlueFlagConceding = entry.BlueFlagConceding ?? driver.Ratings.BlueFlagConceding,
            WeatherTyreChanges = entry.WeatherTyreChanges ?? driver.Ratings.WeatherTyreChanges,
            AvoidanceOfForcedMistakes = entry.AvoidanceOfForcedMistakes ?? driver.Ratings.AvoidanceOfForcedMistakes,
            FuelManagement = entry.FuelManagement ?? driver.Ratings.FuelManagement,
            SetupDownforce = entry.SetupDownforce ?? driver.Ratings.SetupDownforce,
            SetupDownforceRandomness = entry.SetupDownforceRandomness ?? driver.Ratings.SetupDownforceRandomness,
        },
    };
}
