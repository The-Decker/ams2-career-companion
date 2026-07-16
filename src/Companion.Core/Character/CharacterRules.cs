using System.Text.Json;
using System.Text.Json.Serialization;
using Companion.Core.Json;

namespace Companion.Core.Character;

/// <summary>
/// The parsed driver-character rules (<c>data/rules/perks.json</c>) — the app-shipped, user-editable
/// data that prices character creation and progression. This is a PURE model + parser (Core owns no
/// I/O): the ViewModels layer reads the file and calls <see cref="Parse"/>, exactly like the aging
/// curves and team archetypes. Nothing here folds a round or scores a result; the perk effects
/// become a <see cref="PlayerPerkModifiers"/> patch onto deterministic sim inputs only when a
/// character career is actually simulated (docs/dev/character-system.md §6-7).
///
/// The file is hand-authored and carries <c>$comment</c>/<c>$auditNote</c>/<c>note</c> annotations
/// and (potentially) comments and trailing commas, so the parser is tolerant of both; unknown
/// properties are ignored, which is what keeps the annotations harmless.
/// </summary>
public sealed record CharacterRules
{
    public required int Version { get; init; }

    public required CharacterPointsRules CharacterPoints { get; init; }

    public required StatsRules Stats { get; init; }

    public required LevelRules Levels { get; init; }

    public required CreationRules Creation { get; init; }

    public required IReadOnlyList<Perk> Perks { get; init; }

    /// <summary>The additive in-career skill-tree layout. Absent in older rules files => no tree.</summary>
    public SkillTreeRules SkillTree { get; init; } = SkillTreeRules.Empty;

    /// <summary>The additive milestone-respec policy. Absent in older rules files => respec disabled.</summary>
    public RespecRules Respec { get; init; } = new();

    /// <summary>The tunable accident bands + safety-offset scales (character death &amp; injury §3.4),
    /// or null when perks.json ships no <c>accident</c> block — the fold then falls back to
    /// <see cref="AccidentModel.DefaultRules"/>. Optional so every pre-feature fixture parses unchanged.</summary>
    public AccidentRules? Accident { get; init; }

    private Dictionary<string, Perk>? _perksById;

    /// <summary>Perk lookup by id (case-sensitive, matching the snake_case save-format keys).
    /// Throws <see cref="KeyNotFoundException"/> for an unknown id — a career referencing a perk
    /// the rules no longer define is a data error the caller must surface, not silently drop.</summary>
    public Perk PerkById(string id) =>
        (_perksById ??= Perks.ToDictionary(p => p.Id, StringComparer.Ordinal))[id];

    public bool TryGetPerk(string id, out Perk perk) =>
        (_perksById ??= Perks.ToDictionary(p => p.Id, StringComparer.Ordinal)).TryGetValue(id, out perk!);

    private static readonly JsonSerializerOptions ParseOptions = new(CoreJson.Options)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static CharacterRules Parse(string json)
    {
        var rules = JsonSerializer.Deserialize<CharacterRules>(json, ParseOptions)
            ?? throw new JsonException("perks.json parsed to null.");
        rules.Validate();
        return rules;
    }

    /// <summary>Fails fast on structural problems the sim would otherwise hit mid-career: missing
    /// blocks, an archetype referencing a perk id that does not exist, a non-increasing XP curve.
    /// The full balance audit (no strictly-dominant/trap perk, per-lever caps) lives in the CI test
    /// over the data — this is the load-time integrity gate only.</summary>
    public void Validate()
    {
        if (Perks.Count == 0)
            throw new JsonException("perks.json defines no perks.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var perk in Perks)
        {
            if (!ids.Add(perk.Id))
                throw new JsonException($"Duplicate perk id '{perk.Id}' in perks.json.");
        }

        foreach (var archetype in Creation.Archetypes)
        {
            foreach (string perkId in archetype.PerkIds)
            {
                if (!ids.Contains(perkId))
                    throw new JsonException(
                        $"Archetype '{archetype.Id}' references unknown perk id '{perkId}'.");
            }
        }

        if (Levels.XpCurve.Growth <= 1.0)
            throw new JsonException("levels.xpCurve.growth must exceed 1.0 for a rising curve.");
        if (Levels.XpCurve.BaseXpToLevel2 <= 0)
            throw new JsonException("levels.xpCurve.baseXpToLevel2 must be positive.");
        if (Levels.XpCurve.MaxLevel < 2)
            throw new JsonException("levels.xpCurve.maxLevel must be at least 2.");

        ValidateSkillTree(ids);

        if (Accident is { } accident)
            ValidateAccidentBands(accident);
    }

    private void ValidateSkillTree(IReadOnlySet<string> perkIds)
    {
        var branchIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string branch in SkillTree.BranchOrder)
        {
            if (!branchIds.Add(branch))
                throw new JsonException($"skillTree.branchOrder repeats branch '{branch}'.");
        }
        foreach (string meta in SkillTree.MetaBranches)
        {
            if (!branchIds.Contains(meta))
                throw new JsonException($"skillTree.metaBranches references unknown branch '{meta}'.");
        }

        foreach (var perk in Perks)
        {
            if (perk.Tier is < 1 or > 4)
                throw new JsonException($"Perk '{perk.Id}' tier must be between 1 and 4.");
            if (perk.UnlockLevel < 1)
                throw new JsonException($"Perk '{perk.Id}' unlockLevel must be positive.");
            if (branchIds.Count > 0 && !branchIds.Contains(perk.EffectiveBranch))
                throw new JsonException($"Perk '{perk.Id}' references unknown branch '{perk.EffectiveBranch}'.");
            foreach (string requiredId in perk.Requires)
            {
                if (!perkIds.Contains(requiredId))
                    throw new JsonException($"Perk '{perk.Id}' requires unknown perk '{requiredId}'.");
                if (PerkById(requiredId).Tier >= perk.Tier)
                    throw new JsonException(
                        $"Perk '{perk.Id}' tier must exceed required perk '{requiredId}' tier.");
            }
        }

        var visit = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var perk in Perks)
            VisitPerk(perk, visit);

        var allNodeIds = new HashSet<string>(perkIds, StringComparer.Ordinal);
        var knownStats = Stats.TalentStats.Select(s => s.Id)
            .Concat(Stats.MetaStats.Select(s => s.Id))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var node in SkillTree.StatNodes)
        {
            if (!allNodeIds.Add(node.Id))
                throw new JsonException($"Duplicate skill-tree node id '{node.Id}'.");
            if (!knownStats.Contains(node.Stat))
                throw new JsonException($"Stat node '{node.Id}' references unknown stat '{node.Stat}'.");
            if (node.Tier is < 1 or > 4 || node.UnlockLevel < 1 || node.Cost <= 0)
                throw new JsonException(
                    $"Stat node '{node.Id}' needs tier 1..4, positive unlockLevel, and positive cost.");
            if (branchIds.Count > 0 && !branchIds.Contains(node.Branch))
                throw new JsonException($"Stat node '{node.Id}' references unknown branch '{node.Branch}'.");
        }
        foreach (var node in SkillTree.StatNodes)
        {
            foreach (string requiredId in node.Requires)
            {
                if (!allNodeIds.Contains(requiredId))
                    throw new JsonException($"Stat node '{node.Id}' requires unknown node '{requiredId}'.");
                var required = SkillTree.StatNodes.FirstOrDefault(n =>
                    string.Equals(n.Id, requiredId, StringComparison.Ordinal));
                if (required is not null && required.Tier >= node.Tier)
                    throw new JsonException(
                        $"Stat node '{node.Id}' tier must exceed required node '{requiredId}' tier.");
            }
        }
    }

    private void VisitPerk(Perk perk, IDictionary<string, int> visit)
    {
        if (visit.TryGetValue(perk.Id, out int state))
        {
            if (state == 1)
                throw new JsonException($"Perk dependency cycle reaches '{perk.Id}'.");
            return;
        }
        visit[perk.Id] = 1;
        foreach (string requiredId in perk.Requires)
            VisitPerk(PerkById(requiredId), visit);
        visit[perk.Id] = 2;
    }

    /// <summary>Each severity's bands must be non-empty, have non-decreasing UpTo bounds, and the last
    /// band must cover the top of the d500 range (500) — otherwise a high roll would resolve to nothing.</summary>
    private static void ValidateAccidentBands(AccidentRules accident)
    {
        foreach (var (name, bands) in new (string, IReadOnlyList<AccidentBand>)[]
                 {
                     ("light", accident.Light),
                     ("medium", accident.Medium),
                     ("heavy", accident.Heavy),
                 })
        {
            if (bands.Count == 0)
                throw new JsonException($"accident.{name} defines no bands.");
            int previous = 0;
            foreach (var band in bands)
            {
                if (band.UpTo < previous)
                    throw new JsonException($"accident.{name} bands must have non-decreasing upTo bounds.");
                previous = band.UpTo;
                // A hand-authored typo in the outcome (e.g. "deathh", "SeasonEnding") would otherwise
                // silently resolve to "none" (AccidentModel.ParseKind's fallback), nullifying a fatal
                // band — so reject an unknown outcome at load, like the archetype→perk-id gate above.
                if (band.Outcome is not ("none" or "minorInjury" or "seasonEnding" or "death"))
                    throw new JsonException(
                        $"accident.{name} band has unknown outcome '{band.Outcome}' " +
                        "(expected none | minorInjury | seasonEnding | death).");
                if (string.Equals(band.Outcome, "minorInjury", StringComparison.Ordinal) && band.MissRaces < 1)
                    throw new JsonException(
                        $"accident.{name} minorInjury band must miss at least one race (missRaces >= 1).");
            }
            if (bands[^1].UpTo < 500)
                throw new JsonException($"accident.{name}'s last band must cover the full d500 range (upTo >= 500).");
        }
    }
}

public sealed record CharacterPointsRules
{
    public required int CreationBudget { get; init; }
    public int MinBudgetAfterSpend { get; init; }
    public int MaxRefundHeadroom { get; init; }

    /// <summary>The most perks a character may carry at creation — an archetype is the identity
    /// (its signature perks) and creation should add only a few more. Null (file omits it) = no
    /// count limit (only the CP budget binds). The CP budget alone can't cap the count because a
    /// drawback-funded build refunds its way to many cheap perks; this is the hard ceiling.</summary>
    public int? MaxPerks { get; init; }

    /// <summary>The most total talent (sum of the seven stats) a driver may carry at creation.
    /// Redistribution below it is free; the cap is what stops a max-everything build. Defaults to
    /// 4.2 (neutral is 3.5) when the file omits it.</summary>
    public double StatSumCap { get; init; } = 4.2;

    /// <summary>The inclusive net-spend window a valid creation build must land in:
    /// [minBudgetAfterSpend, creationBudget + maxRefundHeadroom].</summary>
    public int MaxNetSpend => CreationBudget + MaxRefundHeadroom;
}

public sealed record StatsRules
{
    public required IReadOnlyList<TalentStat> TalentStats { get; init; }
    public required IReadOnlyList<MetaStat> MetaStats { get; init; }
}

/// <summary>A talent stat (0..1) that writes one or two player-seat <c>PackDriverRatings</c> fields
/// via <c>writtenRating = clamp(writeBase + writeSpan*stat + perkDeltas, 0, 1)</c>.</summary>
public sealed record TalentStat
{
    public required string Id { get; init; }
    public required IReadOnlyList<string> MapsTo { get; init; }
    public double WriteBase { get; init; } = 0.35;
    public double WriteSpan { get; init; } = 0.55;
    public IReadOnlyList<double>? CreationRange { get; init; }

    /// <summary>The rating value this stat writes, before perk deltas and the final 0..1 clamp.</summary>
    public double WrittenRating(double stat) => WriteBase + WriteSpan * stat;
}

/// <summary>A career meta-stat (marketability / durability) with no <c>PackDriverRatings</c> analog.</summary>
public sealed record MetaStat
{
    public required string Id { get; init; }
    public IReadOnlyList<double>? Range { get; init; }
    public double Default { get; init; } = 0.5;
}

public sealed record LevelRules
{
    public required XpCurve XpCurve { get; init; }
    public required XpSources XpSources { get; init; }
    public required LevelGrants LevelGrants { get; init; }

    /// <summary>Era key to maximum reachable level. Empty keeps the global XP-curve cap.</summary>
    public IReadOnlyDictionary<string, JsonElement> SoftCapByEra { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    public int LevelForTotalXp(long totalXp, int year, bool useEraSoftCap)
    {
        int level = XpCurve.LevelForTotalXp(totalXp);
        int? cap = useEraSoftCap ? SoftCapForYear(year) : null;
        return cap is { } value ? Math.Min(level, value) : level;
    }

    public int? SoftCapForYear(int year)
    {
        string key = year switch
        {
            <= 1969 => "golden-age",
            <= 1979 => "seventies",
            <= 1989 => "eighties",
            <= 1999 => "nineties",
            <= 2013 => "two-thousands",
            _ => "hybrid",
        };
        return SoftCapByEra.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
    }
}

public sealed record XpCurve
{
    public double BaseXpToLevel2 { get; init; } = 100;
    public double Growth { get; init; } = 1.35;
    public int MaxLevel { get; init; } = 30;

    /// <summary>XP required to advance FROM level n-1 TO level n:
    /// <c>round(baseXpToLevel2 * growth^(n-2))</c> (so level 2 costs exactly baseXpToLevel2).</summary>
    public long XpForLevel(int level)
    {
        if (level < 2)
            return 0;
        return (long)Math.Round(BaseXpToLevel2 * Math.Pow(Growth, level - 2));
    }

    /// <summary>The level a total accumulated XP corresponds to (1-based, capped at maxLevel).</summary>
    public int LevelForTotalXp(long totalXp)
    {
        int level = 1;
        long cumulative = 0;
        for (int n = 2; n <= MaxLevel; n++)
        {
            cumulative += XpForLevel(n);
            if (totalXp < cumulative)
                break;
            level = n;
        }
        return level;
    }
}

public sealed record XpSources
{
    public required PerRoundXp PerRound { get; init; }
    public required PerSeasonXp PerSeason { get; init; }
}

public sealed record PerRoundXp
{
    public double FinishVsExpectedPerPlace { get; init; } = 6;
    public double FinishVsExpectedFloor { get; init; } = -30;
    public double FinishVsExpectedCap { get; init; } = 60;
    public double Win { get; init; } = 40;
    public double Podium { get; init; } = 20;
    public double Points { get; init; } = 10;
    public double BeatTeammate { get; init; } = 8;
    public double DnfDriverError { get; init; } = -15;
    public double DnfMechanical { get; init; }
}

public sealed record PerSeasonXp
{
    public double Championship1 { get; init; } = 300;
    public double ChampionshipTop3 { get; init; } = 150;
    public double ChampionshipTop10 { get; init; } = 60;
    public double SeasonCompleted { get; init; } = 40;
}

public sealed record LevelGrants
{
    public int CharacterPointsPerLevel { get; init; } = 2;
    public double StatStepValue { get; init; } = 0.05;
    public int StatStepCpCost { get; init; } = 1;
    public double StatCapPerRating { get; init; } = 0.99;
    public int MilestoneEveryLevels { get; init; }
    public string? MilestoneGrant { get; init; }
}

public sealed record CreationRules
{
    public required IReadOnlyList<Archetype> Archetypes { get; init; }
}

/// <summary>A pre-spent, in-budget creation template: a full stat spread + 2-3 signature perks.</summary>
public sealed record Archetype
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public required IReadOnlyDictionary<string, double> StartStats { get; init; }
    public required IReadOnlyDictionary<string, double> StartMeta { get; init; }
    public required IReadOnlyList<string> PerkIds { get; init; }
}

/// <summary>One character perk: a benefit and a drawback expressed as machine-readable
/// <see cref="Effects"/> on named sim levers, with a net CP <see cref="Cost"/>.</summary>
public sealed record Perk
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public int Cost { get; init; }
    public string Description { get; init; } = "";

    /// <summary>Additive skill-tree metadata; absent values preserve the historical flat-list behavior.</summary>
    public int Tier { get; init; } = 1;
    public IReadOnlyList<string> Requires { get; init; } = [];
    public int UnlockLevel { get; init; } = 1;
    public string? Branch { get; init; }
    public string EffectiveBranch => Branch is { Length: > 0 } branch ? branch : Category;

    /// <summary>The registered PCG32 stream a randomness effect names, or "none" for a fully
    /// deterministic perk.</summary>
    public string Stream { get; init; } = "none";

    public required IReadOnlyList<PerkEffect> Effects { get; init; }
}

public sealed record SkillTreeRules
{
    public static SkillTreeRules Empty { get; } = new();

    public IReadOnlyList<string> BranchOrder { get; init; } = [];
    public IReadOnlyList<string> MetaBranches { get; init; } = [];
    public IReadOnlyList<StatNodeRule> StatNodes { get; init; } = [];

    public StatNodeRule? TryGetStatNode(string id) =>
        StatNodes.FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.Ordinal));
}

public sealed record StatNodeRule
{
    public required string Id { get; init; }
    public required string Stat { get; init; }
    public string? Name { get; init; }
    public required string Branch { get; init; }
    public int Tier { get; init; } = 1;
    public int UnlockLevel { get; init; } = 1;
    public int Cost { get; init; } = 1;
    public IReadOnlyList<string> Requires { get; init; } = [];
}

public sealed record RespecRules
{
    public bool PerksLockedAtCreation { get; init; }
    public bool StatPointsBankable { get; init; }
    public bool StatPointsLockedOnSpend { get; init; }
    public int RespecTokenGrantsPerMilestone { get; init; }
    public string RespecTokenScope { get; init; } = "";
    public bool RespecForbidsCreationBudgetChange { get; init; }
}

/// <summary>
/// The boundary an effect changes for the human player. Expectation effects alter the benchmark or
/// staged driver identity, Career effects alter Companion progression/evaluation, and Car effects
/// physically alter the player car's AMS2 weight/power/drag scalars.
/// </summary>
public enum CharacterEffectClass
{
    Expectation = 0,
    Career = 1,
    Car = 2,
}

/// <summary>One machine-readable effect of a perk on a named sim lever. Benefit and drawback are
/// both effects, so <c>Σ cpEquivalent</c> is the net cost and the audit is arithmetic.</summary>
public sealed record PerkEffect
{
    /// <summary>"benefit" | "drawback".</summary>
    public required string Kind { get; init; }

    /// <summary>The lever (statDelta, carScalar, opiRetention, …) — see character-system.md §7.2.</summary>
    public required string Lever { get; init; }

    /// <summary>
    /// Optional authored boundary classification. Older rules omit this field; projections then
    /// classify <c>statDelta</c> as Expectation, <c>carScalar</c> as Car, and every other existing
    /// lever as Career. Null stays omitted on serialization so legacy rule shapes round-trip.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterEffectClass? Classification { get; init; }

    /// <summary>Lever-specific: the rating field, scalar axis, or sub-target this effect moves.
    /// Null for single-target levers (marketability, paceAnchorAlpha).</summary>
    public string? Target { get; init; }

    public double Magnitude { get; init; }

    /// <summary>Null = always-live; otherwise a round condition (wetRound, longRace, …) that gates
    /// when the effect applies.</summary>
    public string? Condition { get; init; }

    public double CpEquivalent { get; init; }

    /// <summary>The authored human note for this effect, if any — a fallback description when the
    /// UI describer has no friendlier phrase for the lever.</summary>
    public string? Note { get; init; }
}
