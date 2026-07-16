namespace Companion.Core.Character;

/// <summary>
/// Pure progression-v2 graph projection. It reads only the complete profile, persisted campaign
/// progression facts supplied by the caller, and one validated catalog. It never buys a node and
/// never consumes RNG; Wave 4 will submit an ordered plan to a separate atomic write seam.
/// </summary>
public static class MasterySkillGraph
{
    private static readonly HashSet<string> MetaFamilies =
        ["physical", "business", "media"];

    public static SkillTreeSnapshot Build(
        CharacterProfile profile,
        int level,
        int availableSp,
        MasterySkillCatalog catalog,
        bool masteryCheckpointComplete)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(catalog);
        if (profile.ProgressionVersion != CharacterLevelProgression.Level300Version)
            throw new InvalidOperationException("The v2 mastery graph requires a progression-v2 profile.");
        if (availableSp is < 0 or > MasterySkillCatalog.SkillPointsMaximum)
            throw new InvalidOperationException("Available v2 Skill Points must stay between 0 and 499.");

        var acquiredSkills = profile.AcquiredSkillIds ?? [];
        var acquiredAttributes = profile.AcquiredAttributeNodeIds ?? [];
        var ownedSkills = acquiredSkills.ToHashSet(StringComparer.Ordinal);
        var ownedAttributes = acquiredAttributes.ToHashSet(StringComparer.Ordinal);
        if (ownedSkills.Count != acquiredSkills.Count)
            throw new InvalidOperationException("Profile repeats a persisted mastery skill id.");
        if (ownedAttributes.Count != acquiredAttributes.Count)
            throw new InvalidOperationException("Profile repeats a persisted attribute node id.");
        foreach (string id in ownedSkills)
            if (!catalog.TryGetSkill(id, out _))
                throw new InvalidOperationException($"Profile owns unknown mastery skill '{id}'.");
        foreach (string id in ownedAttributes)
            if (!catalog.TryGetAttributeNode(id, out _))
                throw new InvalidOperationException($"Profile owns unknown attribute node '{id}'.");
        ValidatePersistedSkillOwnership(
            ownedSkills, level, catalog, masteryCheckpointComplete);

        var branches = new List<SkillBranch>();
        foreach (string family in catalog.FamilyOrder)
        {
            var nodes = catalog.Skills
                .Where(skill => string.Equals(skill.Family, family, StringComparison.Ordinal))
                .OrderBy(skill => skill.Tier)
                .ThenBy(skill => skill.Order)
                .ThenBy(skill => skill.Id, StringComparer.Ordinal)
                .Select(skill => BuildSkill(
                    skill,
                    ownedSkills,
                    level,
                    availableSp,
                    catalog,
                    masteryCheckpointComplete))
                .ToList();

            foreach (var rail in catalog.AttributeRails.Where(rail =>
                         string.Equals(rail.Family, family, StringComparison.Ordinal)))
            {
                nodes.AddRange(BuildRail(
                    rail,
                    profile,
                    ownedAttributes,
                    level,
                    availableSp,
                    catalog));
            }

            branches.Add(new SkillBranch
            {
                Id = family,
                Name = CharacterLabels.Category(family),
                IsMeta = MetaFamilies.Contains(family),
                Nodes = nodes,
            });
        }

        return new SkillTreeSnapshot { Branches = branches };
    }

    private static SkillNode BuildSkill(
        MasterySkillDefinition skill,
        IReadOnlySet<string> owned,
        int level,
        int availableSp,
        MasterySkillCatalog catalog,
        bool masteryCheckpointComplete)
    {
        bool isOwned = owned.Contains(skill.Id);
        var missing = skill.Requires.Where(required => !owned.Contains(required)).ToArray();
        bool exclusiveConflict = skill.ExclusiveGroup is not null && catalog.Skills.Any(other =>
            !string.Equals(other.Id, skill.Id, StringComparison.Ordinal) &&
            string.Equals(other.ExclusiveGroup, skill.ExclusiveGroup, StringComparison.Ordinal) &&
            owned.Contains(other.Id));
        bool masteryEligible = exclusiveConflict &&
                               level >= catalog.MasteryOverrideLevel &&
                               masteryCheckpointComplete;
        var (state, reason) = SkillState(
            isOwned,
            level,
            skill.UnlockLevel,
            skill.Cost,
            availableSp,
            missing,
            exclusiveConflict,
            masteryEligible,
            catalog.MasteryOverrideLevel,
            masteryCheckpointComplete,
            catalog);
        var effects = skill.Effects.Select(DescribeEffect).ToArray();

        return new SkillNode
        {
            Id = skill.Id,
            Name = skill.Name,
            Description = skill.Description,
            Kind = "mastery",
            Cost = skill.Cost,
            Tier = skill.Tier,
            UnlockLevel = skill.UnlockLevel,
            Requires = skill.Requires,
            Order = skill.Order,
            IconKey = skill.IconKey,
            ExclusiveGroup = skill.ExclusiveGroup,
            IsMasteryOverride = state == SkillNodeState.Mastery,
            Effects = effects,
            Benefits = skill.Benefits,
            Drawbacks = skill.Drawbacks,
            State = state,
            LockReason = reason,
        };
    }

    private static IEnumerable<SkillNode> BuildRail(
        MasteryAttributeRailDefinition rail,
        CharacterProfile profile,
        IReadOnlySet<string> owned,
        int level,
        int availableSp,
        MasterySkillCatalog catalog)
    {
        double baseline = BaselineStat(profile, rail.Stat);
        if (!double.IsFinite(baseline) || baseline is < 0.0 or > 0.99)
            throw new InvalidOperationException(
                $"Profile creation baseline '{rail.Stat}' value {baseline} is outside the v2 rail range.");
        int usefulSteps = Math.Max(0, checked((int)Math.Ceiling(
            (rail.CapValue - baseline) / rail.StepValue - 1e-9)));
        if (usefulSteps > rail.StepCount)
            throw new InvalidOperationException(
                $"Profile creation baseline '{rail.Stat}' is below the authored v2 rail minimum.");
        var railNodes = catalog.AttributeNodes
            .Where(node => string.Equals(node.RailId, rail.Id, StringComparison.Ordinal))
            .OrderBy(node => node.Order)
            .ToArray();

        int ownedPrefix = 0;
        while (ownedPrefix < railNodes.Length && owned.Contains(railNodes[ownedPrefix].Id))
            ownedPrefix++;
        if (railNodes.Skip(ownedPrefix).Any(node => owned.Contains(node.Id)))
            throw new InvalidOperationException($"Profile attribute rail '{rail.Id}' has non-sequential ownership.");
        if (ownedPrefix > usefulSteps)
            throw new InvalidOperationException($"Profile attribute rail '{rail.Id}' owns nodes beyond its 0.99 cap.");
        if (railNodes.Take(ownedPrefix).Any(node => level < node.UnlockLevel))
            throw new InvalidOperationException(
                $"Profile attribute rail '{rail.Id}' owns a node above the current level gate.");

        foreach (var node in railNodes)
        {
            bool isOwned = owned.Contains(node.Id);
            bool previousOwned = node.Requires.Count == 0 || owned.Contains(node.Requires[0]);
            SkillNodeState state;
            string reason;
            if (isOwned)
            {
                state = SkillNodeState.Owned;
                reason = "";
            }
            else if (node.Order > usefulSteps)
            {
                state = SkillNodeState.Locked;
                reason = $"{rail.Name} already reaches {rail.CapValue:0.00}";
            }
            else if (level < node.UnlockLevel)
            {
                state = SkillNodeState.Locked;
                reason = $"Reach level {node.UnlockLevel}";
            }
            else if (!previousOwned)
            {
                state = SkillNodeState.Locked;
                reason = $"Requires: {catalog.GetAttributeNode(node.Requires[0]).Name}";
            }
            else if (availableSp < node.Cost)
            {
                state = SkillNodeState.Locked;
                reason = $"Costs {node.Cost} SP";
            }
            else
            {
                state = SkillNodeState.Unlockable;
                reason = "";
            }

            CharacterEffectClass classification = profile.CreationBaseline!.Stats.ContainsKey(rail.Stat)
                ? CharacterEffectClass.Expectation
                : CharacterEffectClass.Career;
            CharacterEffectLine effect = DescribeAttributeEffect(rail, node, classification);
            string benefit = effect.Text;
            yield return new SkillNode
            {
                Id = node.Id,
                Name = node.Name,
                Description = benefit,
                Kind = "attribute",
                Cost = node.Cost,
                Tier = node.Tier,
                UnlockLevel = node.UnlockLevel,
                Requires = node.Requires,
                Order = node.Order,
                IconKey = node.IconKey,
                RailId = node.RailId,
                RailName = rail.Name,
                AttributeStatId = rail.Stat,
                AttributeValueAfter = Math.Min(node.CapValue, baseline + node.Order * node.StepValue),
                Effects = [effect],
                Benefits = [benefit],
                Drawbacks = ["Spends 1 SP from the campaign-gated mastery pool."],
                State = state,
                LockReason = reason,
            };
        }
    }

    private static (SkillNodeState State, string Reason) SkillState(
        bool owned,
        int level,
        int unlockLevel,
        int cost,
        int availableSp,
        IReadOnlyList<string> missing,
        bool exclusiveConflict,
        bool masteryEligible,
        int masteryOverrideLevel,
        bool checkpointComplete,
        MasterySkillCatalog catalog)
    {
        if (owned)
            return (SkillNodeState.Owned, "");
        if (level < unlockLevel)
            return (SkillNodeState.Locked, $"Reach level {unlockLevel}");
        if (missing.Count > 0)
            return (SkillNodeState.Locked,
                "Requires: " + string.Join(", ", missing.Select(id => catalog.GetSkill(id).Name)));
        if (exclusiveConflict && !masteryEligible)
        {
            if (level < masteryOverrideLevel)
                return (SkillNodeState.Locked, $"Second capstone mastery requires level {masteryOverrideLevel}");
            if (!checkpointComplete)
                return (SkillNodeState.Locked, "Complete the campaign mastery checkpoint");
        }
        if (availableSp < cost)
            return (SkillNodeState.Locked, $"Costs {cost} SP");
        return masteryEligible
            ? (SkillNodeState.Mastery, "")
            : (SkillNodeState.Unlockable, "");
    }

    private static double BaselineStat(CharacterProfile profile, string stat)
    {
        var baseline = profile.CreationBaseline
            ?? throw new InvalidOperationException("A progression-v2 profile requires its creation baseline.");
        if (baseline.Stats.TryGetValue(stat, out double talent))
            return talent;
        if (baseline.Meta.TryGetValue(stat, out double meta))
            return meta;
        throw new InvalidOperationException($"Profile creation baseline has no '{stat}' attribute.");
    }

    /// <summary>Converts one validated mastery effect into the same classified, display-ready
    /// boundary line used by the dossier and the new-career catalog preview.</summary>
    public static CharacterEffectLine DescribeEffect(MasterySkillEffect effect)
    {
        string subject = effect.Lever switch
        {
            "statDelta" => CharacterLabels.Rating(effect.Target ?? "rating"),
            "carScalar" => $"{Title(effect.Target)} scalar",
            "xpRate" => $"{Title(effect.Target)} round XP",
            "reputationRate" => $"{Title(effect.Target)} reputation",
            "offerWeight" => $"{Title(effect.Target)} offer weight",
            "roundXpFloor" => $"{Title(effect.Target)} round-XP floor",
            "portablePayBudget" => "Portable pay budget",
            "reputationFloorTier" => "Reputation-floor tier relaxation",
            "opiErrorBlame" => "Error-blame scale",
            "opiErrorFloorBlend" => "Error-floor blend",
            "opiGainSide" => "Gain-side OPI movement",
            "opiRetention" => "OPI retention",
            "paceAnchorAlpha" => "Pace-anchor alpha",
            "injuryDurability" => "Injury durability",
            "injuryBase" => "Base injury risk",
            "salaryAsk" => "Salary ask",
            "salaryOffer" => "Salary offer",
            "ageRisk" => "Age risk",
            "declineAcceleration" => "Decline acceleration",
            "peakAgeShift" => "Peak-age shift",
            "statSoftCap" => "Stat soft cap",
            _ => Title(effect.Lever),
        };
        string movement = effect.Operation switch
        {
            MasteryEffectOperation.Add => effect.Magnitude.ToString("+0.###;-0.###", System.Globalization.CultureInfo.InvariantCulture),
            MasteryEffectOperation.Multiply => ((effect.Magnitude - 1.0) * 100.0)
                .ToString("+0.#;-0.#", System.Globalization.CultureInfo.InvariantCulture) + "%",
            _ => throw new InvalidOperationException("Validated mastery effect has no operation."),
        };
        return PerkDescriber.CreateLine(
            effect.Kind,
            effect.Classification!.Value,
            string.IsNullOrWhiteSpace(effect.Note) ? $"{subject} {movement}" : effect.Note,
            effect.Condition);
    }

    /// <summary>Builds the canonical display line for one authored attribute-rail step. Both the
    /// live dossier graph and the creation-time read-only preview use this seam so their wording,
    /// cap and boundary classification cannot drift apart.</summary>
    public static CharacterEffectLine DescribeAttributeEffect(
        MasteryAttributeRailDefinition rail,
        MasteryAttributeNodeDefinition node,
        CharacterEffectClass classification)
    {
        ArgumentNullException.ThrowIfNull(rail);
        ArgumentNullException.ThrowIfNull(node);
        if (!string.Equals(node.RailId, rail.Id, StringComparison.Ordinal))
            throw new ArgumentException(
                $"Attribute node '{node.Id}' does not belong to rail '{rail.Id}'.",
                nameof(node));

        string benefit = $"+{node.StepValue:0.00} {rail.Name} (up to {node.CapValue:0.00})";
        return PerkDescriber.CreateLine("benefit", classification, benefit);
    }

    private static string Title(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Effect";
        var chars = new List<char>(value.Length + 4) { char.ToUpperInvariant(value[0]) };
        foreach (char ch in value.Skip(1))
        {
            if (char.IsUpper(ch))
                chars.Add(' ');
            chars.Add(ch);
        }
        return new string(chars.ToArray());
    }

    private static void ValidatePersistedSkillOwnership(
        IReadOnlySet<string> owned,
        int level,
        MasterySkillCatalog catalog,
        bool masteryCheckpointComplete)
    {
        foreach (string id in owned)
        {
            var skill = catalog.GetSkill(id);
            if (level < skill.UnlockLevel)
                throw new InvalidOperationException(
                    $"Owned mastery skill '{id}' requires level {skill.UnlockLevel}.");
            var missing = skill.Requires.Where(required => !owned.Contains(required)).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException(
                    $"Owned mastery skill '{id}' is missing prerequisite '{missing[0]}'.");
        }

        foreach (var group in catalog.Skills
                     .Where(skill => skill.ExclusiveGroup is not null && owned.Contains(skill.Id))
                     .GroupBy(skill => skill.ExclusiveGroup, StringComparer.Ordinal))
        {
            if (group.Count() <= 1)
                continue;
            if (group.Count() > 2 || level < catalog.MasteryOverrideLevel || !masteryCheckpointComplete)
            {
                throw new InvalidOperationException(
                    $"Owned capstones in '{group.Key}' do not satisfy the persisted mastery override.");
            }
        }
    }
}
