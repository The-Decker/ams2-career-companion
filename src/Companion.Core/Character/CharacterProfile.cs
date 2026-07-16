using System.Text.Json.Serialization;

namespace Companion.Core.Character;

/// <summary>
/// A player's authored character: the seven stats (five talent + two meta, id → 0..1), the chosen
/// perk ids, and the CP not yet spent. This is the INPUT written once at creation (journaled as
/// <c>player.character</c>) and folded into <c>PlayerCareerState</c> — the sim derives the
/// player-seat rating writes and the <see cref="PlayerPerkModifiers"/> from it deterministically, so
/// the same profile reproduces the career byte-for-byte (docs/dev/character-system.md §5-6).
/// </summary>
public sealed record CharacterProfile
{
    /// <summary>The first supported production semantics for mastery-skill effects.</summary>
    public const int CurrentMasteryEffectsVersion = 1;

    /// <summary>
    /// The tier-aware expected-finish model. Version 1 remains readable for folded careers; version
    /// 2 supplies the missing team-car hierarchy when a pack authors every car at neutral scalars.
    /// </summary>
    public const int CurrentExpectationModelVersion = 2;

    /// <summary>All seven stats by id (pace/oneLap/craft/racecraft/adaptability + marketability/
    /// durability), each 0..1.</summary>
    public required IReadOnlyDictionary<string, double> Stats { get; init; }

    public required IReadOnlyList<string> PerkIds { get; init; }

    /// <summary>The player's chosen driver name — the identity the whole app uses (news, standings,
    /// dossier), rather than the historical driver whose seat they took. Empty for a legacy
    /// character created before naming existed (then the app falls back to the seat's driver).</summary>
    public string Name { get; init; } = "";

    /// <summary>The player's authored three-letter country code. This is immutable identity and
    /// presentation data only: it never changes ratings, physics, expectation, XP, or RNG. Null for
    /// profiles created before country selection existed; omitted so those saves and replay inputs
    /// retain their byte-identical JSON shape.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? CountryCode { get; init; }

    /// <summary>The driver's REAL age in their first season — the character's own age, set at
    /// creation, not borrowed from the historical driver whose seat they took. Drives the season-end
    /// aging curve and the contract-offer age risk (a 19-year-old rookie and a 34-year-old veteran
    /// age and get courted very differently). Null for a legacy character created before ages existed
    /// (then the app falls back to the seat driver's age, exactly as before) — omitted from the JSON
    /// when null, so a legacy character serialises byte-for-byte unchanged.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int? Age { get; init; }

    /// <summary>The rating field One-Trick Pony's specialism is bound to, picked at creation (e.g.
    /// "wetSkill", "tyreManagement"). Gives the perk's +0.30 a concrete home and names the ONE stat
    /// in-career level points may raise. Null when the character did not take One-Trick Pony (or a
    /// legacy profile predating the pick, which then falls back to <see cref="PerkResolver.DefaultChosenFlavor"/>);
    /// omitted from the JSON when null so a character without it serialises byte-for-byte unchanged.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? ChosenFlavor { get; init; }

    /// <summary>Character Points left over at CREATION (immutable) — the starting bank. The pool
    /// available to spend later is this plus level grants minus <see cref="CpSpent"/>
    /// (<see cref="CharacterProgress.AvailableCp"/>).</summary>
    public int CpUnspent { get; init; }

    /// <summary>Total character points SPENT between seasons so far (raising stats, adding perks).
    /// Accumulates as the driver develops; 0 for a character that has never spent. Omitted from the
    /// state blob when 0, so a never-spent character serialises exactly as before.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int CpSpent { get; init; }

    /// <summary>
    /// Opt-in version for the skill-tree/era-cap progression rules. Legacy profiles deserialize as
    /// 0 and retain their original uncapped XP fold; newly created profiles use version 1. Omitted
    /// at 0 so pre-rework state blobs remain byte-identical.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ProgressionVersion { get; init; }

    /// <summary>Stat-node ids bought through the skill tree. Null/empty for legacy profiles and for
    /// drivers who have not bought a stat node; omitted until the first node is owned.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<string>? UnlockedSkillNodeIds { get; init; }

    /// <summary>The perks selected at character creation. Respec keeps these identity perks locked.
    /// Null on legacy profiles, where every already-owned perk is conservatively creation-locked.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<string>? CreationPerkIds { get; init; }

    // ---- Version-2 identity and mastery provenance ----

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? RacingDnaId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int RacingDnaVersion { get; init; }

    /// <summary>Optional stable context chosen by a DNA that needs one (for example a circuit
    /// family, rival, season objective or nationality affinity). Its meaning is versioned by the
    /// selected DNA definition and it is immutable after creation.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? RacingDnaChoice { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public CharacterCreationBaseline? CreationBaseline { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<string>? AcquiredSkillIds { get; init; }

    /// <summary>
    /// Opt-in version for progression-v2 character mechanics: the persisted marketability meta-stat
    /// and effects authored by <see cref="AcquiredSkillIds"/>. Profiles written before those
    /// mechanics became active remain 0, so their historical folds keep the exact prior behavior.
    /// Omitted at 0 for byte-compatible legacy state JSON.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int MasteryEffectsVersion { get; init; }

    /// <summary>
    /// Selects the expected-finish model used by the live fold and replay. Version 0 is the shipped
    /// car+driver formula; version 1 adds the bounded folded-OPI adjustment; version 2 adds the
    /// neutral-pack team-tier fallback. Omitted at 0 so legacy character state blobs and their
    /// derived journals remain byte-identical.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ExpectationModelVersion { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<string>? AcquiredAttributeNodeIds { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SkillPointsSpent { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long XpSpentOnResets { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SkillResetCount { get; init; }

    [JsonIgnore]
    public IReadOnlyList<string> SkillNodeIds => UnlockedSkillNodeIds ?? [];

    public double Stat(string id) => Stats.GetValueOrDefault(id);

    // Structural equality over the collections. The compiler-generated record equality would
    // compare Stats (a dictionary) and PerkIds (a list) by REFERENCE, so two structurally-identical
    // profiles deserialized separately (e.g. a re-derived season start state vs the stored one)
    // would compare unequal — a FALSE replay divergence at every season boundary. Comparing by value
    // makes PlayerCareerState's record equality (which the replay start-state gate uses) match the
    // JSON the DB actually stores. (Increment 4a — determinism fix.)
    public bool Equals(CharacterProfile? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        return CpUnspent == other.CpUnspent
            && CpSpent == other.CpSpent
            && ProgressionVersion == other.ProgressionVersion
            && RacingDnaVersion == other.RacingDnaVersion
            && MasteryEffectsVersion == other.MasteryEffectsVersion
            && ExpectationModelVersion == other.ExpectationModelVersion
            && SkillPointsSpent == other.SkillPointsSpent
            && XpSpentOnResets == other.XpSpentOnResets
            && SkillResetCount == other.SkillResetCount
            && Age == other.Age
            && string.Equals(Name, other.Name, StringComparison.Ordinal)
            && string.Equals(CountryCode, other.CountryCode, StringComparison.Ordinal)
            && string.Equals(ChosenFlavor, other.ChosenFlavor, StringComparison.Ordinal)
            && string.Equals(RacingDnaId, other.RacingDnaId, StringComparison.Ordinal)
            && string.Equals(RacingDnaChoice, other.RacingDnaChoice, StringComparison.Ordinal)
            && Equals(CreationBaseline, other.CreationBaseline)
            && PerkIds.SequenceEqual(other.PerkIds)
            && (UnlockedSkillNodeIds ?? []).SequenceEqual(other.UnlockedSkillNodeIds ?? [])
            && (CreationPerkIds ?? []).SequenceEqual(other.CreationPerkIds ?? [])
            && (AcquiredSkillIds ?? []).SequenceEqual(other.AcquiredSkillIds ?? [])
            && (AcquiredAttributeNodeIds ?? []).SequenceEqual(other.AcquiredAttributeNodeIds ?? [])
            && StatsEqual(Stats, other.Stats);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CpUnspent);
        hash.Add(CpSpent);
        hash.Add(ProgressionVersion);
        hash.Add(RacingDnaVersion);
        hash.Add(MasteryEffectsVersion);
        hash.Add(ExpectationModelVersion);
        hash.Add(SkillPointsSpent);
        hash.Add(XpSpentOnResets);
        hash.Add(SkillResetCount);
        hash.Add(Age);
        hash.Add(Name);
        hash.Add(CountryCode);
        hash.Add(ChosenFlavor);
        hash.Add(RacingDnaId);
        hash.Add(RacingDnaChoice);
        hash.Add(CreationBaseline);
        foreach (string id in PerkIds)
            hash.Add(id);
        foreach (string id in UnlockedSkillNodeIds ?? [])
            hash.Add(id);
        foreach (string id in CreationPerkIds ?? [])
            hash.Add(id);
        foreach (string id in AcquiredSkillIds ?? [])
            hash.Add(id);
        foreach (string id in AcquiredAttributeNodeIds ?? [])
            hash.Add(id);
        foreach (var (key, value) in Stats.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            hash.Add(key);
            hash.Add(value);
        }
        return hash.ToHashCode();
    }

    private static bool StatsEqual(IReadOnlyDictionary<string, double> a, IReadOnlyDictionary<string, double> b)
    {
        if (a.Count != b.Count)
            return false;
        foreach (var (key, value) in a)
            if (!b.TryGetValue(key, out double other) || other != value)
                return false;
        return true;
    }

    /// <summary>Builds the complete, in-budget character an archetype preset describes — the
    /// one-click default at creation. Talent stats and meta stats merge into the one stat map.</summary>
    public static CharacterProfile FromArchetype(Archetype archetype)
    {
        var stats = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (id, value) in archetype.StartStats)
            stats[id] = value;
        foreach (var (id, value) in archetype.StartMeta)
            stats[id] = value;

        return new CharacterProfile
        {
            Stats = stats,
            PerkIds = archetype.PerkIds.ToList(),
            CreationPerkIds = archetype.PerkIds.ToList(),
            ProgressionVersion = 1,
        };
    }
}
