namespace Companion.Core.Character;

/// <summary>
/// One display-ready mechanical effect with an explicit boundary label. <see cref="Text"/> remains
/// the same ready-to-show phrase used by the legacy benefit/drawback lists; the raw condition is
/// also retained so a graphical tree can distinguish conditional mechanics without parsing copy.
/// </summary>
public sealed record CharacterEffectLine
{
    public required string Kind { get; init; }
    public required CharacterEffectClass Classification { get; init; }
    public required string ClassificationLabel { get; init; }
    public required string Text { get; init; }
    public string? Condition { get; init; }
    public bool IsConditional => !string.IsNullOrWhiteSpace(Condition);
}

/// <summary>
/// Turns a perk's machine-readable <see cref="PerkEffect"/>s into plain-language lines, so the
/// creator and the dossier can say what a perk actually does instead of showing opaque numbers
/// (character depth 5). Pure and data-driven, falls back to a perk effect's authored note for any
/// lever it does not have a friendlier phrase for.
/// </summary>
public static class PerkDescriber
{
    /// <summary>All non-empty effects in authored order, with their human-player boundary made explicit.</summary>
    public static IReadOnlyList<CharacterEffectLine> Effects(Perk perk) =>
        perk.Effects.Select(DescribeLine).Where(line => line.Text.Length > 0).ToList();

    /// <summary>The good things a perk does, in plain language (empty when it has none).</summary>
    public static IReadOnlyList<string> Benefits(Perk perk) =>
        Benefits(Effects(perk));

    /// <summary>The benefit text derived from an already-built effect-line collection.</summary>
    public static IReadOnlyList<string> Benefits(IReadOnlyList<CharacterEffectLine> effects) =>
        effects.Where(line => line.Kind == "benefit").Select(line => line.Text).ToList();

    /// <summary>The costs a perk carries, in plain language.</summary>
    public static IReadOnlyList<string> Drawbacks(Perk perk) =>
        Drawbacks(Effects(perk));

    /// <summary>The drawback text derived from an already-built effect-line collection.</summary>
    public static IReadOnlyList<string> Drawbacks(IReadOnlyList<CharacterEffectLine> effects) =>
        effects.Where(line => line.Kind == "drawback").Select(line => line.Text).ToList();

    /// <summary>Builds one classified line, honoring an authored classification before legacy mapping.</summary>
    public static CharacterEffectLine DescribeLine(PerkEffect effect)
    {
        CharacterEffectClass classification = effect.Classification ?? DefaultClassification(effect.Lever);
        return CreateLine(effect.Kind, classification, Describe(effect), effect.Condition);
    }

    /// <summary>Builds a classified line for a projection-native effect such as a stat-raise node.</summary>
    public static CharacterEffectLine CreateLine(
        string kind,
        CharacterEffectClass classification,
        string text,
        string? condition = null) => new()
    {
        Kind = kind,
        Classification = classification,
        ClassificationLabel = ClassificationLabel(classification),
        Text = text,
        Condition = condition,
    };

    /// <summary>Absent-field compatibility for every lever in the existing v1 perk catalog.</summary>
    public static CharacterEffectClass DefaultClassification(string lever) => lever switch
    {
        "statDelta" => CharacterEffectClass.Expectation,
        "carScalar" => CharacterEffectClass.Car,
        _ => CharacterEffectClass.Career,
    };

    public static string ClassificationLabel(CharacterEffectClass classification) => classification switch
    {
        CharacterEffectClass.Expectation => "EXPECTATION",
        CharacterEffectClass.Career => "CAREER",
        CharacterEffectClass.Car => "CAR",
        _ => throw new ArgumentOutOfRangeException(nameof(classification), classification, null),
    };

    /// <summary>One effect as a short phrase (e.g. "Stronger race pace", "Faster car (real pace)",
    /// "Mistakes punished harder, in the wet").</summary>
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
            "statPoints" => StatPoints(e.Target, up),
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
        bool faster = target == "power" ? up : !up; // weight/drag are "hurts", down = faster
        return (faster ? "Faster car" : "Slower car") + " (real pace)";
    }

    private static string Aging(string? target, bool up) => target switch
    {
        "peakShift" => up ? "Peaks later in career" : "Peaks earlier",
        "declineAccelMult" => up ? "Fades faster with age" : "Ages more gracefully",
        _ => up ? "Ages better" : "Ages worse",
    };

    private static string StatPoints(string? target, bool up) => target switch
    {
        "lockToOne" => "Only your one specialism can ever be developed",
        "softCap" => up ? "Raises the stat ceiling" : "Lowers the stat ceiling",
        _ => up ? "Extra points each level" : "Fewer points each level",
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
        "wetRound" => ", in the wet",
        "dryRound" => ", in the dry",
        "longRace" => ", on long races",
        "shortRace" => ", on short races",
        "tierLte2" => ", in a top car",
        "tierGte4" => ", in a weak car",
        "eraTransition" => ", across an era change",
        "driverErrorDnf" => ", after a mistake",
        "ageLtPeak" => ", while young",
        "ageGtePeak" => ", as a veteran",
        _ => "",
    };
}
