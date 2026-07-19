using Companion.Ams2.ContentLibrary;
using Companion.Core.Packs;

namespace Companion.Ams2.Preflight;

/// <summary>A modded vehicle a pack's opt-in field needs, and whether it is installed (its car is
/// in the extracted content library, and, when the install path is known, the community livery
/// override folder for it exists).</summary>
public sealed record RequiredModVehicle(string VehicleId, string ModName, bool Installed);

/// <summary>
/// The pre-season check for a pack's OPT-IN modded field (<see cref="PackManifest.ModdedField"/>):
/// whether the required community car mod is installed. Rule (same as the alternate tracks): the
/// modded entries apply ONLY when the player ticks it on AND the mod is present; otherwise the
/// season generates on its base field and no mod is assumed. Surfaces the status for the wizard
/// and gates the creation-time <see cref="ModdedFieldTransform"/>.
/// </summary>
public static class ModdedVehiclePreflight
{
    /// <summary>The pack's required mod vehicle + whether it is installed, or null when the pack
    /// declares no modded field. Installed = the vehicle id is in the library AND (when the
    /// install path is known) its livery-override folder exists, the override is where the mod's
    /// skins live, so its absence means the cars would show default liveries.</summary>
    public static RequiredModVehicle? RequiredModVehicleFor(
        SeasonPack pack, Ams2ContentLibrary library, string? installDirectory)
    {
        if (pack.Manifest.ModdedField is not { } field)
            return null;

        bool inLibrary = library.Vehicles.ContainsKey(field.VehicleId);
        bool overridePresent = installDirectory is not { Length: > 0 } dir
            || Directory.Exists(Path.Combine(
                dir, "Vehicles", "Textures", "CustomLiveries", "Overrides", field.VehicleId));
        return new RequiredModVehicle(field.VehicleId, field.ModName, inLibrary && overridePresent);
    }

    /// <summary>True only when the pack HAS a modded field AND the required vehicle mod is
    /// installed, the condition under which the creation-time transform may add the entries.</summary>
    public static bool CanApplyModdedField(
        SeasonPack pack, Ams2ContentLibrary library, string? installDirectory) =>
        RequiredModVehicleFor(pack, library, installDirectory) is { Installed: true };
}
