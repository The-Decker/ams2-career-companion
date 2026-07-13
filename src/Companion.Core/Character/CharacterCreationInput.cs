using System.Text.Json.Serialization;
using Companion.Core.Career;

namespace Companion.Core.Character;

/// <summary>Versioned, lossless v2 <c>player.character</c> INPUT envelope. Legacy v0/v1 rows retain
/// their original compact payload; only an explicitly gated v2 creation writes this shape.</summary>
public sealed record CharacterCreationInput
{
    public const int CurrentVersion = 1;

    private static readonly string[] RequiredTalentStatIds =
        ["pace", "oneLap", "craft", "racecraft", "adaptability"];
    private static readonly string[] RequiredMetaStatIds = ["marketability", "durability"];

    public int Version { get; init; } = CurrentVersion;
    public required CharacterProfile Profile { get; init; }
    public required string ExperienceMode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public CampaignProgressionPlan? CampaignProgressionPlan { get; init; }

    /// <summary>Validates a new v2 creation before it becomes an immutable INPUT row. Deserialization
    /// itself stays permissive so legacy state can still be inspected and migrated deliberately.</summary>
    public void ValidateForNewCareer()
    {
        if (Version != CurrentVersion)
            throw new NotSupportedException(
                $"Character creation input version {Version} is not supported by this build.");
        if (Profile is null)
            throw new InvalidOperationException("The versioned creation envelope requires a complete profile.");
        if (Profile.Stats is null || Profile.PerkIds is null)
            throw new InvalidOperationException("A v2 profile requires complete stat and perk collections.");
        if (Profile.ProgressionVersion != CharacterLevelProgression.Level300Version)
            throw new InvalidOperationException("The versioned creation envelope requires a progression-v2 profile.");
        if (!CareerExperienceModes.IsKnown(ExperienceMode))
            throw new InvalidOperationException($"Unknown career experience mode '{ExperienceMode}'.");

        if (CareerExperienceModes.IsBoundedCampaign(ExperienceMode))
        {
            if (CampaignProgressionPlan is null)
                throw new InvalidOperationException("A bounded v2 career requires a campaign progression plan.");
            CampaignProgressionPlan.Validate();
            if (!string.Equals(CampaignProgressionPlan.Mode, ExperienceMode, StringComparison.Ordinal))
                throw new InvalidOperationException("The creation mode and campaign-plan mode must match.");
        }
        else if (CampaignProgressionPlan is not null)
        {
            throw new InvalidOperationException("Racing Passport uses portfolio state, not a bounded campaign plan.");
        }

        if (string.IsNullOrWhiteSpace(Profile.RacingDnaId) || Profile.RacingDnaVersion <= 0)
            throw new InvalidOperationException("A v2 character requires a versioned Racing DNA identity.");
        if (Profile.RacingDnaChoice is not null && string.IsNullOrWhiteSpace(Profile.RacingDnaChoice))
            throw new InvalidOperationException("A Racing DNA context choice cannot be blank.");
        var baseline = Profile.CreationBaseline
            ?? throw new InvalidOperationException("A v2 character requires a lossless creation baseline.");
        if (baseline.Stats is null || baseline.Meta is null || baseline.TraitIds is null)
            throw new InvalidOperationException("A v2 creation baseline requires complete stat, meta and trait collections.");

        ValidateBaselineMap(baseline.Stats, "talent stat");
        ValidateBaselineMap(baseline.Meta, "meta stat");
        ValidateExactIds(baseline.Stats.Keys, RequiredTalentStatIds, "talent stat");
        ValidateExactIds(baseline.Meta.Keys, RequiredMetaStatIds, "meta stat");
        if (baseline.Stats.Keys.Any(baseline.Meta.ContainsKey))
            throw new InvalidOperationException("Creation baseline talent and meta stat ids must not overlap.");

        var combined = baseline.Stats
            .Concat(baseline.Meta)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        if (combined.Count != Profile.Stats.Count ||
            combined.Any(kv => !Profile.Stats.TryGetValue(kv.Key, out double value) || value != kv.Value))
        {
            throw new InvalidOperationException("Creation baseline stats must exactly reproduce the profile stats.");
        }

        if (baseline.TraitIds.Any(string.IsNullOrWhiteSpace) ||
            baseline.TraitIds.Distinct(StringComparer.Ordinal).Count() != baseline.TraitIds.Count)
        {
            throw new InvalidOperationException("Creation baseline trait ids must be nonblank and unique.");
        }
        if (!(Profile.CreationPerkIds ?? []).SequenceEqual(baseline.TraitIds) ||
            !Profile.PerkIds.SequenceEqual(baseline.TraitIds))
        {
            throw new InvalidOperationException("Creation baseline traits must match the profile's creation perks.");
        }
        if (!string.Equals(baseline.ChosenFlavor, Profile.ChosenFlavor, StringComparison.Ordinal))
            throw new InvalidOperationException("Creation baseline flavor must match the profile flavor.");

        if (Profile.CpUnspent != 0 ||
            Profile.CpSpent != 0 ||
            (Profile.UnlockedSkillNodeIds?.Count ?? 0) != 0 ||
            (Profile.AcquiredSkillIds?.Count ?? 0) != 0 ||
            (Profile.AcquiredAttributeNodeIds?.Count ?? 0) != 0 ||
            Profile.SkillPointsSpent != 0 ||
            Profile.XpSpentOnResets != 0 ||
            Profile.SkillResetCount != 0)
        {
            throw new InvalidOperationException("A new v2 character cannot begin with acquired mastery or reset spend.");
        }
    }

    private static void ValidateBaselineMap(IReadOnlyDictionary<string, double> values, string label)
    {
        foreach (var (id, value) in values)
        {
            if (string.IsNullOrWhiteSpace(id) || !double.IsFinite(value) || value is < 0.0 or > 1.0)
                throw new InvalidOperationException(
                    $"Creation baseline {label} '{id}' must be finite and within 0.0-1.0.");
        }
    }

    private static void ValidateExactIds(
        IEnumerable<string> actual,
        IReadOnlyCollection<string> required,
        string label)
    {
        var ids = actual.ToHashSet(StringComparer.Ordinal);
        if (ids.Count != required.Count || required.Any(id => !ids.Contains(id)))
            throw new InvalidOperationException(
                $"Creation baseline must contain exactly the required {label} ids.");
    }
}
