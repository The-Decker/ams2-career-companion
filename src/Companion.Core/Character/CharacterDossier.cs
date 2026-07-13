namespace Companion.Core.Character;

/// <summary>
/// A read-only snapshot of the player's driver for the hub's Driver dossier: identity, the seven
/// stats, the perks (with what they do), and progression (level + XP toward the next). A pure
/// projection built from the folded <c>PlayerCareerState</c> + the character rules — no session
/// coupling — so the dossier view-model is built and tested from a plain value. (Character depth 3.)
/// </summary>
public sealed record CharacterDossier
{
    public required string Name { get; init; }

    /// <summary>The driver's CURRENT age (their created first-season age plus the seasons since), or
    /// null for a legacy character created before ages existed. A real, visible part of the driver.</summary>
    public int? Age { get; init; }

    public required int Level { get; init; }
    public required long Xp { get; init; }

    /// <summary>XP accumulated INTO the current level (0 at a fresh level).</summary>
    public required long XpIntoLevel { get; init; }

    /// <summary>XP required to advance from the current level to the next; 0 at the max level.</summary>
    public required long XpForNextLevel { get; init; }

    /// <summary>Character points banked for between-season spending.</summary>
    public required int CpUnspent { get; init; }

    public required IReadOnlyList<DossierStat> Stats { get; init; }

    public required IReadOnlyList<DossierPerk> Perks { get; init; }

    /// <summary>The season-end injury risk this driver carries ("Low"/"Moderate"/"High"), or null
    /// when the character has no injury-stream perk (and so is never exposed to the roll).</summary>
    public string? InjuryRisk { get; init; }

    /// <summary>The driver's CURRENT availability, folded from the accident/injury state
    /// (docs/dev/character-death-injury.md §6): Fit / Injured / Season over / Deceased. A healthy
    /// driver — and every Off-mortality career, whose injury fields are always default — reads Fit.</summary>
    public AvailabilityStatus Availability { get; init; }

    /// <summary>A ready-to-show label for <see cref="Availability"/> ("Fit", "Injured — out 2 races",
    /// "Season over — recovering", "Deceased"); mirrors the fold in <c>PlayerMortalityStatus.IsFit</c>.</summary>
    public string AvailabilityLabel { get; init; } = "Fit";

    /// <summary>Progress through the current level, 0..1 (1 at the max level).</summary>
    public double LevelProgress => XpForNextLevel <= 0 ? 1.0 : Math.Clamp((double)XpIntoLevel / XpForNextLevel, 0.0, 1.0);

    public static CharacterDossier Build(
        CharacterProfile character, int level, long xp, CharacterRules rules, int? age = null,
        int raceSuspensionRemaining = 0, bool seasonEndingInjury = false, bool deceased = false,
        int? levelCap = null, int? progressionYear = null)
    {
        // Cumulative XP required to have REACHED the current level, and the cost of the next level,
        // selected through the same progression-version dispatcher the live/replay folds use.
        long cumulativeToLevel = CharacterLevelProgression.CumulativeXpToLevel(
            character.ProgressionVersion, level, rules);
        int versionCap = CharacterLevelProgression.MaxLevel(
            character.ProgressionVersion,
            progressionYear ?? int.MaxValue,
            rules);
        int effectiveCap = Math.Min(versionCap, levelCap ?? versionCap);
        long forNext = level >= effectiveCap
            ? 0
            : CharacterLevelProgression.XpForLevel(
                character.ProgressionVersion, level + 1, rules);

        var stats = new List<DossierStat>();
        foreach (var stat in rules.Stats.TalentStats)
            stats.Add(new DossierStat(stat.Id, CharacterLabels.Stat(stat.Id), character.Stat(stat.Id), Talent: true));
        foreach (var meta in rules.Stats.MetaStats)
            stats.Add(new DossierStat(meta.Id, CharacterLabels.Stat(meta.Id), character.Stat(meta.Id), Talent: false));

        var perks = new List<DossierPerk>();
        foreach (string perkId in character.PerkIds)
        {
            if (rules.TryGetPerk(perkId, out var perk))
                perks.Add(new DossierPerk
                {
                    Id = perk.Id,
                    Name = perk.Name,
                    Category = perk.Category,
                    Description = perk.Description,
                    Cost = perk.Cost,
                    Benefits = PerkDescriber.Benefits(perk),
                    Drawbacks = PerkDescriber.Drawbacks(perk),
                });
        }

        string? injuryRisk = null;
        if (InjuryModel.HasInjuryPerk(character, rules))
        {
            double hazard = InjuryModel.Hazard(character.Stat("durability"), PerkResolver.Resolve(character, rules));
            injuryRisk = hazard >= 0.30 ? "High" : hazard >= 0.16 ? "Moderate" : "Low";
        }

        // Availability — the SAME precedence as PlayerMortalityStatus.IsFit (deceased > season-ending
        // > suspended > fit). Off-mortality careers carry all-default fields, so they read Fit.
        var (availability, availabilityLabel) =
            deceased ? (AvailabilityStatus.Deceased, "Deceased")
            : seasonEndingInjury ? (AvailabilityStatus.SeasonOver, "Season over — recovering")
            : raceSuspensionRemaining > 0
                ? (AvailabilityStatus.Injured, $"Injured — out {raceSuspensionRemaining} race{(raceSuspensionRemaining == 1 ? "" : "s")}")
                : (AvailabilityStatus.Fit, "Fit");

        return new CharacterDossier
        {
            Name = character.Name,
            Age = age,
            Level = level,
            Xp = xp,
            XpIntoLevel = Math.Max(0, xp - cumulativeToLevel),
            XpForNextLevel = forNext,
            CpUnspent = CharacterProgress.AvailableCp(character, level, rules),
            Stats = stats,
            Perks = perks,
            InjuryRisk = injuryRisk,
            Availability = availability,
            AvailabilityLabel = availabilityLabel,
        };
    }
}

/// <summary>The driver's current availability for the next round — the display projection of the
/// accident/injury fold state (docs/dev/character-death-injury.md §6). Ordered by severity so the
/// default (0) is the healthy state.</summary>
public enum AvailabilityStatus
{
    /// <summary>Available to race normally.</summary>
    Fit = 0,

    /// <summary>Sitting out one or more rounds with a minor accident injury (heals as rounds pass).</summary>
    Injured = 1,

    /// <summary>Out for the rest of the season with a season-ending injury (returns next season).</summary>
    SeasonOver = 2,

    /// <summary>Killed in an accident — terminal; the career is over.</summary>
    Deceased = 3,
}

/// <summary>One stat row of the dossier: id, display label, current value, and whether it is a
/// talent stat (writes ratings) or a career meta-stat.</summary>
public sealed record DossierStat(string Id, string Label, double Value, bool Talent);

/// <summary>One perk row of the dossier: what it is, what it costs, and — in plain language — the
/// good things it does and the costs it carries.</summary>
public sealed record DossierPerk
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required int Cost { get; init; }
    public required IReadOnlyList<string> Benefits { get; init; }
    public required IReadOnlyList<string> Drawbacks { get; init; }
}

/// <summary>Display labels for the character stats — shared by the creation wizard and the dossier
/// so the two never drift.</summary>
public static class CharacterLabels
{
    private static readonly IReadOnlyDictionary<string, string> StatLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["pace"] = "Pace",
            ["oneLap"] = "One-lap pace",
            ["craft"] = "Craft",
            ["racecraft"] = "Racecraft",
            ["adaptability"] = "Adaptability",
            ["marketability"] = "Marketability",
            ["durability"] = "Durability",
        };

    public static string Stat(string id) => StatLabels.GetValueOrDefault(id, id);

    private static readonly IReadOnlyDictionary<string, string> CategoryLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["pace"] = "Pace",
            ["racecraft"] = "Racecraft",
            ["physical"] = "Physical",
            ["mental"] = "Mental",
            ["business"] = "Business",
            ["weather"] = "Weather",
            ["team"] = "Team",
            ["media"] = "Media",
            ["era"] = "Era-flavor",
        };

    /// <summary>The friendly title for a perk category id ("era" → "Era-flavor"); Title-cases any
    /// unmapped id so a new category never surfaces as a raw lowercase token.</summary>
    public static string Category(string id) =>
        CategoryLabels.TryGetValue(id, out var label)
            ? label
            : id.Length == 0 ? id : char.ToUpperInvariant(id[0]) + id[1..];

    private static readonly IReadOnlyDictionary<string, string> RatingLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["raceSkill"] = "race pace",
            ["qualifyingSkill"] = "one-lap pace",
            ["aggression"] = "overtaking",
            ["defending"] = "defending",
            ["consistency"] = "consistency",
            ["avoidanceOfMistakes"] = "composure",
            ["startReactions"] = "starts",
            ["wetSkill"] = "wet-weather pace",
            ["tyreManagement"] = "tyre management",
            ["stamina"] = "stamina",
            ["fuelManagement"] = "fuel saving",
        };

    /// <summary>The friendly name for a <c>PackDriverRatings</c> field the character UI surfaces
    /// (the stat→rating "advanced" numbers, the One-Trick specialism picker).</summary>
    public static string Rating(string field) => RatingLabels.GetValueOrDefault(field, field);
}
