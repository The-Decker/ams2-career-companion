using System.Globalization;

namespace Companion.ViewModels.Services;

/// <summary>
/// Honest, qualitative fuel-and-distance guidance for the Race-Day briefing (docs/dev
/// ams2-custom-race-reference §8): AMS2 makes the player own the starting fuel load in the car
/// setup and does not reliably auto-fill enough, so a long vintage race can run dry. The app can't
/// read exact per-lap burn (packed in the car's <c>.bff</c>), so this compares the round's lap count
/// against a per-class one-tank range and tells the player how not to run out — never recommending
/// refuelling for an era that didn't refuel.
/// </summary>
public static class FuelGuidance
{
    /// <summary>A class's fuel envelope: the tank size and the conservative distance it covers at 1×
    /// (rich-map) consumption. Keyed by the pack's exact <c>ams2Class</c>; a class with no profile
    /// gets no note (so packs we haven't measured are unaffected until authored).</summary>
    private readonly record struct FuelProfile(int TankLitres, int OneTankLaps, int SafeLaps);

    private static readonly IReadOnlyDictionary<string, FuelProfile> Profiles =
        new Dictionary<string, FuelProfile>(StringComparer.Ordinal)
        {
            // F-Vintage Gen1 (1967): 190 L ≈ 55–58 laps at 1× (ams2cars.info + Reiza forum fuel thread).
            // SafeLaps 55 is the conservative "fill to the distance and you're fine" boundary.
            ["F-Vintage_Gen1"] = new(TankLitres: 190, OneTankLaps: 58, SafeLaps: 55),
        };

    /// <summary>The advisory line for a round, or null when the class has no fuel profile.
    /// <paramref name="refuellingAllowed"/> only adds the era "cars don't refuel" caveat when it is
    /// explicitly <c>false</c> — an unknown (null) season stays neutral.</summary>
    public static string? Note(string ams2Class, int laps, bool? refuellingAllowed)
    {
        if (!Profiles.TryGetValue(ams2Class, out var profile))
            return null;

        string lapsText = laps.ToString(CultureInfo.InvariantCulture);
        string noRefuel = refuellingAllowed == false
            ? " These cars don't refuel, so you must finish the distance on one tank."
            : "";

        string main = laps <= profile.SafeLaps
            ? $"⛽ One tank (~{profile.TankLitres} L, ~{profile.OneTankLaps} laps) covers this " +
              $"{lapsText}-lap race — set your starting fuel to the full distance in the car setup " +
              $"(Setup → Fuel).{noRefuel}"
            : $"⛽ {lapsText} laps is beyond the ~{profile.OneTankLaps}-lap range of the " +
              $"~{profile.TankLitres} L tank at full (1×) consumption. Fill to max and save fuel — " +
              "a leaner fuel map (ICM) + short-shifting — or lower Options → Gameplay → Fuel Usage." +
              noRefuel;

        // The gotcha, once, on the same panel: AMS2 doesn't auto-fill enough and the strategy won't
        // apply unless a setup value changes.
        const string gotcha =
            " Note: AMS2 doesn't auto-fill enough — set the fuel load yourself, and change at least " +
            "one setup value or the pit strategy won't apply.";

        return main + gotcha;
    }
}
