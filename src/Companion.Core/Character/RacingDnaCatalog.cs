using System.Text.Json;
using Companion.Core.Json;

namespace Companion.Core.Character;

/// <summary>
/// The progression-v2 Racing DNA catalog. A DNA is a permanent, zero-SP creation identity; this
/// catalog only validates and projects its immutable authored definition. Mechanical consumers are
/// added separately so a descriptive hook can never silently change a fold.
/// </summary>
public sealed class RacingDnaCatalog
{
    public const int CurrentSchemaVersion = 1;
    public const int SupportedProgressionVersion = CharacterLevelProgression.Level300Version;
    public const int IdentityCount = 30;

    private static readonly HashSet<string> Families =
        ["pace", "racecraft", "physical", "mental", "business", "weather", "team", "media", "era"];

    private readonly IReadOnlyDictionary<(string Id, int Version), RacingDnaDefinition> _byIdentity;
    private readonly IReadOnlyDictionary<string, int> _traitCosts;

    public int SchemaVersion { get; }
    public int ProgressionVersion { get; }
    public RacingDnaCreationBudget CreationBudget { get; }
    public IReadOnlyList<RacingDnaDefinition> Definitions { get; }

    private RacingDnaCatalog(
        RacingDnaCatalogFile file,
        IReadOnlyDictionary<(string Id, int Version), RacingDnaDefinition> byIdentity,
        IReadOnlyDictionary<string, int> traitCosts)
    {
        SchemaVersion = file.SchemaVersion;
        ProgressionVersion = file.ProgressionVersion;
        CreationBudget = file.CreationBudget;
        Definitions = file.Definitions.ToArray();
        _byIdentity = byIdentity;
        _traitCosts = traitCosts;
    }

    private static readonly JsonSerializerOptions ParseOptions = new(CoreJson.Options)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Parses and fully audits authored data. Core owns no I/O; callers supply both JSON
    /// and the immutable legacy trait catalog used by v2 creation presets.</summary>
    public static RacingDnaCatalog Parse(string json, CharacterRules characterRules)
    {
        ArgumentNullException.ThrowIfNull(characterRules);
        var file = JsonSerializer.Deserialize<RacingDnaCatalogFile>(json, ParseOptions)
            ?? throw new JsonException("racing-dna-v2.json parsed to null.");
        return Validate(file, characterRules);
    }

    public bool TryGet(string id, int version, out RacingDnaDefinition definition) =>
        _byIdentity.TryGetValue((id, version), out definition!);

    /// <summary>Exact-version lookup. There is deliberately no "latest by ID" API because an old
    /// career must never drift onto a later balance definition.</summary>
    public RacingDnaDefinition Get(string id, int version) =>
        TryGet(id, version, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown Racing DNA definition '{id}' version {version}.");

    /// <summary>Validates a complete v2 creation against the selected exact DNA definition and the
    /// catalog's separately versioned Creation Budget. Dynamic rival/nationality existence is
    /// checked by the owning creation mode; this pure gate still requires a stable nonblank value.</summary>
    public RacingDnaDefinition ValidateCreation(CharacterProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.ProgressionVersion != ProgressionVersion)
            throw new InvalidOperationException(
                $"Racing DNA catalog {SchemaVersion} requires progression version {ProgressionVersion}.");
        if (string.IsNullOrWhiteSpace(profile.RacingDnaId))
            throw new InvalidOperationException("A v2 character requires a Racing DNA id.");

        RacingDnaDefinition definition;
        try
        {
            definition = Get(profile.RacingDnaId, profile.RacingDnaVersion);
        }
        catch (KeyNotFoundException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }

        ValidateChoice(definition, profile.RacingDnaChoice, dataError: false);
        var baseline = profile.CreationBaseline
            ?? throw new InvalidOperationException("A v2 character requires a creation baseline.");
        ValidateBuild(
            definition.Id,
            baseline.Stats,
            baseline.Meta,
            baseline.TraitIds,
            CreationBudget,
            _traitCosts,
            dataError: false);
        return definition;
    }

    private static RacingDnaCatalog Validate(RacingDnaCatalogFile file, CharacterRules rules)
    {
        if (file.SchemaVersion != CurrentSchemaVersion)
            throw new JsonException(
                $"Racing DNA schema version {file.SchemaVersion} is not supported by this build.");
        if (file.ProgressionVersion != SupportedProgressionVersion)
            throw new JsonException(
                $"Racing DNA catalog targets progression version {file.ProgressionVersion}, expected {SupportedProgressionVersion}.");
        if (file.CreationBudget is null)
            throw new JsonException("Racing DNA catalog has no Creation Budget.");
        if (file.Definitions is null || file.Definitions.Count == 0)
            throw new JsonException("Racing DNA catalog declares no definitions.");

        var talentIds = rules.Stats.TalentStats.Select(stat => stat.Id).ToHashSet(StringComparer.Ordinal);
        var metaIds = rules.Stats.MetaStats.Select(stat => stat.Id).ToHashSet(StringComparer.Ordinal);
        ValidateBudget(file.CreationBudget, talentIds, metaIds);

        var traitCosts = rules.Perks.ToDictionary(perk => perk.Id, perk => perk.Cost, StringComparer.Ordinal);
        var byIdentity = new Dictionary<(string Id, int Version), RacingDnaDefinition>();
        var identityFamilies = new Dictionary<string, (string Primary, string Secondary)>(StringComparer.Ordinal);

        foreach (var definition in file.Definitions)
        {
            ValidateDefinition(definition, file.CreationBudget, traitCosts);
            if (!byIdentity.TryAdd((definition.Id, definition.Version), definition))
                throw new JsonException(
                    $"Duplicate Racing DNA definition '{definition.Id}' version {definition.Version}.");

            if (identityFamilies.TryGetValue(definition.Id, out var priorFamilies))
            {
                if (!string.Equals(priorFamilies.Primary, definition.PrimaryFamily, StringComparison.Ordinal) ||
                    !string.Equals(priorFamilies.Secondary, definition.SecondaryFamily, StringComparison.Ordinal))
                {
                    throw new JsonException(
                        $"Racing DNA '{definition.Id}' changes family identity between definition versions.");
                }
            }
            else
            {
                identityFamilies.Add(definition.Id, (definition.PrimaryFamily, definition.SecondaryFamily));
            }
        }

        if (identityFamilies.Count != IdentityCount)
            throw new JsonException(
                $"Racing DNA catalog must contain exactly {IdentityCount} unique identities; found {identityFamilies.Count}.");
        foreach (string id in identityFamilies.Keys)
        {
            if (!byIdentity.ContainsKey((id, 1)))
                throw new JsonException($"Racing DNA identity '{id}' has no immutable version 1 definition.");
        }

        return new RacingDnaCatalog(file, byIdentity, traitCosts);
    }

    private static void ValidateBudget(
        RacingDnaCreationBudget budget,
        IReadOnlySet<string> talentIds,
        IReadOnlySet<string> metaIds)
    {
        if (budget.Version <= 0)
            throw new JsonException("Racing DNA Creation Budget version must be positive.");
        if (!double.IsFinite(budget.StatSumCap) || budget.StatSumCap <= 0.0)
            throw new JsonException("Racing DNA statSumCap must be finite and positive.");
        if (budget.MaxTraits < 0)
            throw new JsonException("Racing DNA maxTraits cannot be negative.");
        if (budget.TraitBudget < budget.TraitSpendMin || budget.TraitBudget > budget.TraitSpendMax)
            throw new JsonException("Racing DNA traitBudget must sit inside its allowed spend window.");
        if (budget.TraitSpendMin > budget.TraitSpendMax)
            throw new JsonException("Racing DNA trait-spend minimum exceeds its maximum.");

        ValidateRangeMap("talentRanges", budget.TalentRanges, talentIds);
        ValidateRangeMap("metaRanges", budget.MetaRanges, metaIds);
    }

    private static void ValidateRangeMap(
        string label,
        IReadOnlyDictionary<string, IReadOnlyList<double>> ranges,
        IReadOnlySet<string> requiredIds)
    {
        if (ranges is null || ranges.Count != requiredIds.Count || requiredIds.Any(id => !ranges.ContainsKey(id)))
            throw new JsonException($"Racing DNA {label} must contain exactly the configured stat ids.");
        foreach (var (id, range) in ranges)
        {
            if (!requiredIds.Contains(id) || range is null || range.Count != 2 ||
                !double.IsFinite(range[0]) || !double.IsFinite(range[1]) ||
                range[0] < 0.0 || range[1] > 1.0 || range[0] > range[1])
            {
                throw new JsonException(
                    $"Racing DNA {label} entry '{id}' must be a finite [min,max] range within 0.0-1.0.");
            }
        }
    }

    private static void ValidateDefinition(
        RacingDnaDefinition definition,
        RacingDnaCreationBudget budget,
        IReadOnlyDictionary<string, int> traitCosts)
    {
        if (!IsStableDnaId(definition.Id))
            throw new JsonException($"Racing DNA id '{definition.Id}' is not a stable dna_* id.");
        if (definition.Version <= 0)
            throw new JsonException($"Racing DNA '{definition.Id}' has a non-positive version.");
        if (string.IsNullOrWhiteSpace(definition.Name) || string.IsNullOrWhiteSpace(definition.Description))
            throw new JsonException($"Racing DNA '{definition.Id}' requires a name and description.");
        if (!Families.Contains(definition.PrimaryFamily) || !Families.Contains(definition.SecondaryFamily) ||
            string.Equals(definition.PrimaryFamily, definition.SecondaryFamily, StringComparison.Ordinal))
        {
            throw new JsonException(
                $"Racing DNA '{definition.Id}' requires two distinct known families.");
        }

        ValidateBuild(
            definition.Id,
            definition.StartingStats,
            definition.StartingMeta,
            definition.StartingTraitIds,
            budget,
            traitCosts,
            dataError: true);
        ValidateEffects(definition);
        ValidateChoice(definition, null, dataError: true);
    }

    private static void ValidateBuild(
        string identityId,
        IReadOnlyDictionary<string, double> stats,
        IReadOnlyDictionary<string, double> meta,
        IReadOnlyList<string> traitIds,
        RacingDnaCreationBudget budget,
        IReadOnlyDictionary<string, int> traitCosts,
        bool dataError)
    {
        string? error = ValidateValues(stats, budget.TalentRanges, "talent stat")
            ?? ValidateValues(meta, budget.MetaRanges, "meta stat");
        if (error is null && stats.Keys.Any(meta.ContainsKey))
            error = "talent and meta stat ids overlap";
        if (error is null && stats.Values.Concat(meta.Values).Sum() > budget.StatSumCap + 1e-9)
            error = $"stat sum exceeds {budget.StatSumCap:0.###}";

        traitIds ??= [];
        if (error is null &&
            (traitIds.Any(string.IsNullOrWhiteSpace) ||
             traitIds.Distinct(StringComparer.Ordinal).Count() != traitIds.Count))
        {
            error = "trait ids must be nonblank and unique";
        }
        if (error is null && traitIds.Count > budget.MaxTraits)
            error = $"trait count exceeds {budget.MaxTraits}";

        int traitSpend = 0;
        if (error is null)
        {
            foreach (string traitId in traitIds)
            {
                if (!traitCosts.TryGetValue(traitId, out int cost))
                {
                    error = $"unknown trait id '{traitId}'";
                    break;
                }
                traitSpend = checked(traitSpend + cost);
            }
        }
        if (error is null && (traitSpend < budget.TraitSpendMin || traitSpend > budget.TraitSpendMax))
            error = $"trait spend {traitSpend} is outside {budget.TraitSpendMin}-{budget.TraitSpendMax}";

        if (error is not null)
        {
            string message = $"Racing DNA '{identityId}' creation build is invalid: {error}.";
            if (dataError)
                throw new JsonException(message);
            throw new InvalidOperationException(message);
        }
    }

    private static string? ValidateValues(
        IReadOnlyDictionary<string, double> values,
        IReadOnlyDictionary<string, IReadOnlyList<double>> ranges,
        string label)
    {
        if (values is null || values.Count != ranges.Count || ranges.Keys.Any(id => !values.ContainsKey(id)))
            return $"must contain exactly the configured {label} ids";
        foreach (var (id, value) in values)
        {
            if (!ranges.TryGetValue(id, out var range) || !double.IsFinite(value) ||
                value < range[0] || value > range[1])
            {
                return $"{label} '{id}' is outside its authored range";
            }
        }
        return null;
    }

    private static void ValidateEffects(RacingDnaDefinition definition)
    {
        if (definition.PersistentEffects is not { Count: > 0 } ||
            definition.TradeoffEffects is not { Count: > 0 })
        {
            throw new JsonException(
                $"Racing DNA '{definition.Id}' requires a persistent effect and a permanent tradeoff.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        double benefit = 0.0;
        double drawback = 0.0;
        foreach (var effect in definition.PersistentEffects)
        {
            ValidateEffect(definition.Id, effect, benefit: true, keys);
            benefit += effect.BalanceValue;
        }
        foreach (var effect in definition.TradeoffEffects)
        {
            ValidateEffect(definition.Id, effect, benefit: false, keys);
            drawback += effect.BalanceValue;
        }

        if (Math.Abs(benefit + drawback) > 0.5 + 1e-9)
            throw new JsonException(
                $"Racing DNA '{definition.Id}' benefit/tradeoff pricing is not zero-cost (net {benefit + drawback:0.###}).");
    }

    private static void ValidateEffect(
        string identityId,
        RacingDnaEffect effect,
        bool benefit,
        ISet<string> keys)
    {
        if (!IsStableEffectKey(effect.Key) || !keys.Add(effect.Key))
            throw new JsonException($"Racing DNA '{identityId}' has a blank, malformed, or duplicate effect key.");
        if (effect.Classification is null)
            throw new JsonException($"Racing DNA '{identityId}' effect '{effect.Key}' needs an explicit classification.");
        if (string.IsNullOrWhiteSpace(effect.Summary))
            throw new JsonException($"Racing DNA '{identityId}' effect '{effect.Key}' needs a summary.");
        if (!double.IsFinite(effect.Magnitude) || effect.Magnitude <= 0.0)
            throw new JsonException($"Racing DNA '{identityId}' effect '{effect.Key}' magnitude must be positive.");
        if (effect.Unit is null)
            throw new JsonException($"Racing DNA '{identityId}' effect '{effect.Key}' needs a known unit.");
        if (!double.IsFinite(effect.BalanceValue) ||
            (benefit && effect.BalanceValue <= 0.0) ||
            (!benefit && effect.BalanceValue >= 0.0))
        {
            throw new JsonException(
                $"Racing DNA '{identityId}' effect '{effect.Key}' has the wrong signed balance value.");
        }
        if (effect.Classification == CharacterEffectClass.Car)
            throw new JsonException(
                $"Racing DNA '{identityId}' effect '{effect.Key}' is classified CAR but names no weight/power/drag axis.");
    }

    private static void ValidateChoice(RacingDnaDefinition definition, string? selected, bool dataError)
    {
        string? error = null;
        var choice = definition.Choice;
        if (choice is null)
        {
            if (selected is not null)
                error = "does not author a context choice";
        }
        else if (choice.Kind is null || string.IsNullOrWhiteSpace(choice.Prompt))
        {
            error = "has an incomplete choice rule";
        }
        else
        {
            var options = choice.Options ?? [];
            if (options.Any(string.IsNullOrWhiteSpace) ||
                options.Distinct(StringComparer.Ordinal).Count() != options.Count)
            {
                error = "choice options must be nonblank and unique";
            }
            else if (choice.Kind is RacingDnaChoiceKind.TrackFamily or RacingDnaChoiceKind.SeasonObjective &&
                     options.Count == 0)
            {
                error = "static choice rules require options";
            }
            else if (choice.Kind is RacingDnaChoiceKind.RivalDriverId or RacingDnaChoiceKind.NationalityAffinity &&
                     options.Count != 0)
            {
                error = "dynamic choice rules cannot hard-code options";
            }
            else if (selected is null)
            {
                if (choice.Required && !dataError)
                    error = "requires a context choice";
            }
            else if (string.IsNullOrWhiteSpace(selected))
            {
                error = "context choice cannot be blank";
            }
            else if (options.Count > 0 && !options.Contains(selected, StringComparer.Ordinal))
            {
                error = $"does not allow context choice '{selected}'";
            }
        }

        if (error is null)
            return;
        string message = $"Racing DNA '{definition.Id}' {error}.";
        if (dataError)
            throw new JsonException(message);
        throw new InvalidOperationException(message);
    }

    private static bool IsStableDnaId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("dna_", StringComparison.Ordinal))
        {
            return false;
        }
        foreach (char character in value)
        {
            if (!(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_'))
                return false;
        }
        return true;
    }

    private static bool IsStableEffectKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] is < 'a' or > 'z')
            return false;
        return value.All(character =>
            character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9');
    }

    private sealed record RacingDnaCatalogFile
    {
        public int SchemaVersion { get; init; }
        public int ProgressionVersion { get; init; }
        public required RacingDnaCreationBudget CreationBudget { get; init; }
        public required IReadOnlyList<RacingDnaDefinition> Definitions { get; init; }
    }
}

public sealed record RacingDnaCreationBudget
{
    public int Version { get; init; }
    public double StatSumCap { get; init; }
    public int TraitBudget { get; init; }
    public int TraitSpendMin { get; init; }
    public int TraitSpendMax { get; init; }
    public int MaxTraits { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<double>> TalentRanges { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<double>> MetaRanges { get; init; }
}

public sealed record RacingDnaDefinition
{
    public required string Id { get; init; }
    public int Version { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string PrimaryFamily { get; init; }
    public required string SecondaryFamily { get; init; }
    public required IReadOnlyDictionary<string, double> StartingStats { get; init; }
    public required IReadOnlyDictionary<string, double> StartingMeta { get; init; }
    public required IReadOnlyList<string> StartingTraitIds { get; init; }
    public required IReadOnlyList<RacingDnaEffect> PersistentEffects { get; init; }
    public required IReadOnlyList<RacingDnaEffect> TradeoffEffects { get; init; }
    public RacingDnaChoiceRule? Choice { get; init; }
}

public sealed record RacingDnaEffect
{
    public required string Key { get; init; }
    public CharacterEffectClass? Classification { get; init; }
    public required string Summary { get; init; }
    public double Magnitude { get; init; }
    public RacingDnaEffectUnit? Unit { get; init; }
    public double BalanceValue { get; init; }
}

public enum RacingDnaEffectUnit
{
    MultiplierDelta = 0,
    FlatScore = 1,
    SeverityStep = 2,
    SeasonCount = 3,
    BooleanOverride = 4,
}

public sealed record RacingDnaChoiceRule
{
    public RacingDnaChoiceKind? Kind { get; init; }
    public required string Prompt { get; init; }
    public bool Required { get; init; }
    public IReadOnlyList<string> Options { get; init; } = [];
}

public enum RacingDnaChoiceKind
{
    TrackFamily = 0,
    RivalDriverId = 1,
    SeasonObjective = 2,
    NationalityAffinity = 3,
}
