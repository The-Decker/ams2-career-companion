using System.Globalization;

namespace Companion.ViewModels.Services;

/// <summary>
/// Honest, qualitative fuel-and-distance guidance for the Race-Day briefing (docs/dev
/// ams2-custom-race-reference §8): AMS2 makes the player own the starting fuel load in the car
/// setup and does not reliably auto-fill enough, so a long vintage race can run dry. The app can't
/// read exact per-lap burn (packed in the car's <c>.bff</c>), so this compares the round's lap count
/// against a per-class one-tank range and tells the player how not to run out, never recommending
/// refuelling for an era that didn't refuel.
/// </summary>
public static class FuelGuidance
{
    /// <summary>A class's fuel envelope: the tank size and the conservative distance it covers at 1×
    /// (rich-map) consumption. Keyed by the pack's exact <c>ams2Class</c>; a class with no profile
    /// gets no note (so packs we haven't measured are unaffected until authored).</summary>
    private readonly record struct FuelProfile(int TankLitres, int OneTankLaps, int SafeLaps);

    // Tank litres are AMS2-stated (ams2cars.info per-car "Fuel capacity") unless marked (est.);
    // one-tank laps = conservative range at 1× consumption on a representative ~5 km GP circuit of
    // the class's era, derived from Reiza-forum fuel data anchored to the measured F-Vintage_Gen1
    // exemplar (~3.3 L/lap) and era consumption records (m2 deep-pass research, 2026-07-09, full
    // sourcing in that session's research notes). (est.) classes shipped in v1.6.9.8, newer than
    // every public spec sheet: figures deliberately err toward over-fuelling, never running dry.
    private static readonly IReadOnlyDictionary<string, FuelProfile> Profiles =
        new Dictionary<string, FuelProfile>(StringComparer.Ordinal)
        {
            // F-Vintage Gen1 (1967): 190 L ≈ 55–58 laps at 1× (ams2cars.info + Reiza forum fuel thread).
            // SafeLaps 55 is the conservative "fill to the distance and you're fine" boundary.
            ["F-Vintage_Gen1"] = new(TankLitres: 190, OneTankLaps: 58, SafeLaps: 55),
            ["F-Vintage_Gen2"] = new(TankLitres: 190, OneTankLaps: 55, SafeLaps: 52),   // 1969, all 3 cars 190 L
            ["F-Retro_Gen1"] = new(TankLitres: 250, OneTankLaps: 69, SafeLaps: 65),     // 1974, AMS2 oversizes vs real ~190 L
            ["F-Retro_Gen2"] = new(TankLitres: 250, OneTankLaps: 65, SafeLaps: 61),     // 1978 ground effect
            ["F-Retro_Gen3"] = new(TankLitres: 250, OneTankLaps: 59, SafeLaps: 56),     // 1983-spec turbos (BT52 outlier: 195 L)
            ["F-Classic_Gen1"] = new(TankLitres: 195, OneTankLaps: 48, SafeLaps: 45),   // 1986, real 195 L fuel-limit modelled
            ["F-Classic_Gen2"] = new(TankLitres: 150, OneTankLaps: 50, SafeLaps: 47),   // 1988, real 150 L turbo fuel limit
            ["F-Classic_Gen3"] = new(TankLitres: 205, OneTankLaps: 58, SafeLaps: 55),   // 1990 atmo
            ["F-Classic_Gen4"] = new(TankLitres: 220, OneTankLaps: 58, SafeLaps: 55),   // 1991 atmo
            ["F-Hitech_Gen1"] = new(TankLitres: 205, OneTankLaps: 55, SafeLaps: 52),    // 1992 active era
            ["F-Hitech_Gen2"] = new(TankLitres: 205, OneTankLaps: 56, SafeLaps: 53),    // 1993 active era
            ["FE-G1"] = new(TankLitres: 140, OneTankLaps: 41, SafeLaps: 39),            // 1995 Formula Edge (ex-F-V12, 140 L)
            ["F-V10_Gen1"] = new(TankLitres: 221, OneTankLaps: 51, SafeLaps: 48),       // 1997, AMS2 oversizes vs real ~140 L
            ["F-V10_Gen2"] = new(TankLitres: 221, OneTankLaps: 56, SafeLaps: 53),       // 2000
            ["F-V10_Gen3"] = new(TankLitres: 221, OneTankLaps: 55, SafeLaps: 52),       // 2005
            ["F-V8_Gen1"] = new(TankLitres: 100, OneTankLaps: 28, SafeLaps: 26),        // 2006 (est.) period-realistic cell
            // 2008 (est.): platform lineage suggests a 221 L tank, but the class is unverified and it
            // only races refuel-era seasons, keep the period-realistic figure so the briefing says
            // "plan a stop" (historically correct) instead of a possibly-false "one tank covers it".
            ["F-V8_Gen2"] = new(TankLitres: 110, OneTankLaps: 30, SafeLaps: 28),
            ["F-Ultimate_Gen1"] = new(TankLitres: 146, OneTankLaps: 54, SafeLaps: 51),  // 2016 hybrid, ~100 kg race fuel
            ["F-Ultimate"] = new(TankLitres: 146, OneTankLaps: 56, SafeLaps: 53),       // 2020 hybrid, 110 kg allowance
        };

    /// <summary>The advisory line for a round, or null when the class has no fuel profile.
    /// <paramref name="refuellingAllowed"/> only adds the era "cars don't refuel" caveat when it is
    /// explicitly <c>false</c>, an unknown (null) season stays neutral.</summary>
    public static string? Note(string ams2Class, int laps, bool? refuellingAllowed)
    {
        if (!Profiles.TryGetValue(ams2Class, out var profile))
            return null;

        string lapsText = laps.ToString(CultureInfo.InvariantCulture);

        // The gotcha, once, on the same panel: AMS2 doesn't auto-fill enough and the strategy won't
        // apply unless a setup value changes.
        const string gotcha =
            " Note: AMS2 doesn't auto-fill enough, set the fuel load yourself, and change at least " +
            "one setup value or the pit strategy won't apply.";

        // Refuelling era (1994–2009 seasons): pit strategy is the era-authentic play, so lead with
        // it, never a bare "fill to the distance" that tempts a no-stop on an unverified tank.
        if (refuellingAllowed == true)
        {
            // Over-range copy quotes SafeLaps, the number this branch actually switched on, so a
            // race in the SafeLaps..OneTankLaps window never reads "X laps is beyond ~Y" with X <= Y.
            string main = laps <= profile.SafeLaps
                ? $"⛽ Refuelling is allowed this season. One tank (~{profile.TankLitres} L, " +
                  $"~{profile.OneTankLaps} laps) can cover this {lapsText}-lap race non-stop, or " +
                  "run a lighter load and refuel for pace, era-style (Setup → Fuel + pit strategy)."
                : $"⛽ Refuelling is allowed this season, and likely needed: {lapsText} laps is beyond " +
                  $"the conservative ~{profile.SafeLaps}-lap safe range of the ~{profile.TankLitres} L " +
                  "tank at full (1×) consumption. Plan at least one fuel stop (Setup → Fuel + pit strategy).";
            return main + gotcha;
        }

        string noRefuel = refuellingAllowed == false
            ? " These cars don't refuel, so you must finish the distance on one tank."
            : "";

        string oneTank = laps <= profile.SafeLaps
            ? $"⛽ One tank (~{profile.TankLitres} L, ~{profile.OneTankLaps} laps) covers this " +
              $"{lapsText}-lap race, set your starting fuel to the full distance in the car setup " +
              $"(Setup → Fuel).{noRefuel}"
            : $"⛽ {lapsText} laps is beyond the conservative ~{profile.SafeLaps}-lap safe range of " +
              $"the ~{profile.TankLitres} L tank at full (1×) consumption. Fill to max and save fuel, " +
              "a leaner fuel map (ICM) + short-shifting, or lower Options → Gameplay → Fuel Usage." +
              noRefuel;

        return oneTank + gotcha;
    }
}
