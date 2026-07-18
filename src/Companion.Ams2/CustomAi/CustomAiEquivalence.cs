namespace Companion.Ams2.CustomAi;

/// <summary>Outcome of comparing a generated grid file against the installed class XML.</summary>
public sealed record CustomAiEquivalenceResult
{
    /// <summary>True when every generated seat has an installed base entry whose effective
    /// fields match, staging would change nothing the game reads for this grid.</summary>
    public required bool Matches { get; init; }

    /// <summary>Human-readable field-level differences (empty when <see cref="Matches"/>).</summary>
    public required IReadOnlyList<string> Differences { get; init; }
}

/// <summary>
/// Decides whether an installed class XML already satisfies a generated grid (NAMeS-first
/// diff-aware staging, locked decision #7). The comparison is per generated seat against the
/// installed file's BASE entry for that livery: names/countries ordinal-equal, skill floats
/// within <see cref="FloatTolerance"/> (both-absent also counts as equal, an absent skill
/// means "stock driver default"), and physics scalars equivalent treating an omitted scalar
/// as the neutral 1.0. Extra installed entries, other liveries, per-track overrides, never
/// break equivalence: they are the community author's own refinements and staying out of
/// their way is the point.
/// </summary>
public static class CustomAiEquivalence
{
    public const double FloatTolerance = 1e-4;

    public static CustomAiEquivalenceResult Compare(CustomAiFile generated, CommunityAiFile installed)
    {
        var differences = new List<string>();
        var byLivery = installed.BaseEntriesByLivery();

        foreach (var seat in generated.Drivers)
        {
            if (!byLivery.TryGetValue(seat.LiveryName, out var entry))
            {
                differences.Add($"'{seat.LiveryName}': the installed file has no base entry for this livery.");
                continue;
            }
            CompareEntry(seat, entry, differences);
        }

        return new CustomAiEquivalenceResult
        {
            Matches = differences.Count == 0,
            Differences = differences,
        };
    }

    private static void CompareEntry(CustomAiDriver generated, CustomAiDriver installed, List<string> differences)
    {
        string livery = generated.LiveryName;

        Text(livery, "name", generated.Name, installed.Name, differences);
        Text(livery, "country", generated.Country, installed.Country, differences);

        Skill(livery, "race_skill", generated.RaceSkill, installed.RaceSkill, differences);
        Skill(livery, "qualifying_skill", generated.QualifyingSkill, installed.QualifyingSkill, differences);
        Skill(livery, "aggression", generated.Aggression, installed.Aggression, differences);
        Skill(livery, "defending", generated.Defending, installed.Defending, differences);
        Skill(livery, "stamina", generated.Stamina, installed.Stamina, differences);
        Skill(livery, "consistency", generated.Consistency, installed.Consistency, differences);
        Skill(livery, "start_reactions", generated.StartReactions, installed.StartReactions, differences);
        Skill(livery, "wet_skill", generated.WetSkill, installed.WetSkill, differences);
        Skill(livery, "tyre_management", generated.TyreManagement, installed.TyreManagement, differences);
        Skill(livery, "fuel_management", generated.FuelManagement, installed.FuelManagement, differences);
        Skill(livery, "blue_flag_conceding", generated.BlueFlagConceding, installed.BlueFlagConceding, differences);
        Skill(livery, "weather_tyre_changes", generated.WeatherTyreChanges, installed.WeatherTyreChanges, differences);
        Skill(livery, "avoidance_of_mistakes", generated.AvoidanceOfMistakes, installed.AvoidanceOfMistakes, differences);
        Skill(livery, "avoidance_of_forced_mistakes", generated.AvoidanceOfForcedMistakes, installed.AvoidanceOfForcedMistakes, differences);
        Skill(livery, "vehicle_reliability", generated.VehicleReliability, installed.VehicleReliability, differences);
        Skill(livery, "setup_downforce", generated.SetupDownforce, installed.SetupDownforce, differences);
        Skill(livery, "setup_downforce_randomness", generated.SetupDownforceRandomness, installed.SetupDownforceRandomness, differences);

        // Physics scalars: an omitted scalar IS 1.0 in-game, so omitted-vs-1.0 is equal.
        Scalar(livery, "weight_scalar", generated.WeightScalar, installed.WeightScalar, differences);
        Scalar(livery, "power_scalar", generated.PowerScalar, installed.PowerScalar, differences);
        Scalar(livery, "drag_scalar", generated.DragScalar, installed.DragScalar, differences);
    }

    private static void Text(string livery, string field, string? generated, string? installed, List<string> differences)
    {
        if (!string.Equals(generated, installed, StringComparison.Ordinal))
            differences.Add($"'{livery}': {field} differs ('{generated ?? "<absent>"}' vs installed '{installed ?? "<absent>"}').");
    }

    /// <summary>Skill-like floats have no known default: equal means both absent, or both
    /// present within tolerance. Present-vs-absent changes what the game does, a difference.</summary>
    private static void Skill(string livery, string field, double? generated, double? installed, List<string> differences)
    {
        if (generated is null && installed is null)
            return;
        if (generated is { } g && installed is { } i)
        {
            if (Math.Abs(g - i) > FloatTolerance)
                differences.Add($"'{livery}': {field} differs ({Format(generated)} vs installed {Format(installed)}).");
            return;
        }
        differences.Add($"'{livery}': {field} differs ({Format(generated)} vs installed {Format(installed)}).");
    }

    private static void Scalar(string livery, string field, double? generated, double? installed, List<string> differences)
    {
        double g = generated ?? 1.0;
        double i = installed ?? 1.0;
        if (Math.Abs(g - i) > FloatTolerance)
            differences.Add($"'{livery}': {field} differs ({Format(generated)} vs installed {Format(installed)}).");
    }

    private static string Format(double? value) =>
        value is { } number ? number.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture) : "<absent>";
}
