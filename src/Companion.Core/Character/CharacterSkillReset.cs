using Companion.Core.Career;

namespace Companion.Core.Character;

/// <summary>
/// The complete deterministic INPUT for one progression-v2 committed-tree reset. Prior ownership
/// is persisted in canonical node-id order so replay can reject catalog drift or a rewritten refund.
/// </summary>
public sealed record CharacterSkillResetInput
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public int ProgressionVersion { get; init; } = CharacterLevelProgression.Level300Version;
    public int PolicyVersion { get; init; }
    public long XpCost { get; init; }
    public int RefundedSkillPoints { get; init; }
    public required IReadOnlyList<CharacterSkillPlanEntry> PriorAcquisitions { get; init; }
}

/// <summary>
/// A pure quote for the destructive committed-tree action. Ordinary ineligibility is represented
/// as a blocked preview; corrupt or unsupported persisted state still fails closed.
/// </summary>
public sealed record SkillResetPreview
{
    public long LifetimeXp { get; init; }
    public long AvailableResetXp { get; init; }
    public long Cost { get; init; }
    public long AvailableResetXpAfter { get; init; }
    public int SkillPointsRefunded { get; init; }
    public int SkillPointsAfterReset { get; init; }
    public int AcquisitionCount { get; init; }
    public bool CanApply { get; init; }
    public required string BlockReason { get; init; }
    public CharacterSkillResetInput? Input { get; init; }
    public PlayerCareerState? ProjectedState { get; init; }
}

/// <summary>
/// Pure preparation and application for progression-v2's XP-funded full skill reset. Lifetime XP
/// and level never move; only the character's reset-spend counters and committed mastery build do.
/// </summary>
public static class CharacterSkillReset
{
    public static SkillResetPreview Preview(
        PlayerCareerState state,
        CharacterRules rules,
        MasterySkillCatalog catalog)
    {
        var context = ValidateContext(state, rules, catalog);
        string reason = context.Acquisitions.Count == 0
            ? "There is no committed skill tree to reset."
            : context.AvailableResetXp < context.Cost
                ? $"The reset costs {context.Cost} XP but only {context.AvailableResetXp} reset XP is available."
                : "";

        if (reason.Length != 0)
        {
            return new SkillResetPreview
            {
                LifetimeXp = state.Xp,
                AvailableResetXp = context.AvailableResetXp,
                Cost = context.Cost,
                AvailableResetXpAfter = Math.Max(0L, context.AvailableResetXp - context.Cost),
                SkillPointsRefunded = context.Profile.SkillPointsSpent,
                SkillPointsAfterReset = context.SkillPointsAfterReset,
                AcquisitionCount = context.Acquisitions.Count,
                CanApply = false,
                BlockReason = reason,
            };
        }

        var input = CreateInput(context, catalog);
        var projected = Project(state, context, input);
        return new SkillResetPreview
        {
            LifetimeXp = state.Xp,
            AvailableResetXp = context.AvailableResetXp,
            Cost = context.Cost,
            AvailableResetXpAfter = checked(context.AvailableResetXp - context.Cost),
            SkillPointsRefunded = context.Profile.SkillPointsSpent,
            SkillPointsAfterReset = context.SkillPointsAfterReset,
            AcquisitionCount = context.Acquisitions.Count,
            CanApply = true,
            BlockReason = "",
            Input = input,
            ProjectedState = projected,
        };
    }

    /// <summary>Returns the exact payload ready to journal, or rejects an ordinary blocked quote.</summary>
    public static CharacterSkillResetInput Prepare(
        PlayerCareerState state,
        CharacterRules rules,
        MasterySkillCatalog catalog)
    {
        SkillResetPreview preview = Preview(state, rules, catalog);
        if (!preview.CanApply)
            throw new InvalidOperationException(preview.BlockReason);
        return preview.Input!;
    }

    /// <summary>
    /// Revalidates a persisted payload against the pre-reset state and applies it atomically. The
    /// supplied records and their collections are never mutated, including on validation failure.
    /// </summary>
    public static PlayerCareerState Apply(
        PlayerCareerState state,
        CharacterSkillResetInput input,
        CharacterRules rules,
        MasterySkillCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(input);
        var context = ValidateContext(state, rules, catalog);
        ValidateInput(input, context, catalog);
        if (context.Acquisitions.Count == 0)
            throw new InvalidOperationException("There is no committed skill tree to reset.");
        if (context.AvailableResetXp < context.Cost)
        {
            throw new InvalidOperationException(
                $"The reset costs {context.Cost} XP but only {context.AvailableResetXp} reset XP is available.");
        }
        return Project(state, context, input);
    }

    private static ResetContext ValidateContext(
        PlayerCareerState state,
        CharacterRules rules,
        MasterySkillCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(catalog);
        var profile = state.Character
            ?? throw new InvalidOperationException("A committed skill reset requires a character.");
        if (profile.ProgressionVersion != CharacterLevelProgression.Level300Version)
            throw new InvalidOperationException("Committed skill resets require a progression-v2 profile.");
        if (catalog.ProgressionVersion != profile.ProgressionVersion)
            throw new InvalidOperationException("The mastery catalog and character progression versions differ.");
        if (state.Level is < 1 or > CharacterLevelProgression.Level300Max)
            throw new InvalidOperationException("A progression-v2 character level must be between 1 and 300.");
        if (state.Xp < 0)
            throw new InvalidOperationException("Lifetime XP cannot be negative.");
        int derivedLevel = CharacterLevelProgression.LevelForTotalXp(
            CharacterLevelProgression.Level300Version,
            state.Xp,
            year: 0,
            rules);
        if (state.Level != derivedLevel)
        {
            throw new InvalidOperationException(
                $"Persisted level {state.Level} does not match lifetime XP level {derivedLevel}.");
        }
        if (state.SeasonsCompleted < 0)
            throw new InvalidOperationException("Completed seasons cannot be negative.");
        if (profile.XpSpentOnResets < 0 || profile.XpSpentOnResets > state.Xp)
            throw new InvalidOperationException("XP spent on resets must stay within lifetime XP.");
        if (profile.SkillResetCount < 0)
            throw new InvalidOperationException("Skill reset count cannot be negative.");
        if (profile.SkillResetCount == int.MaxValue)
            throw new InvalidOperationException("Skill reset count cannot be incremented safely.");
        if ((profile.SkillResetCount == 0) != (profile.XpSpentOnResets == 0))
            throw new InvalidOperationException("Skill reset count and XP-spend provenance disagree.");
        if (profile.CpUnspent != 0 || profile.CpSpent != 0 ||
            (profile.UnlockedSkillNodeIds?.Count ?? 0) != 0)
        {
            throw new InvalidOperationException("A progression-v2 profile cannot carry legacy CP ownership.");
        }
        if (string.IsNullOrWhiteSpace(profile.RacingDnaId) || profile.RacingDnaVersion <= 0)
            throw new InvalidOperationException("A progression-v2 profile requires its immutable Racing DNA identity.");
        if (profile.RacingDnaChoice is not null && string.IsNullOrWhiteSpace(profile.RacingDnaChoice))
            throw new InvalidOperationException("A Racing DNA context choice cannot be blank.");

        CharacterCreationBaseline baseline = ValidateBaseline(profile, rules);
        CharacterSkillPlanEntry[] acquisitions = CanonicalAcquisitions(profile, catalog);
        if (profile.SkillPointsSpent != acquisitions.Sum(entry => entry.Cost))
        {
            throw new InvalidOperationException(
                "Persisted Skill Points spent do not match canonical committed ownership.");
        }
        if (profile.SkillPointsSpent is < 0 or > MasterySkillCatalog.SkillPointsMaximum)
            throw new InvalidOperationException("Persisted Skill Points spent are outside 0..499.");

        var campaign = state.CampaignProgressionPlan
            ?? throw new InvalidOperationException(
                "A committed skill reset requires its pinned campaign progression plan.");
        campaign.Validate();
        if (!CareerExperienceModes.IsBoundedCampaign(state.ExperienceMode) ||
            !string.Equals(state.ExperienceMode, campaign.Mode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The committed skill-reset experience mode and campaign plan do not match.");
        }
        bool masteryCheckpointComplete = state.SeasonsCompleted >= campaign.MasterySeason;
        var balance = CharacterProgressionV2Math.SkillPoints(
            state.Level,
            state.SeasonsCompleted,
            campaign.MasterySeason,
            profile.SkillPointsSpent);
        if (profile.SkillPointsSpent > balance.Earned)
            throw new InvalidOperationException("Committed Skill Point spend exceeds the campaign-earned pool.");

        // This validates unknown/duplicate ownership, prerequisite closure, level gates, capstone
        // override provenance, rail sequence, and baseline rail bounds.
        _ = MasterySkillGraph.Build(
            profile,
            state.Level,
            availableSp: 0,
            catalog,
            masteryCheckpointComplete);
        ValidateCurrentProfile(profile, baseline, catalog);

        long availableResetXp = checked(state.Xp - profile.XpSpentOnResets);
        long cost = ResetCost(state.Level, profile.SkillResetCount, rules, catalog.SkillResetPolicy);
        return new ResetContext(
            profile,
            baseline,
            acquisitions,
            availableResetXp,
            cost,
            balance.Earned);
    }

    private static CharacterCreationBaseline ValidateBaseline(
        CharacterProfile profile,
        CharacterRules rules)
    {
        if (profile.Stats is null || profile.PerkIds is null)
            throw new InvalidOperationException("A progression-v2 profile requires complete stats and traits.");
        var baseline = profile.CreationBaseline
            ?? throw new InvalidOperationException("A committed skill reset requires a creation baseline.");
        if (baseline.Stats is null || baseline.Meta is null || baseline.TraitIds is null)
            throw new InvalidOperationException("The creation baseline requires complete stats, meta, and traits.");

        ValidateBaselineMap(
            baseline.Stats,
            rules.Stats.TalentStats.Select(stat => stat.Id),
            "talent");
        ValidateBaselineMap(
            baseline.Meta,
            rules.Stats.MetaStats.Select(stat => stat.Id),
            "meta");
        if (baseline.Stats.Keys.Any(baseline.Meta.ContainsKey))
            throw new InvalidOperationException("Creation-baseline talent and meta stats overlap.");
        if (baseline.TraitIds.Any(string.IsNullOrWhiteSpace) ||
            baseline.TraitIds.Distinct(StringComparer.Ordinal).Count() != baseline.TraitIds.Count)
        {
            throw new InvalidOperationException("Creation-baseline trait ids must be nonblank and unique.");
        }
        foreach (string traitId in baseline.TraitIds)
        {
            if (!rules.TryGetPerk(traitId, out _))
                throw new InvalidOperationException($"Creation baseline references unknown trait '{traitId}'.");
        }
        if (!(profile.CreationPerkIds ?? []).SequenceEqual(baseline.TraitIds) ||
            !profile.PerkIds.SequenceEqual(baseline.TraitIds))
        {
            throw new InvalidOperationException("Current traits do not match immutable creation-baseline traits.");
        }
        if (!string.Equals(profile.ChosenFlavor, baseline.ChosenFlavor, StringComparison.Ordinal))
            throw new InvalidOperationException("Current flavor does not match the creation baseline.");
        return baseline;
    }

    private static void ValidateBaselineMap(
        IReadOnlyDictionary<string, double> values,
        IEnumerable<string> requiredIds,
        string label)
    {
        string[] required = requiredIds.ToArray();
        var requiredSet = required.ToHashSet(StringComparer.Ordinal);
        if (requiredSet.Count != required.Length || values.Count != required.Length ||
            required.Any(id => !values.ContainsKey(id)))
        {
            throw new InvalidOperationException(
                $"Creation baseline must contain exactly the configured {label} stats.");
        }
        foreach (var (id, value) in values)
        {
            if (!requiredSet.Contains(id) || !double.IsFinite(value) || value is < 0.0 or > 0.99)
            {
                throw new InvalidOperationException(
                    $"Creation-baseline {label} stat '{id}' must be finite and within 0.00-0.99.");
            }
        }
    }

    private static CharacterSkillPlanEntry[] CanonicalAcquisitions(
        CharacterProfile profile,
        MasterySkillCatalog catalog)
    {
        var result = new List<CharacterSkillPlanEntry>();
        var allIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? id in profile.AcquiredSkillIds ?? [])
        {
            if (string.IsNullOrWhiteSpace(id) || !allIds.Add(id))
                throw new InvalidOperationException("Committed mastery-skill ids must be nonblank and unique.");
            if (!catalog.TryGetSkill(id, out var skill))
                throw new InvalidOperationException($"Profile owns unknown mastery skill '{id}'.");
            result.Add(new CharacterSkillPlanEntry
            {
                NodeId = id,
                Kind = CharacterSkillPlanEntry.MasteryKind,
                Cost = skill.Cost,
            });
        }
        foreach (string? id in profile.AcquiredAttributeNodeIds ?? [])
        {
            if (string.IsNullOrWhiteSpace(id) || !allIds.Add(id))
                throw new InvalidOperationException("Committed attribute-node ids must be nonblank and unique.");
            if (!catalog.TryGetAttributeNode(id, out var attribute))
                throw new InvalidOperationException($"Profile owns unknown attribute node '{id}'.");
            result.Add(new CharacterSkillPlanEntry
            {
                NodeId = id,
                Kind = CharacterSkillPlanEntry.AttributeKind,
                Cost = attribute.Cost,
            });
        }
        return result.OrderBy(entry => entry.NodeId, StringComparer.Ordinal).ToArray();
    }

    private static void ValidateCurrentProfile(
        CharacterProfile profile,
        CharacterCreationBaseline baseline,
        MasterySkillCatalog catalog)
    {
        var expected = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (id, value) in baseline.Stats)
            expected.Add(id, value);
        foreach (var (id, value) in baseline.Meta)
            expected.Add(id, value);

        var ownedAttributes = (profile.AcquiredAttributeNodeIds ?? []).ToHashSet(StringComparer.Ordinal);
        foreach (var rail in catalog.AttributeRails)
        {
            double baselineValue = expected[rail.Stat];
            int ownedCount = catalog.AttributeNodes.Count(node =>
                string.Equals(node.RailId, rail.Id, StringComparison.Ordinal) &&
                ownedAttributes.Contains(node.Id));
            expected[rail.Stat] = Math.Min(
                rail.CapValue,
                baselineValue + ownedCount * rail.StepValue);
        }

        if (profile.Stats.Count != expected.Count || expected.Any(pair =>
                !profile.Stats.TryGetValue(pair.Key, out double actual) || actual != pair.Value))
        {
            throw new InvalidOperationException(
                "Current attributes do not match the creation baseline and committed attribute rails.");
        }
    }

    private static long ResetCost(
        int level,
        int resetCount,
        CharacterRules rules,
        MasterySkillResetPolicy policy)
    {
        long cumulative = CharacterLevelProgression.CumulativeXpToLevel(
            CharacterLevelProgression.Level300Version,
            level,
            rules);
        long scaledNumerator = checked(cumulative * policy.CumulativeXpNumerator);
        long minimumNumerator = checked(policy.MinimumBaseXp * policy.CumulativeXpDenominator);
        long selectedNumerator = Math.Max(minimumNumerator, scaledNumerator);
        long roundUnitNumerator = checked(policy.RoundUpXp * policy.CumulativeXpDenominator);
        long roundUnits = selectedNumerator / roundUnitNumerator;
        if (selectedNumerator % roundUnitNumerator != 0)
            roundUnits = checked(roundUnits + 1);
        long baseCost = checked(roundUnits * policy.RoundUpXp);
        long multiplier = checked(1L + checked((long)resetCount * policy.RepeatCostIncrement));
        return checked(baseCost * multiplier);
    }

    private static CharacterSkillResetInput CreateInput(
        ResetContext context,
        MasterySkillCatalog catalog) =>
        new()
        {
            PolicyVersion = catalog.SkillResetPolicy.Version,
            XpCost = context.Cost,
            RefundedSkillPoints = context.Profile.SkillPointsSpent,
            PriorAcquisitions = context.Acquisitions,
        };

    private static void ValidateInput(
        CharacterSkillResetInput input,
        ResetContext context,
        MasterySkillCatalog catalog)
    {
        if (input.Version != CharacterSkillResetInput.CurrentVersion)
            throw new NotSupportedException($"Character skill-reset input version {input.Version} is not supported.");
        if (input.ProgressionVersion != CharacterLevelProgression.Level300Version)
            throw new InvalidOperationException("Character skill-reset progression version is invalid.");
        if (input.PolicyVersion != catalog.SkillResetPolicy.Version)
            throw new InvalidOperationException("Character skill-reset policy version does not match the catalog.");
        if (input.XpCost != context.Cost)
            throw new InvalidOperationException("Character skill-reset XP cost does not match the authoritative quote.");
        if (input.RefundedSkillPoints != context.Profile.SkillPointsSpent)
            throw new InvalidOperationException("Character skill-reset SP refund does not match committed ownership.");
        if (input.PriorAcquisitions is null ||
            input.PriorAcquisitions.Count != context.Acquisitions.Count)
        {
            throw new InvalidOperationException(
                "Character skill-reset prior acquisitions do not match committed ownership.");
        }
        for (int index = 0; index < context.Acquisitions.Count; index++)
        {
            CharacterSkillPlanEntry? actual = input.PriorAcquisitions[index];
            CharacterSkillPlanEntry expected = context.Acquisitions[index];
            if (actual is null ||
                !string.Equals(actual.NodeId, expected.NodeId, StringComparison.Ordinal) ||
                !string.Equals(actual.Kind, expected.Kind, StringComparison.Ordinal) ||
                actual.Cost != expected.Cost)
            {
                throw new InvalidOperationException(
                    "Character skill-reset prior acquisitions are not the canonical ownership snapshot.");
            }
        }
    }

    private static PlayerCareerState Project(
        PlayerCareerState state,
        ResetContext context,
        CharacterSkillResetInput input)
    {
        var stats = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (id, value) in context.Baseline.Stats)
            stats.Add(id, value);
        foreach (var (id, value) in context.Baseline.Meta)
            stats.Add(id, value);

        // DNA passives remain immutable catalog projections; they are deliberately not baked into
        // profile stats here. Reset restores only the persisted creation snapshot.
        CharacterProfile character = context.Profile with
        {
            Stats = stats,
            PerkIds = context.Baseline.TraitIds.ToArray(),
            CreationPerkIds = context.Baseline.TraitIds.ToArray(),
            ChosenFlavor = context.Baseline.ChosenFlavor,
            AcquiredSkillIds = null,
            AcquiredAttributeNodeIds = null,
            SkillPointsSpent = 0,
            XpSpentOnResets = checked(context.Profile.XpSpentOnResets + input.XpCost),
            SkillResetCount = checked(context.Profile.SkillResetCount + 1),
        };
        return state with { Character = character };
    }

    private sealed record ResetContext(
        CharacterProfile Profile,
        CharacterCreationBaseline Baseline,
        IReadOnlyList<CharacterSkillPlanEntry> Acquisitions,
        long AvailableResetXp,
        long Cost,
        int SkillPointsAfterReset);
}
