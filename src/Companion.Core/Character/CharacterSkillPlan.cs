using System.Text.Json.Serialization;

namespace Companion.Core.Character;

/// <summary>
/// One canonical acquisition inside a progression-v2 skill plan. Kind and cost are persisted with
/// the stable node id so replay can reject a catalog/input mismatch instead of silently repricing a
/// historical choice.
/// </summary>
public sealed record CharacterSkillPlanEntry
{
    public const string MasteryKind = "mastery";
    public const string AttributeKind = "attribute";

    public required string NodeId { get; init; }
    public required string Kind { get; init; }
    public required int Cost { get; init; }
}

/// <summary>
/// The complete ordered INPUT payload for one atomic progression-v2 acquisition. A caller journals
/// this object only after <see cref="MasterySkillPlan.Prepare"/> succeeds; replay passes the exact
/// persisted payload through <see cref="MasterySkillPlan.Apply"/> and therefore performs no RNG or
/// catalog-driven normalization.
/// </summary>
public sealed record CharacterSkillPlanInput
{
    public const int CurrentVersion = 1;
    public const int CurrentEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion;

    public int Version { get; init; } = CurrentVersion;
    public int ProgressionVersion { get; init; } = CharacterLevelProgression.Level300Version;

    /// <summary>
    /// The mastery-effect semantics activated by this plan. A missing/default 0 identifies plans
    /// persisted before effects were live; replay accepts those without changing the profile gate.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int EffectsVersion { get; init; }

    public required IReadOnlyList<CharacterSkillPlanEntry> Entries { get; init; }
    public int TotalCost { get; init; }
}

/// <summary>A pure authoritative preview of one unconfirmed ordered plan.</summary>
public sealed record SkillPlanPreview
{
    public required CharacterSkillPlanInput Input { get; init; }
    public required SkillTreeSnapshot ProjectedTree { get; init; }
    public required int SkillPointsAfterPlan { get; init; }
}

/// <summary>The persisted campaign gates needed to validate a v2 plan without I/O.</summary>
public readonly record struct MasteryProgressionFacts(
    int Level,
    int AvailableSkillPoints,
    bool MasteryCheckpointComplete);

/// <summary>
/// Pure, deterministic preparation and application for an ordered v2 mastery/attribute plan. The
/// engine creates new collections at every accepted step and returns only after every entry has
/// passed, so a rejected later node cannot partially mutate the supplied profile.
/// </summary>
public static class MasterySkillPlan
{
    /// <summary>
    /// Applies confirmed plan envelopes in journal order while carrying the remaining SP balance
    /// between envelopes. The operation is still atomic from the caller's perspective: profiles
    /// are immutable and no projected value is returned unless every plan validates.
    /// </summary>
    public static CharacterProfile ApplyAll(
        CharacterProfile profile,
        IReadOnlyList<CharacterSkillPlanInput> persistedInputs,
        MasteryProgressionFacts progressionFacts,
        MasterySkillCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(persistedInputs);
        ArgumentNullException.ThrowIfNull(catalog);

        CharacterProfile projected = profile;
        int available = progressionFacts.AvailableSkillPoints;
        foreach (var input in persistedInputs)
        {
            projected = Apply(
                projected,
                input,
                progressionFacts with { AvailableSkillPoints = available },
                catalog);
            available = checked(available - input.TotalCost);
        }
        return projected;
    }

    /// <summary>
    /// Derives the canonical kind/cost payload from an ordered UI selection and validates the whole
    /// projected build. The returned object is ready to persist as one versioned INPUT row.
    /// </summary>
    public static CharacterSkillPlanInput Prepare(
        CharacterProfile profile,
        IReadOnlyList<string> orderedNodeIds,
        MasteryProgressionFacts progressionFacts,
        MasterySkillCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(orderedNodeIds);
        ArgumentNullException.ThrowIfNull(catalog);
        if (orderedNodeIds.Count == 0)
            throw new InvalidOperationException("A skill plan must contain at least one acquisition.");

        var entries = new CharacterSkillPlanEntry[orderedNodeIds.Count];
        int totalCost = 0;
        for (int index = 0; index < orderedNodeIds.Count; index++)
        {
            string nodeId = orderedNodeIds[index]
                ?? throw new InvalidOperationException("A skill plan contains a null node id.");
            var canonical = ResolveCanonical(nodeId, catalog);
            entries[index] = new CharacterSkillPlanEntry
            {
                NodeId = nodeId,
                Kind = canonical.Kind,
                Cost = canonical.Cost,
            };
            totalCost = checked(totalCost + canonical.Cost);
        }

        var input = new CharacterSkillPlanInput
        {
            EffectsVersion = CharacterSkillPlanInput.CurrentEffectsVersion,
            Entries = entries,
            TotalCost = totalCost,
        };

        // Authoritative sequential validation is shared with replay. Discarding the projected
        // profile keeps preparation pure while guaranteeing that Persist never sees an invalid plan.
        _ = Apply(profile, input, progressionFacts, catalog);
        return input;
    }

    /// <summary>
    /// Applies one already-persisted plan after verifying its version, canonical payload, campaign
    /// gates, acquisition order, affordability, and profile provenance. No caller-owned collection
    /// is modified when validation fails.
    /// </summary>
    public static CharacterProfile Apply(
        CharacterProfile profile,
        CharacterSkillPlanInput persistedInput,
        MasteryProgressionFacts progressionFacts,
        MasterySkillCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(persistedInput);
        ArgumentNullException.ThrowIfNull(catalog);

        ValidateContext(profile, progressionFacts, catalog);
        ValidateEnvelope(persistedInput);
        CharacterSkillPlanEntry[] entries = persistedInput.Entries.ToArray();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        int canonicalTotal = 0;
        foreach (var entry in entries)
        {
            if (entry is null)
                throw new InvalidOperationException("A skill plan contains a null entry.");
            if (string.IsNullOrWhiteSpace(entry.NodeId))
                throw new InvalidOperationException("A skill plan contains a blank node id.");
            if (!seen.Add(entry.NodeId))
                throw new InvalidOperationException($"Skill plan repeats node '{entry.NodeId}'.");

            var canonical = ResolveCanonical(entry.NodeId, catalog);
            if (!string.Equals(entry.Kind, canonical.Kind, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Skill plan node '{entry.NodeId}' kind '{entry.Kind}' does not match canonical kind '{canonical.Kind}'.");
            }
            if (entry.Cost != canonical.Cost)
            {
                throw new InvalidOperationException(
                    $"Skill plan node '{entry.NodeId}' cost {entry.Cost} does not match canonical cost {canonical.Cost}.");
            }
            canonicalTotal = checked(canonicalTotal + canonical.Cost);
        }

        if (persistedInput.TotalCost != canonicalTotal)
        {
            throw new InvalidOperationException(
                $"Skill plan total cost {persistedInput.TotalCost} does not match canonical cost {canonicalTotal}.");
        }
        if (canonicalTotal > progressionFacts.AvailableSkillPoints)
        {
            throw new InvalidOperationException(
                $"Skill plan costs {canonicalTotal} SP but only {progressionFacts.AvailableSkillPoints} SP are available.");
        }

        CharacterProfile projected = profile;
        int available = progressionFacts.AvailableSkillPoints;
        foreach (var entry in entries)
        {
            var node = MasterySkillGraph.Build(
                    projected,
                    progressionFacts.Level,
                    available,
                    catalog,
                    progressionFacts.MasteryCheckpointComplete)
                .Branches
                .SelectMany(branch => branch.Nodes)
                .Single(candidate => string.Equals(candidate.Id, entry.NodeId, StringComparison.Ordinal));

            if (node.State is not (SkillNodeState.Unlockable or SkillNodeState.Mastery))
            {
                string detail = string.IsNullOrWhiteSpace(node.LockReason)
                    ? node.State.ToString()
                    : node.LockReason;
                throw new InvalidOperationException(
                    $"Skill plan cannot acquire '{entry.NodeId}': {detail}.");
            }

            projected = ApplyEntry(projected, entry, catalog);
            available = checked(available - entry.Cost);
        }

        return persistedInput.EffectsVersion == CharacterSkillPlanInput.CurrentEffectsVersion
            ? projected with
            {
                MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
            }
            : projected;
    }

    private static void ValidateEnvelope(CharacterSkillPlanInput input)
    {
        if (input.Version != CharacterSkillPlanInput.CurrentVersion)
        {
            throw new NotSupportedException(
                $"Character skill-plan input version {input.Version} is not supported.");
        }
        if (input.ProgressionVersion != CharacterLevelProgression.Level300Version)
        {
            throw new InvalidOperationException(
                $"Character skill-plan progression version {input.ProgressionVersion} is invalid.");
        }
        if (input.EffectsVersion is not 0 and not CharacterSkillPlanInput.CurrentEffectsVersion)
        {
            throw new NotSupportedException(
                $"Character skill-plan effects version {input.EffectsVersion} is not supported.");
        }
        if (input.Entries is null || input.Entries.Count == 0)
            throw new InvalidOperationException("A skill plan must contain at least one acquisition.");
        if (input.TotalCost < 0)
            throw new InvalidOperationException("A skill plan total cost cannot be negative.");
    }

    private static void ValidateContext(
        CharacterProfile profile,
        MasteryProgressionFacts facts,
        MasterySkillCatalog catalog)
    {
        if (profile.ProgressionVersion != CharacterLevelProgression.Level300Version)
            throw new InvalidOperationException("The atomic mastery plan requires a progression-v2 profile.");
        if (profile.MasteryEffectsVersion is not 0 and not CharacterProfile.CurrentMasteryEffectsVersion)
        {
            throw new NotSupportedException(
                $"Character mastery-effects version {profile.MasteryEffectsVersion} is not supported.");
        }
        if (profile.Stats is null)
            throw new InvalidOperationException("A progression-v2 profile requires its complete stat map.");
        if (catalog.ProgressionVersion != profile.ProgressionVersion)
            throw new InvalidOperationException("The mastery catalog and character progression versions differ.");
        if (facts.Level is < 1 or > CharacterLevelProgression.Level300Max)
        {
            throw new ArgumentOutOfRangeException(
                nameof(facts), facts.Level, "A progression-v2 character level must be between 1 and 300.");
        }
        if (facts.AvailableSkillPoints is < 0 or > MasterySkillCatalog.SkillPointsMaximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(facts), facts.AvailableSkillPoints,
                "Available progression-v2 Skill Points must be between 0 and 499.");
        }
        if (profile.SkillPointsSpent is < 0 or > MasterySkillCatalog.SkillPointsMaximum)
            throw new InvalidOperationException("Persisted progression-v2 Skill Points spent are outside 0..499.");
        if (checked(profile.SkillPointsSpent + facts.AvailableSkillPoints) >
            MasterySkillCatalog.SkillPointsMaximum)
        {
            throw new InvalidOperationException("Persisted spent and available Skill Points exceed the lifetime pool.");
        }

        // This also validates unknown/duplicate ownership, prerequisite closure, level gates,
        // capstone override provenance, rail sequence, baseline range, and the 0.99 rail cap.
        _ = MasterySkillGraph.Build(
            profile,
            facts.Level,
            facts.AvailableSkillPoints,
            catalog,
            facts.MasteryCheckpointComplete);

        int ownedCost = checked((profile.AcquiredSkillIds ?? [])
            .Sum(id => catalog.GetSkill(id).Cost));
        ownedCost = checked(ownedCost + (profile.AcquiredAttributeNodeIds ?? [])
            .Sum(id => catalog.GetAttributeNode(id).Cost));
        if (profile.SkillPointsSpent != ownedCost)
        {
            throw new InvalidOperationException(
                $"Persisted Skill Points spent {profile.SkillPointsSpent} do not match owned-node cost {ownedCost}.");
        }

        ValidateCurrentAttributeValues(profile, catalog);
    }

    private static void ValidateCurrentAttributeValues(
        CharacterProfile profile,
        MasterySkillCatalog catalog)
    {
        var baseline = profile.CreationBaseline
            ?? throw new InvalidOperationException("A progression-v2 profile requires its creation baseline.");
        if (baseline.Stats is null || baseline.Meta is null)
            throw new InvalidOperationException("A progression-v2 creation baseline requires complete stat maps.");
        var owned = (profile.AcquiredAttributeNodeIds ?? []).ToHashSet(StringComparer.Ordinal);
        foreach (var rail in catalog.AttributeRails)
        {
            double baselineValue = BaselineStat(baseline, rail.Stat);
            int ownedCount = catalog.AttributeNodes.Count(node =>
                string.Equals(node.RailId, rail.Id, StringComparison.Ordinal) && owned.Contains(node.Id));
            double expected = Math.Min(rail.CapValue, baselineValue + ownedCount * rail.StepValue);
            if (!profile.Stats.TryGetValue(rail.Stat, out double actual) || actual != expected)
            {
                throw new InvalidOperationException(
                    $"Profile attribute '{rail.Stat}' value does not match its creation baseline and acquired rail.");
            }
        }
    }

    private static CharacterProfile ApplyEntry(
        CharacterProfile profile,
        CharacterSkillPlanEntry entry,
        MasterySkillCatalog catalog)
    {
        if (string.Equals(entry.Kind, CharacterSkillPlanEntry.MasteryKind, StringComparison.Ordinal))
        {
            MasterySkillDefinition skill = catalog.GetSkill(entry.NodeId);
            if (skill.Effects.Any(effect =>
                    string.Equals(effect.Target, "chosenFlavor", StringComparison.Ordinal)) &&
                !PerkResolver.IsEligibleChosenFlavor(profile.ChosenFlavor))
            {
                throw new InvalidOperationException(
                    $"Mastery skill '{entry.NodeId}' requires a supported persisted chosen flavor.");
            }

            var acquired = (profile.AcquiredSkillIds ?? []).ToList();
            acquired.Add(entry.NodeId);
            return profile with
            {
                AcquiredSkillIds = acquired,
                SkillPointsSpent = checked(profile.SkillPointsSpent + entry.Cost),
            };
        }

        var node = catalog.GetAttributeNode(entry.NodeId);
        var baseline = profile.CreationBaseline
            ?? throw new InvalidOperationException("A progression-v2 profile requires its creation baseline.");
        double value = Math.Min(node.CapValue, BaselineStat(baseline, node.Stat) + node.Order * node.StepValue);
        var stats = new Dictionary<string, double>(profile.Stats, StringComparer.Ordinal)
        {
            [node.Stat] = value,
        };
        var acquiredAttributes = (profile.AcquiredAttributeNodeIds ?? []).ToList();
        acquiredAttributes.Add(entry.NodeId);
        return profile with
        {
            Stats = stats,
            AcquiredAttributeNodeIds = acquiredAttributes,
            SkillPointsSpent = checked(profile.SkillPointsSpent + entry.Cost),
        };
    }

    private static (string Kind, int Cost) ResolveCanonical(
        string nodeId,
        MasterySkillCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            throw new InvalidOperationException("A skill plan contains a blank node id.");
        if (catalog.TryGetSkill(nodeId, out var skill))
            return (CharacterSkillPlanEntry.MasteryKind, skill.Cost);
        if (catalog.TryGetAttributeNode(nodeId, out var attribute))
            return (CharacterSkillPlanEntry.AttributeKind, attribute.Cost);
        throw new InvalidOperationException($"Skill plan references unknown node '{nodeId}'.");
    }

    private static double BaselineStat(CharacterCreationBaseline baseline, string stat)
    {
        if (baseline.Stats.TryGetValue(stat, out double talent))
            return talent;
        if (baseline.Meta.TryGetValue(stat, out double meta))
            return meta;
        throw new InvalidOperationException($"Profile creation baseline has no '{stat}' attribute.");
    }
}
