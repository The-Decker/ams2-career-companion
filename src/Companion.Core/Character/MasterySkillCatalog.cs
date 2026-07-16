using System.Text.Json;
using System.Text.Json.Serialization;
using Companion.Core.Json;

namespace Companion.Core.Character;

/// <summary>
/// Progression-v2's isolated mastery catalog. It deliberately does not extend
/// <see cref="CharacterRules.Perks"/>: the shipped 42 perks and 15 legacy stat nodes remain the
/// immutable v0/v1 graph, while v2 ownership resolves only through this versioned namespace.
/// </summary>
public sealed class MasterySkillCatalog
{
    public const int CurrentSchemaVersion = 1;
    public const int SupportedProgressionVersion = CharacterLevelProgression.Level300Version;
    public const int SkillCount = 90;
    public const int SkillPointsMaximum = 499;
    public const int DraftSkillCost = 280;
    public const int DraftAttributeCost = 119;
    public const int DraftCompleteCost = DraftSkillCost + DraftAttributeCost;

    public static readonly IReadOnlyList<string> RequiredFamilies =
        ["pace", "racecraft", "physical", "mental", "business", "weather", "team", "media", "era"];

    public static readonly IReadOnlyDictionary<int, int> RequiredTierUnlockLevels =
        new Dictionary<int, int> { [1] = 1, [2] = 30, [3] = 90, [4] = 165, [5] = 240 };

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<double>> RequiredAggregateClamps =
        new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal)
        {
            ["carScalar"] = [0.90, 1.10],
            ["opiRetention"] = [0.65, 0.90],
            ["paceAnchorAlpha"] = [0.15, 0.60],
            ["marketability"] = [0.0, 1.0],
            ["salaryMultiplier"] = [0.50, 1.75],
            ["ageRiskMultiplier"] = [0.25, 2.0],
            ["declineAcceleration"] = [0.10, 2.0],
            ["errorBlameScale"] = [0.70, 1.25],
            ["errorFloorBlend"] = [0.0, 0.60],
            ["roundXpMultiplier"] = [0.0, 1.40],
            ["reputationMultiplier"] = [0.60, 1.40],
            ["portablePayBudgetBonus"] = [0.0, 5.0],
        };

    private static readonly IReadOnlyList<int> RequiredRailUnlockLevels =
        [1, 1, 1, 30, 30, 30, 90, 90, 90, 90, 165, 165, 165, 165, 240, 240, 240];

    private static readonly HashSet<string> AllowedConditions =
    [
        "wetRound", "dryRound", "longRace", "shortRace", "ageBeforePeak", "ageAtOrAfterPeak",
        "eraTransition", "tierAtMost2", "tierAtLeast4", "worksTeam", "homeCountry", "foreignCountry",
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> LeverTargets =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["statDelta"] = Set(
                "raceSkill", "qualifyingSkill", "aggression", "defending", "avoidanceOfMistakes",
                "startReactions", "consistency", "tyreManagement", "wetSkill", "fuelManagement",
                "stamina", "chosenFlavor"),
            ["carScalar"] = Set("weight", "power", "drag"),
            ["xpRate"] = Set("all", "round", "finishVsExpected", "win", "podium", "mechanicalDnf", "midfield"),
            ["reputationRate"] = Set("round", "season", "signedRound"),
            ["offerWeight"] = Set("experience", "lowTier", "salaryAsk"),
            ["roundXpFloor"] = Set("round", "finishVsExpected"),
            ["marketability"] = EmptySet(),
            ["paceAnchorAlpha"] = EmptySet(),
            ["injuryDurability"] = EmptySet(),
            ["injuryBase"] = EmptySet(),
            ["salaryAsk"] = EmptySet(),
            ["salaryOffer"] = EmptySet(),
            ["portablePayBudget"] = EmptySet(),
            ["reputationFloorTier"] = EmptySet(),
            ["ageRisk"] = EmptySet(),
            ["opiRetention"] = EmptySet(),
            ["opiGainSide"] = EmptySet(),
            ["opiErrorBlame"] = EmptySet(),
            ["opiErrorFloorBlend"] = EmptySet(),
            ["declineAcceleration"] = EmptySet(),
            ["peakAgeShift"] = EmptySet(),
            ["statSoftCap"] = EmptySet(),
        };

    private readonly IReadOnlyDictionary<string, MasterySkillDefinition> _skillsById;
    private readonly IReadOnlyDictionary<string, MasteryAttributeNodeDefinition> _attributeNodesById;

    public int SchemaVersion { get; }
    public int ProgressionVersion { get; }
    public int MaximumSkillPoints { get; }
    public int MasteryOverrideLevel { get; }
    public IReadOnlyList<string> FamilyOrder { get; }
    public IReadOnlyDictionary<int, int> TierUnlockLevels { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<double>> AggregateClamps { get; }
    public MasteryCarAuditRules CarAudit { get; }
    public MasterySkillResetPolicy SkillResetPolicy { get; }
    public IReadOnlyList<MasterySkillDefinition> Skills { get; }
    public IReadOnlyList<MasteryAttributeRailDefinition> AttributeRails { get; }
    public IReadOnlyList<MasteryAttributeNodeDefinition> AttributeNodes { get; }
    public int TotalSkillCost { get; }
    public int OrdinaryMaximumSkillCost { get; }
    public int MaximumAttributeCost { get; }
    public int MaximumCompleteCost => checked(TotalSkillCost + MaximumAttributeCost);

    private MasterySkillCatalog(
        MasterySkillCatalogFile file,
        IReadOnlyDictionary<string, MasterySkillDefinition> skillsById,
        IReadOnlyList<MasteryAttributeNodeDefinition> attributeNodes)
    {
        SchemaVersion = file.SchemaVersion;
        ProgressionVersion = file.ProgressionVersion;
        MaximumSkillPoints = file.MaximumSkillPoints;
        MasteryOverrideLevel = file.MasteryOverrideLevel;
        FamilyOrder = file.FamilyOrder.ToArray();
        TierUnlockLevels = new Dictionary<int, int>(file.TierUnlockLevels);
        AggregateClamps = file.AggregateClamps.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<double>)pair.Value.ToArray(),
            StringComparer.Ordinal);
        CarAudit = file.CarAudit;
        SkillResetPolicy = file.SkillResetPolicy;
        Skills = file.Nodes.ToArray();
        AttributeRails = file.AttributeRails.ToArray();
        AttributeNodes = attributeNodes;
        _skillsById = skillsById;
        _attributeNodesById = attributeNodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        TotalSkillCost = checked(Skills.Sum(node => node.Cost));
        MaximumAttributeCost = checked(AttributeNodes.Sum(node => node.Cost));
        OrdinaryMaximumSkillCost = checked(TotalSkillCost - Skills
            .Where(node => node.ExclusiveGroup is not null)
            .GroupBy(node => node.ExclusiveGroup, StringComparer.Ordinal)
            .Sum(group => group.Sum(node => node.Cost) - group.Max(node => node.Cost)));
    }

    private static readonly JsonSerializerOptions ParseOptions = new(CoreJson.Options)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static MasterySkillCatalog Parse(
        string json,
        CharacterRules legacyRules,
        RacingDnaCatalog racingDna)
    {
        ArgumentNullException.ThrowIfNull(legacyRules);
        ArgumentNullException.ThrowIfNull(racingDna);
        var file = JsonSerializer.Deserialize<MasterySkillCatalogFile>(json, ParseOptions)
            ?? throw new JsonException("mastery-skills-v2.json parsed to null.");
        return Validate(file, legacyRules, racingDna);
    }

    public bool TryGetSkill(string id, out MasterySkillDefinition definition) =>
        _skillsById.TryGetValue(id, out definition!);

    public MasterySkillDefinition GetSkill(string id) =>
        TryGetSkill(id, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown progression-v2 mastery skill '{id}'.");

    public bool TryGetAttributeNode(string id, out MasteryAttributeNodeDefinition definition) =>
        _attributeNodesById.TryGetValue(id, out definition!);

    public MasteryAttributeNodeDefinition GetAttributeNode(string id) =>
        TryGetAttributeNode(id, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown progression-v2 attribute node '{id}'.");

    private static MasterySkillCatalog Validate(
        MasterySkillCatalogFile file,
        CharacterRules legacyRules,
        RacingDnaCatalog racingDna)
    {
        if (file.SchemaVersion != CurrentSchemaVersion)
            throw new JsonException($"Unsupported mastery-skill schema version {file.SchemaVersion}.");
        if (file.ProgressionVersion != SupportedProgressionVersion)
            throw new JsonException(
                $"Mastery catalog progression version {file.ProgressionVersion} is unsupported.");
        if (file.MaximumSkillPoints != SkillPointsMaximum)
            throw new JsonException($"Mastery catalog must expose exactly {SkillPointsMaximum} Skill Points.");
        if (file.MasteryOverrideLevel != 285)
            throw new JsonException("Mastery override level must be 285 for progression version 2.");
        if (file.FamilyOrder is null || !file.FamilyOrder.SequenceEqual(RequiredFamilies, StringComparer.Ordinal))
            throw new JsonException("Mastery familyOrder must contain the nine canonical families in order.");
        if (file.TierUnlockLevels is null || file.TierUnlockLevels.Count != RequiredTierUnlockLevels.Count ||
            RequiredTierUnlockLevels.Any(pair =>
                !file.TierUnlockLevels.TryGetValue(pair.Key, out int value) || value != pair.Value))
        {
            throw new JsonException("Mastery tierUnlockLevels must be 1/30/90/165/240.");
        }
        if (file.AggregateClamps is null || file.AggregateClamps.Count != RequiredAggregateClamps.Count ||
            RequiredAggregateClamps.Any(pair =>
                !file.AggregateClamps.TryGetValue(pair.Key, out var range) ||
                range is null || !range.SequenceEqual(pair.Value)))
        {
            throw new JsonException("Mastery aggregateClamps do not match the progression-v2 balance envelope.");
        }
        if (file.CarAudit is null ||
            file.CarAudit.ComposedAdvantageMinimum != -0.10 ||
            file.CarAudit.ComposedAdvantageMaximum != 0.05 ||
            file.CarAudit.TeamRootPathMaximum != 0.010 ||
            file.CarAudit.TeamCrossBranchMaximum != 0.015)
        {
            throw new JsonException("Mastery carAudit does not match the progression-v2 balance envelope.");
        }
        ValidateSkillResetPolicy(file.SkillResetPolicy);
        if (file.Nodes is null || file.Nodes.Count != SkillCount)
            throw new JsonException($"Progression-v2 must define exactly {SkillCount} mastery skills.");
        if (file.AttributeRails is null || file.AttributeRails.Count != 7)
            throw new JsonException("Progression-v2 must define exactly seven attribute rails.");

        var familyRank = RequiredFamilies
            .Select((family, index) => (family, index))
            .ToDictionary(pair => pair.family, pair => pair.index, StringComparer.Ordinal);
        file = file with
        {
            Nodes = file.Nodes
                .Select(node => node with
                {
                    Requires = node.Requires?.Order(StringComparer.Ordinal).ToArray()!,
                })
                .OrderBy(node => familyRank.GetValueOrDefault(node.Family, int.MaxValue))
                .ThenBy(node => node.Tier)
                .ThenBy(node => node.Order)
                .ThenBy(node => node.Id, StringComparer.Ordinal)
                .ToArray(),
            AttributeRails = file.AttributeRails
                .OrderBy(rail => familyRank.GetValueOrDefault(rail.Family, int.MaxValue))
                .ThenBy(rail => rail.Id, StringComparer.Ordinal)
                .ToArray(),
        };

        var legacyIds = legacyRules.Perks.Select(perk => perk.Id)
            .Concat(legacyRules.SkillTree.StatNodes.Select(node => node.Id))
            .ToHashSet(StringComparer.Ordinal);
        var ids = new HashSet<string>(legacyIds, StringComparer.Ordinal);
        var icons = new HashSet<string>(StringComparer.Ordinal);
        var positions = new HashSet<(string Family, int Order)>();
        var skillsById = new Dictionary<string, MasterySkillDefinition>(StringComparer.Ordinal);

        foreach (var node in file.Nodes)
        {
            ValidateNode(node, file, ids, icons, positions);
            skillsById.Add(node.Id, node);
        }

        foreach (string family in RequiredFamilies)
        {
            var familyNodes = file.Nodes.Where(node =>
                string.Equals(node.Family, family, StringComparison.Ordinal)).ToArray();
            if (familyNodes.Length != 10)
                throw new JsonException($"Mastery family '{family}' must contain exactly ten skills.");
            for (int tier = 1; tier <= 5; tier++)
            {
                if (familyNodes.Count(node => node.Tier == tier) != 2)
                    throw new JsonException($"Mastery family '{family}' must contain two tier-{tier} skills.");
            }
            var capstones = familyNodes.Where(node => node.Tier == 5).ToArray();
            string group = family + ".capstone";
            if (capstones.Any(node => !string.Equals(node.ExclusiveGroup, group, StringComparison.Ordinal)))
                throw new JsonException($"Both '{family}' capstones must use exclusiveGroup '{group}'.");
        }

        ValidateDependencies(file.Nodes, skillsById);
        ValidateCarBalance(file.Nodes, skillsById, file.CarAudit);

        var attributeNodes = ValidateRails(file, legacyRules, racingDna, ids, icons);
        var catalog = new MasterySkillCatalog(file, skillsById, attributeNodes);
        if (catalog.TotalSkillCost != DraftSkillCost)
            throw new JsonException($"Wave-1 mastery skills must cost exactly {DraftSkillCost} SP.");
        if (catalog.MaximumAttributeCost != DraftAttributeCost)
            throw new JsonException($"The seven v2 attribute rails must cost exactly {DraftAttributeCost} SP.");
        if (catalog.MaximumCompleteCost != DraftCompleteCost ||
            catalog.MaximumCompleteCost > catalog.MaximumSkillPoints)
        {
            throw new JsonException(
                $"Maximum legal v2 cost {catalog.MaximumCompleteCost} exceeds its authored budget.");
        }
        return catalog;
    }

    private static void ValidateSkillResetPolicy(MasterySkillResetPolicy? policy)
    {
        if (policy is null)
            throw new JsonException("Progression-v2 requires a skillResetPolicy.");
        if (policy.Version != MasterySkillResetPolicy.CurrentVersion)
        {
            throw new JsonException(
                $"Unsupported mastery skill-reset policy version {policy.Version}.");
        }
        if (policy.MinimumBaseXp <= 0)
            throw new JsonException("skillResetPolicy.minimumBaseXp must be positive.");
        if (policy.CumulativeXpNumerator <= 0 || policy.CumulativeXpDenominator <= 0)
            throw new JsonException("skillResetPolicy cumulative XP ratio must be positive.");
        if (policy.RoundUpXp <= 0)
            throw new JsonException("skillResetPolicy.roundUpXp must be positive.");
        if (policy.RepeatCostIncrement <= 0)
            throw new JsonException("skillResetPolicy.repeatCostIncrement must be positive.");
    }

    private static void ValidateNode(
        MasterySkillDefinition node,
        MasterySkillCatalogFile file,
        ISet<string> ids,
        ISet<string> icons,
        ISet<(string Family, int Order)> positions)
    {
        if (!IsSnakeCaseId(node.Id))
            throw new JsonException($"Mastery skill id '{node.Id}' is not stable snake_case.");
        if (!ids.Add(node.Id))
            throw new JsonException($"Duplicate or legacy-colliding mastery skill id '{node.Id}'.");
        if (node.IntroducedInProgressionVersion != SupportedProgressionVersion)
            throw new JsonException($"Mastery skill '{node.Id}' has the wrong introduced progression version.");
        if (string.IsNullOrWhiteSpace(node.Name) || string.IsNullOrWhiteSpace(node.Description))
            throw new JsonException($"Mastery skill '{node.Id}' needs a name and description.");
        if (!RequiredFamilies.Contains(node.Family, StringComparer.Ordinal))
            throw new JsonException($"Mastery skill '{node.Id}' has unknown family '{node.Family}'.");
        if (node.Tier is < 1 or > 5 || node.Order is < 1 or > 10 ||
            node.Order != ((node.Tier - 1) * 2) + ((node.Order - 1) % 2) + 1)
        {
            throw new JsonException($"Mastery skill '{node.Id}' has invalid tier/order placement.");
        }
        if (!positions.Add((node.Family, node.Order)))
            throw new JsonException($"Mastery family '{node.Family}' repeats order {node.Order}.");
        if (node.Cost <= 0)
            throw new JsonException($"Mastery skill '{node.Id}' must have a positive cost.");
        if (!file.TierUnlockLevels.TryGetValue(node.Tier, out int unlock) || node.UnlockLevel != unlock)
            throw new JsonException($"Mastery skill '{node.Id}' has the wrong tier unlock level.");
        if (!IsIconKey(node.IconKey) || !icons.Add(node.IconKey))
            throw new JsonException($"Mastery skill '{node.Id}' needs a unique lowercase iconKey.");
        if (node.Tier == 5)
        {
            if (!string.Equals(node.ExclusiveGroup, node.Family + ".capstone", StringComparison.Ordinal))
                throw new JsonException($"Mastery capstone '{node.Id}' has an invalid exclusiveGroup.");
        }
        else if (node.ExclusiveGroup is not null)
        {
            throw new JsonException($"Only tier-5 mastery skills may declare exclusiveGroup.");
        }
        if (node.Requires is null || node.Benefits is null || node.Drawbacks is null || node.Effects is null ||
            node.Benefits.Count == 0 || node.Drawbacks.Count == 0 || node.Effects.Count == 0 ||
            node.Benefits.Any(string.IsNullOrWhiteSpace) || node.Drawbacks.Any(string.IsNullOrWhiteSpace))
        {
            throw new JsonException($"Mastery skill '{node.Id}' needs prerequisites, benefits, drawbacks, and effects arrays.");
        }
        if (node.Tier == 1 && node.Requires.Count != 0)
            throw new JsonException($"Tier-1 mastery skill '{node.Id}' cannot have prerequisites.");
        if (node.Tier > 1 && node.Requires.Count == 0)
            throw new JsonException($"Mastery skill '{node.Id}' must name a lower-tier prerequisite.");
        if (node.Requires.Count != node.Requires.Distinct(StringComparer.Ordinal).Count())
            throw new JsonException($"Mastery skill '{node.Id}' repeats a prerequisite.");

        foreach (var effect in node.Effects)
            ValidateEffect(node.Id, effect);
    }

    private static void ValidateDependencies(
        IReadOnlyList<MasterySkillDefinition> nodes,
        IReadOnlyDictionary<string, MasterySkillDefinition> byId)
    {
        foreach (var node in nodes)
        {
            foreach (string requiredId in node.Requires)
            {
                if (!byId.TryGetValue(requiredId, out var required))
                    throw new JsonException($"Mastery skill '{node.Id}' requires unknown skill '{requiredId}'.");
                if (!string.Equals(required.Family, node.Family, StringComparison.Ordinal))
                    throw new JsonException($"Mastery skill '{node.Id}' has a cross-family prerequisite.");
                if (required.Tier >= node.Tier || required.Order >= node.Order)
                    throw new JsonException($"Mastery skill '{node.Id}' prerequisite '{requiredId}' is not earlier and lower-tier.");
            }
        }

        var visit = new Dictionary<string, byte>(StringComparer.Ordinal);
        foreach (var node in nodes)
            Visit(node, byId, visit);

        foreach (string family in RequiredFamilies)
        {
            var familyNodes = nodes.Where(node => node.Family == family).ToArray();
            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var capstone in familyNodes.Where(node => node.Tier == 5))
                AddClosure(capstone, byId, covered);
            if (covered.Count != 10 || familyNodes.Any(node => !covered.Contains(node.Id)))
                throw new JsonException($"The two '{family}' capstones must cover the full family graph.");
        }
    }

    private static void Visit(
        MasterySkillDefinition node,
        IReadOnlyDictionary<string, MasterySkillDefinition> byId,
        IDictionary<string, byte> visit)
    {
        if (visit.TryGetValue(node.Id, out byte state))
        {
            if (state == 1)
                throw new JsonException($"Mastery dependency cycle reaches '{node.Id}'.");
            return;
        }
        visit[node.Id] = 1;
        foreach (string requiredId in node.Requires)
            Visit(byId[requiredId], byId, visit);
        visit[node.Id] = 2;
    }

    private static void AddClosure(
        MasterySkillDefinition node,
        IReadOnlyDictionary<string, MasterySkillDefinition> byId,
        ISet<string> closure)
    {
        if (!closure.Add(node.Id))
            return;
        foreach (string requiredId in node.Requires)
            AddClosure(byId[requiredId], byId, closure);
    }

    private static IReadOnlyList<MasteryAttributeNodeDefinition> ValidateRails(
        MasterySkillCatalogFile file,
        CharacterRules rules,
        RacingDnaCatalog racingDna,
        ISet<string> allIds,
        ISet<string> icons)
    {
        var knownStats = rules.Stats.TalentStats.Select(stat => stat.Id)
            .Concat(rules.Stats.MetaStats.Select(stat => stat.Id))
            .ToHashSet(StringComparer.Ordinal);
        var railStats = new HashSet<string>(StringComparer.Ordinal);
        var railIds = new HashSet<string>(StringComparer.Ordinal);
        var prefixes = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new List<MasteryAttributeNodeDefinition>();

        foreach (var rail in file.AttributeRails)
        {
            if (!string.Equals(rail.Id, "attribute." + rail.Stat, StringComparison.Ordinal) ||
                !railIds.Add(rail.Id))
                throw new JsonException($"Attribute rail id '{rail.Id}' is invalid or duplicated.");
            if (!knownStats.Contains(rail.Stat) || !railStats.Add(rail.Stat))
                throw new JsonException($"Attribute rail '{rail.Id}' has an unknown or repeated stat '{rail.Stat}'.");
            if (!RequiredFamilies.Contains(rail.Family, StringComparer.Ordinal))
                throw new JsonException($"Attribute rail '{rail.Id}' has unknown family '{rail.Family}'.");
            if (string.IsNullOrWhiteSpace(rail.Name) || !IsIconKey(rail.IconKey) || !icons.Add(rail.IconKey))
                throw new JsonException($"Attribute rail '{rail.Id}' needs a name and unique lowercase iconKey.");
            if (!IsNodePrefix(rail.NodeIdPrefix) || !prefixes.Add(rail.NodeIdPrefix))
                throw new JsonException($"Attribute rail '{rail.Id}' has an invalid or repeated nodeIdPrefix.");
            if (rail.StepValue != 0.05 || rail.CapValue != 0.99 || rail.CostPerStep != 1 || rail.StepCount != 17)
                throw new JsonException($"Attribute rail '{rail.Id}' must use 17 x 0.05 steps to the 0.99 cap at 1 SP each.");
            if (rail.UnlockLevels is null || !rail.UnlockLevels.SequenceEqual(RequiredRailUnlockLevels))
                throw new JsonException($"Attribute rail '{rail.Id}' has the wrong progression gates.");

            IReadOnlyDictionary<string, IReadOnlyList<double>> ranges =
                racingDna.CreationBudget.TalentRanges.ContainsKey(rail.Stat)
                    ? racingDna.CreationBudget.TalentRanges
                    : racingDna.CreationBudget.MetaRanges;
            if (!ranges.TryGetValue(rail.Stat, out var range) || range.Count != 2)
                throw new JsonException($"Attribute rail '{rail.Id}' has no v2 creation range.");
            int requiredSteps = checked((int)Math.Ceiling((rail.CapValue - range[0]) / rail.StepValue - 1e-9));
            if (requiredSteps != rail.StepCount || range[1] > rail.CapValue)
                throw new JsonException($"Attribute rail '{rail.Id}' does not cover its exact creation range.");

            string? previous = null;
            for (int index = 1; index <= rail.StepCount; index++)
            {
                string id = rail.NodeIdPrefix + index.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                if (!allIds.Add(id))
                    throw new JsonException($"Attribute node id '{id}' collides with another progression node.");
                int unlockLevel = rail.UnlockLevels[index - 1];
                int tier = RequiredTierUnlockLevels.Single(pair => pair.Value == unlockLevel).Key;
                var node = new MasteryAttributeNodeDefinition
                {
                    Id = id,
                    RailId = rail.Id,
                    Stat = rail.Stat,
                    Family = rail.Family,
                    Name = $"Raise {rail.Name} {index}",
                    IconKey = rail.IconKey,
                    Order = index,
                    Tier = tier,
                    UnlockLevel = unlockLevel,
                    Cost = rail.CostPerStep,
                    StepValue = rail.StepValue,
                    CapValue = rail.CapValue,
                    Requires = previous is null ? [] : [previous],
                };
                nodes.Add(node);
                previous = id;
            }
        }

        if (!railStats.SetEquals(knownStats))
            throw new JsonException("Attribute rails must cover every one of the seven character attributes exactly once.");
        return nodes;
    }

    private static void ValidateEffect(string nodeId, MasterySkillEffect effect)
    {
        if (effect.Kind is not ("benefit" or "drawback"))
            throw new JsonException($"Mastery skill '{nodeId}' effect has invalid kind '{effect.Kind}'.");
        if (effect.Classification is null || effect.Operation is null)
            throw new JsonException($"Mastery skill '{nodeId}' effect needs classification and operation.");
        if (!Enum.IsDefined(effect.Classification.Value) || !Enum.IsDefined(effect.Operation.Value))
            throw new JsonException($"Mastery skill '{nodeId}' effect uses an unsupported enum value.");
        if (string.IsNullOrWhiteSpace(effect.Lever) ||
            !LeverTargets.TryGetValue(effect.Lever, out var allowedTargets))
            throw new JsonException($"Mastery skill '{nodeId}' uses unknown effect lever '{effect.Lever}'.");
        if (allowedTargets.Count == 0)
        {
            if (effect.Target is not null)
                throw new JsonException($"Mastery lever '{effect.Lever}' does not accept a target.");
        }
        else if (effect.Target is null || !allowedTargets.Contains(effect.Target))
        {
            throw new JsonException($"Mastery lever '{effect.Lever}' has invalid target '{effect.Target}'.");
        }
        if (effect.Condition is not null && !AllowedConditions.Contains(effect.Condition))
            throw new JsonException($"Mastery effect condition '{effect.Condition}' is not supported.");
        if (!double.IsFinite(effect.Magnitude) ||
            effect.Operation == MasteryEffectOperation.Add && effect.Magnitude == 0.0 ||
            effect.Operation == MasteryEffectOperation.Multiply && (effect.Magnitude < 0.0 || effect.Magnitude == 1.0))
        {
            throw new JsonException($"Mastery skill '{nodeId}' has an inert or non-finite effect magnitude.");
        }

        bool operationAllowed = effect.Lever switch
        {
            "statDelta" or "carScalar" or "marketability" or "paceAnchorAlpha" or
                "injuryDurability" or "injuryBase" or "opiRetention" or "opiErrorBlame" or
                "opiErrorFloorBlend" or "declineAcceleration" or "peakAgeShift" or
                "portablePayBudget" or "reputationFloorTier" or "statSoftCap" =>
                effect.Operation == MasteryEffectOperation.Add,
            "xpRate" or "reputationRate" or "salaryAsk" or "salaryOffer" or "ageRisk" or
                "opiGainSide" or "roundXpFloor" => effect.Operation == MasteryEffectOperation.Multiply,
            "offerWeight" => true,
            _ => false,
        };
        if (!operationAllowed)
            throw new JsonException($"Mastery lever '{effect.Lever}' uses an unsupported operation.");
        if (effect.Operation == MasteryEffectOperation.Add && Math.Abs(effect.Magnitude) > 5.0 ||
            effect.Operation == MasteryEffectOperation.Multiply && effect.Magnitude > 2.0 ||
            effect.Lever == "statDelta" && Math.Abs(effect.Magnitude) > 0.30 ||
            effect.Lever == "carScalar" && Math.Abs(effect.Magnitude) > 0.10)
        {
            throw new JsonException($"Mastery skill '{nodeId}' effect exceeds its authored magnitude envelope.");
        }
        if (effect.Lever == "carScalar" && effect.Condition is not null &&
            effect.Condition is not ("wetRound" or "dryRound" or "longRace" or "shortRace"))
        {
            throw new JsonException($"Mastery CAR effect condition '{effect.Condition}' is not stageable.");
        }
        CharacterEffectClass expected = effect.Lever switch
        {
            "statDelta" => CharacterEffectClass.Expectation,
            "carScalar" => CharacterEffectClass.Car,
            _ => CharacterEffectClass.Career,
        };
        if (effect.Classification != expected)
            throw new JsonException(
                $"Mastery skill '{nodeId}' lever '{effect.Lever}' must be classified {expected}.");
    }

    private static bool IsSnakeCaseId(string? value) =>
        value is { Length: > 0 } && value[0] is >= 'a' and <= 'z' &&
        value.All(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '_') &&
        !value.Contains("__", StringComparison.Ordinal) && !value.EndsWith('_');

    private static bool IsNodePrefix(string? value) =>
        value is { Length: > 1 } && value.EndsWith('_') && IsSnakeCaseId(value[..^1]);

    private static bool IsIconKey(string? value) =>
        value is { Length: > 0 } && value[0] is >= 'a' and <= 'z' &&
        value.All(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);
    private static HashSet<string> EmptySet() => new(StringComparer.Ordinal);

    private static void ValidateCarBalance(
        IReadOnlyList<MasterySkillDefinition> nodes,
        IReadOnlyDictionary<string, MasterySkillDefinition> byId,
        MasteryCarAuditRules audit)
    {
        var team = nodes.Where(node => node.Family == "team").ToArray();
        double crossBranch = team.SelectMany(node => node.Effects)
            .Where(effect => effect.Classification == CharacterEffectClass.Car && effect.Condition is null)
            .Sum(CarAdvantage);
        if (crossBranch > audit.TeamCrossBranchMaximum + 1e-12)
            throw new JsonException(
                $"Team's unconditional cross-branch CAR advantage exceeds +{audit.TeamCrossBranchMaximum:0.###}.");

        foreach (var capstone in team.Where(node => node.Tier == 5))
        {
            foreach (var path in RootPaths(capstone, byId))
            {
                double advantage = path.SelectMany(node => node.Effects)
                    .Where(effect => effect.Classification == CharacterEffectClass.Car && effect.Condition is null)
                    .Sum(CarAdvantage);
                if (advantage > audit.TeamRootPathMaximum + 1e-12)
                    throw new JsonException(
                        $"Team CAR path to '{capstone.Id}' exceeds its +0.010 advantage envelope.");
            }
        }
    }

    private static IEnumerable<IReadOnlyList<MasterySkillDefinition>> RootPaths(
        MasterySkillDefinition node,
        IReadOnlyDictionary<string, MasterySkillDefinition> byId)
    {
        if (node.Requires.Count == 0)
        {
            yield return [node];
            yield break;
        }
        foreach (string requiredId in node.Requires)
        foreach (var prefix in RootPaths(byId[requiredId], byId))
            yield return prefix.Append(node).ToArray();
    }

    private static double CarAdvantage(MasterySkillEffect effect) => effect.Target switch
    {
        "power" => effect.Magnitude,
        "weight" or "drag" => -effect.Magnitude,
        _ => 0.0,
    };
}

internal sealed record MasterySkillCatalogFile
{
    public int SchemaVersion { get; init; }
    public int ProgressionVersion { get; init; }
    public int MaximumSkillPoints { get; init; }
    public int MasteryOverrideLevel { get; init; }
    public required IReadOnlyList<string> FamilyOrder { get; init; }
    public required IReadOnlyDictionary<int, int> TierUnlockLevels { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<double>> AggregateClamps { get; init; }
    public required MasteryCarAuditRules CarAudit { get; init; }
    public required MasterySkillResetPolicy SkillResetPolicy { get; init; }
    public required IReadOnlyList<MasterySkillDefinition> Nodes { get; init; }
    public required IReadOnlyList<MasteryAttributeRailDefinition> AttributeRails { get; init; }
}

/// <summary>
/// Integer-only, versioned balance data for progression-v2 committed-tree resets. The formula is
/// interpreted by <see cref="CharacterSkillReset"/>; changing a value requires a policy-version
/// change so a persisted reset can never be silently repriced.
/// </summary>
public sealed record MasterySkillResetPolicy
{
    public const int CurrentVersion = 1;

    public int Version { get; init; }
    public long MinimumBaseXp { get; init; }
    public long CumulativeXpNumerator { get; init; }
    public long CumulativeXpDenominator { get; init; }
    public long RoundUpXp { get; init; }
    public int RepeatCostIncrement { get; init; }
}

public sealed record MasteryCarAuditRules
{
    public double ComposedAdvantageMinimum { get; init; }
    public double ComposedAdvantageMaximum { get; init; }
    public double TeamRootPathMaximum { get; init; }
    public double TeamCrossBranchMaximum { get; init; }
}

public sealed record MasterySkillDefinition
{
    public required string Id { get; init; }
    public int IntroducedInProgressionVersion { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Family { get; init; }
    public int Tier { get; init; }
    public int Order { get; init; }
    public required string IconKey { get; init; }
    public int Cost { get; init; }
    public int UnlockLevel { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExclusiveGroup { get; init; }
    public required IReadOnlyList<string> Requires { get; init; }
    public required IReadOnlyList<string> Benefits { get; init; }
    public required IReadOnlyList<string> Drawbacks { get; init; }
    public required IReadOnlyList<MasterySkillEffect> Effects { get; init; }
}

public sealed record MasterySkillEffect
{
    public required string Kind { get; init; }
    public CharacterEffectClass? Classification { get; init; }
    public required string Lever { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Target { get; init; }
    public MasteryEffectOperation? Operation { get; init; }
    public required double Magnitude { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Condition { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }
}

public enum MasteryEffectOperation
{
    Add = 0,
    Multiply = 1,
}

public sealed record MasteryAttributeRailDefinition
{
    public required string Id { get; init; }
    public required string Stat { get; init; }
    public required string Family { get; init; }
    public required string Name { get; init; }
    public required string IconKey { get; init; }
    public required string NodeIdPrefix { get; init; }
    public double StepValue { get; init; }
    public double CapValue { get; init; }
    public int CostPerStep { get; init; }
    public int StepCount { get; init; }
    public required IReadOnlyList<int> UnlockLevels { get; init; }
}

public sealed record MasteryAttributeNodeDefinition
{
    public required string Id { get; init; }
    public required string RailId { get; init; }
    public required string Stat { get; init; }
    public required string Family { get; init; }
    public required string Name { get; init; }
    public required string IconKey { get; init; }
    public int Order { get; init; }
    public int Tier { get; init; }
    public int UnlockLevel { get; init; }
    public int Cost { get; init; }
    public double StepValue { get; init; }
    public double CapValue { get; init; }
    public required IReadOnlyList<string> Requires { get; init; }
}
