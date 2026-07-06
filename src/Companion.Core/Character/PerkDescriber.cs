namespace Companion.Core.Character;

/// <summary>
/// Turns a perk's machine-readable <see cref="PerkEffect"/>s into plain-language lines, so the
/// creator and the dossier can say what a perk actually does instead of showing opaque numbers
/// (character depth 5). Pure and data-driven — falls back to a perk effect's authored note for any
/// lever it does not have a friendlier phrase for.
/// </summary>
public static class PerkDescriber
{
    /// <summary>The good things a perk does, in plain language (empty when it has none).</summary>
    public static IReadOnlyList<string> Benefits(Perk perk) =>
        perk.Effects.Where(e => e.Kind == "benefit").Select(Describe).Where(s => s.Length > 0).ToList();

    /// <summary>The costs a perk carries, in plain language.</summary>
    public static IReadOnlyList<string> Drawbacks(Perk perk) =>
        perk.Effects.Where(e => e.Kind == "drawback").Select(Describe).Where(s => s.Length > 0).ToList();

    /// <summary>One effect as a short phrase (e.g. "Stronger race pace", "Faster car (real pace)",
    /// "Mistakes punished harder — in the wet").</summary>
    public static string Describe(PerkEffect e)
    {
        bool up = e.Magnitude > 0;
        string phrase = e.Lever switch
        {
            "statDelta" => $"{(up ? "Stronger" : "Weaker")} {Rating(e.Target)}",
            "carScalar" => CarScalar(e.Target, up),
            "opiErrorBlame" => up ? "Mistakes punished harder" : "Mistakes forgiven more",
            "opiRetention" => e.Target == "gainSide"
                ? (up ? "Form swings build faster" : "Form swings build slower")
                : (up ? "Steadier reputation" : "Streakier reputation"),
            "reputationGainRate" => up ? "Reputation grows faster" : "Reputation grows slower",
            "underdogMultiplier" => up ? "More credit for punching above the car" : "Less underdog credit",
            "marketability" => up ? "More marketable" : "Less marketable",
            "paceAnchorAlpha" => up ? "Dials a new car in faster" : "Dials a new car in slower",
            "agingCurve" => Aging(e.Target, up),
            "offerWeight" => Offer(e.Target, up),
            "income" => up ? "Brings sponsor money" : "Brings less money",
            "injuryHazard" => Injury(e.Target, e.Magnitude) ? "Higher injury risk" : "Lower injury risk",
            "xpRate" => up ? "Levels up faster" : "Levels up slower",
            "statPoints" => up ? "Extra points each level" : "Fewer points each level",
            _ => e.Note ?? "",
        };
        return phrase.Length == 0 ? phrase : phrase + Condition(e.Condition);
    }

    private static string Rating(string? target) => target switch
    {
        "raceSkill" => "race pace",
        "qualifyingSkill" => "one-lap pace",
        "aggression" => "overtaking",
        "defending" => "defending",
        "avoidanceOfMistakes" => "composure",
        "consistency" => "consistency",
        "startReactions" => "starts",
        "wetSkill" => "wet-weather pace",
        "tyreManagement" => "tyre management",
        "stamina" => "stamina",
        "fuelManagement" => "fuel saving",
        "chosenFlavor" => "chosen specialism",
        _ => target ?? "a skill",
    };

    private static string CarScalar(string? target, bool up)
    {
        // Power up, or weight/drag down, means a quicker car; the reverse is slower.
        bool faster = target == "power" ? up : !up; // weight/drag are "hurts" — down = faster
        return (faster ? "Faster car" : "Slower car") + " (real pace)";
    }

    private static string Aging(string? target, bool up) => target switch
    {
        "peakShift" => up ? "Peaks later in career" : "Peaks earlier",
        "declineAccelMult" => up ? "Fades faster with age" : "Ages more gracefully",
        "riseMult" => up ? "Improves faster when young" : "Improves slower",
        _ => up ? "Ages better" : "Ages worse",
    };

    private static string Offer(string? target, bool up) => target switch
    {
        "experience" => up ? "Reads as more experienced" : "Reads as less experienced",
        "salaryAsk" => up ? "Asks for more pay" : "Works for less",
        "ageRisk" => up ? "Teams worry more about age" : "Teams worry less about age",
        "repFloorRelax" => "Gets a look from bigger teams sooner",
        _ => up ? "More appealing to teams" : "Less appealing to teams",
    };

    // durabilityDelta down = more fragile; baseAdd / perErrorAdd up = more risk.
    private static bool Injury(string? target, double magnitude) =>
        target == "durabilityDelta" ? magnitude < 0 : magnitude > 0;

    private static string Condition(string? condition) => condition switch
    {
        null => "",
        "wetRound" => " — in the wet",
        "dryRound" => " — in the dry",
        "longRace" => " — on long races",
        "shortRace" => " — on short races",
        "tierLte2" => " — in a top car",
        "tierGte4" => " — in a weak car",
        "eraTransition" => " — across an era change",
        "driverErrorDnf" => " — after a mistake",
        "ageLtPeak" => " — while young",
        "ageGtePeak" => " — as a veteran",
        _ => "",
    };
}
