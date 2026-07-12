using Companion.Core.Career;

namespace Companion.Core.Character;

/// <summary>What an accident did to the driver (character death &amp; injury §3.2/§3.3).</summary>
public enum AccidentOutcomeKind
{
    /// <summary>No injury — the driver walks away.</summary>
    None,

    /// <summary>A minor injury — sit out <see cref="AccidentOutcome.MissRaces"/> race(s).</summary>
    MinorInjury,

    /// <summary>Out for the rest of the season; returns next year.</summary>
    SeasonEnding,

    /// <summary>Fatal — terminal.</summary>
    Death,
}

/// <summary>The resolved accident: the outcome kind, how many races a minor injury sits out, and the
/// effective (safety-adjusted) d500 roll that produced it — journaled for the Why? inspector.</summary>
public sealed record AccidentOutcome(AccidentOutcomeKind Kind, int MissRaces, int EffectiveRoll);

/// <summary>One band of the d500 outcome table: outcomes for effective rolls up to (and including)
/// <see cref="UpTo"/>. Bands are listed low→high; the first band whose <see cref="UpTo"/> the effective
/// roll does not exceed wins. The last band must cover 500.</summary>
public sealed record AccidentBand
{
    /// <summary>Inclusive upper bound of the effective d500 roll for this band.</summary>
    public required int UpTo { get; init; }

    /// <summary>Outcome name: <c>none</c> | <c>minorInjury</c> | <c>seasonEnding</c> | <c>death</c>.</summary>
    public required string Outcome { get; init; }

    /// <summary>Races missed for a <c>minorInjury</c> band (ignored otherwise).</summary>
    public int MissRaces { get; init; }
}

/// <summary>The tunable accident bands + safety-offset scales (character death &amp; injury §3.4). Lives on
/// <see cref="CharacterRules"/> (parsed from perks.json) so Mike can retune without a rebuild; a code
/// <see cref="AccidentModel.DefaultRules"/> is the fallback when the data omits it.</summary>
public sealed record AccidentRules
{
    /// <summary>How strongly durability (and injury durability perks) shift the roll toward safety.</summary>
    public double SafetyDurabilityScale { get; init; } = 80.0;

    /// <summary>How strongly a reckless injury baseAdd (e.g. hot_head) shifts the roll toward danger.</summary>
    public double SafetyBaseAddScale { get; init; } = 200.0;

    public required IReadOnlyList<AccidentBand> Light { get; init; }
    public required IReadOnlyList<AccidentBand> Medium { get; init; }
    public required IReadOnlyList<AccidentBand> Heavy { get; init; }

    public IReadOnlyList<AccidentBand> BandsFor(AccidentSeverity severity) => severity switch
    {
        AccidentSeverity.Light => Light,
        AccidentSeverity.Heavy => Heavy,
        _ => Medium,
    };
}

/// <summary>
/// PURE resolution of an accident into an outcome (character death &amp; injury §3.2–§3.4). The d500 roll is
/// modified by the driver's safety profile via a deterministic INTEGER offset (never a second draw — the
/// draw count stays fixed): high durability + protective injury perks shift the effective roll DOWN toward
/// the safe bands, reckless ones shift it UP toward death. The effective roll is then bucketed against the
/// severity's bands. Everything here is a pure function of its inputs, so the fold re-derives it identically.
/// </summary>
public static class AccidentModel
{
    /// <summary>The §3.4 default d500 table (1 unit = 0.2%). Bands are inclusive upper bounds on the
    /// effective roll; higher effective = more dangerous. Used when perks.json ships no accident block.</summary>
    public static AccidentRules DefaultRules { get; } = new()
    {
        SafetyDurabilityScale = 80.0,
        SafetyBaseAddScale = 200.0,
        Light =
        [
            new() { UpTo = 480, Outcome = "none" },
            new() { UpTo = 496, Outcome = "minorInjury", MissRaces = 1 },
            new() { UpTo = 499, Outcome = "minorInjury", MissRaces = 2 },
            new() { UpTo = 500, Outcome = "death" },
        ],
        Medium =
        [
            new() { UpTo = 410, Outcome = "none" },
            new() { UpTo = 470, Outcome = "minorInjury", MissRaces = 1 },
            new() { UpTo = 490, Outcome = "minorInjury", MissRaces = 2 },
            new() { UpTo = 497, Outcome = "seasonEnding" },
            new() { UpTo = 500, Outcome = "death" },
        ],
        Heavy =
        [
            new() { UpTo = 250, Outcome = "none" },
            new() { UpTo = 380, Outcome = "minorInjury", MissRaces = 1 },
            new() { UpTo = 450, Outcome = "minorInjury", MissRaces = 2 },
            new() { UpTo = 485, Outcome = "seasonEnding" },
            new() { UpTo = 500, Outcome = "death" },
        ],
    };

    /// <summary>The integer safety shift (positive = safer): high durability (and protective injury
    /// perks, via <see cref="PlayerPerkModifiers.InjuryDurabilityDelta"/>) push it up; a reckless injury
    /// baseAdd pushes it down. Quantized (AwayFromZero) so it is deterministic and machine-stable.</summary>
    public static int SafetyOffset(double durability, PlayerPerkModifiers mods, AccidentRules rules)
    {
        double effectiveDurability = durability + mods.InjuryDurabilityDelta;
        double raw = (effectiveDurability - 0.5) * rules.SafetyDurabilityScale
            - mods.InjuryBaseAdd * rules.SafetyBaseAddScale;
        return (int)Math.Round(raw, MidpointRounding.AwayFromZero);
    }

    /// <summary>Resolve a d500 roll (1..500) + severity + safety profile into an outcome. Protective
    /// drivers LOWER the effective roll (toward the safe low bands); reckless ones RAISE it (toward death).
    /// The effective roll is clamped to [1,500] and bucketed against the severity's bands.</summary>
    public static AccidentOutcome Resolve(
        AccidentSeverity severity, int roll, double durability, PlayerPerkModifiers mods, AccidentRules rules)
    {
        int effective = Math.Clamp(roll - SafetyOffset(durability, mods, rules), 1, 500);
        var bands = rules.BandsFor(severity);
        foreach (var band in bands)
        {
            if (effective <= band.UpTo)
                return new AccidentOutcome(ParseKind(band.Outcome), band.MissRaces, effective);
        }
        // Defensive: a well-formed table's last band covers 500, so this only trips on malformed data.
        var last = bands.Count > 0 ? bands[^1] : new AccidentBand { UpTo = 500, Outcome = "none" };
        return new AccidentOutcome(ParseKind(last.Outcome), last.MissRaces, effective);
    }

    private static AccidentOutcomeKind ParseKind(string outcome) => outcome switch
    {
        "minorInjury" => AccidentOutcomeKind.MinorInjury,
        "seasonEnding" => AccidentOutcomeKind.SeasonEnding,
        "death" => AccidentOutcomeKind.Death,
        _ => AccidentOutcomeKind.None,
    };
}
