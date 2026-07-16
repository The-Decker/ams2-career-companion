using Companion.Core.Career;
using Companion.Core.Determinism;

namespace Companion.Core.Character;

/// <summary>Stable creation facts required to randomize all 30 DNA identities honestly. Dynamic
/// choices are supplied by the owning mode/pack; the builder never invents a rival or nationality.</summary>
public sealed record RacingDnaRandomContext
{
    public required IReadOnlyList<string> EligibleRivalDriverIds { get; init; }
    public required IReadOnlyList<string> NationalityAffinities { get; init; }
}

/// <summary>
/// Pure progression-v2 random character creation. The returned complete profile is the only value
/// that is journaled; replay never redraws it. A fresh keyed stream per reroll makes the result
/// independent of every gameplay stream and of prior UI draw counts.
/// </summary>
public static class RacingDnaRandomBuild
{
    public const int MinCreationAge = 16;
    public const int MaxCreationAge = 45;

    public static CharacterProfile Create(
        RacingDnaCatalog catalog,
        ulong seed,
        int rerollOrdinal,
        RacingDnaRandomContext context,
        string name,
        int age)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(name);
        if (rerollOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(rerollOrdinal), "Reroll ordinal cannot be negative.");

        var rivals = NormalizeChoices(context.EligibleRivalDriverIds, "rival driver");
        var nationalities = NormalizeChoices(context.NationalityAffinities, "nationality affinity");
        var definitions = catalog.Definitions
            .GroupBy(definition => definition.Id, StringComparer.Ordinal)
            .Select(group => group.MaxBy(definition => definition.Version)!)
            .OrderBy(definition => definition.Id, StringComparer.Ordinal)
            .ToArray();
        if (definitions.Length != RacingDnaCatalog.IdentityCount)
            throw new InvalidOperationException("Random creation requires the complete 30-identity catalog.");

        definitions = definitions
            .Where(definition => HasEligibleChoiceContext(definition, rivals, nationalities))
            .ToArray();
        if (definitions.Length == 0)
            throw new InvalidOperationException("No Racing DNA identity is eligible for the supplied creation context.");

        var stream = new StreamFactory(seed)
            .CreateStream(CareerStreams.CharacterGen, year: 0, round: rerollOrdinal, entityId: "player");
        var definition = definitions[stream.NextInt(0, definitions.Length)];
        string? choice = DrawChoice(definition.Choice, rivals, nationalities, stream);

        var talent = new Dictionary<string, double>(definition.StartingStats, StringComparer.Ordinal);
        var meta = new Dictionary<string, double>(definition.StartingMeta, StringComparer.Ordinal);
        var stats = talent.Concat(meta)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        string[] traitIds = definition.StartingTraitIds.ToArray();
        string? chosenFlavor = traitIds.Contains("one_trick", StringComparer.Ordinal)
            ? PerkResolver.DefaultChosenFlavor
            : null;

        var profile = new CharacterProfile
        {
            Name = name.Trim(),
            Age = Math.Clamp(age, MinCreationAge, MaxCreationAge),
            Stats = stats,
            PerkIds = traitIds,
            CreationPerkIds = traitIds,
            ChosenFlavor = chosenFlavor,
            ProgressionVersion = CharacterLevelProgression.Level300Version,
            MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
            ExpectationModelVersion = CharacterProfile.CurrentExpectationModelVersion,
            RacingDnaId = definition.Id,
            RacingDnaVersion = definition.Version,
            RacingDnaChoice = choice,
            CreationBaseline = new CharacterCreationBaseline
            {
                Stats = talent,
                Meta = meta,
                TraitIds = traitIds,
                ChosenFlavor = chosenFlavor,
            },
        };

        catalog.ValidateCreation(profile);
        return profile;
    }

    private static string? DrawChoice(
        RacingDnaChoiceRule? rule,
        IReadOnlyList<string> rivals,
        IReadOnlyList<string> nationalities,
        Pcg32 stream)
    {
        if (rule is null)
            return null;
        IReadOnlyList<string> values = rule.Options.Count > 0
            ? rule.Options
            : rule.Kind switch
            {
                RacingDnaChoiceKind.RivalDriverId => rivals,
                RacingDnaChoiceKind.NationalityAffinity => nationalities,
                _ => [],
            };
        if (values.Count == 0 && rule.Required)
            throw new InvalidOperationException("The selected Racing DNA has no eligible context choice.");
        if (values.Count == 0)
            return null;
        return values[stream.NextInt(0, values.Count)];
    }

    private static bool HasEligibleChoiceContext(
        RacingDnaDefinition definition,
        IReadOnlyList<string> rivals,
        IReadOnlyList<string> nationalities)
    {
        var rule = definition.Choice;
        if (rule is null || rule.Options.Count > 0 || !rule.Required)
            return true;
        return rule.Kind switch
        {
            RacingDnaChoiceKind.RivalDriverId => rivals.Count > 0,
            RacingDnaChoiceKind.NationalityAffinity => nationalities.Count > 0,
            _ => true,
        };
    }

    private static IReadOnlyList<string> NormalizeChoices(IReadOnlyList<string> values, string label)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Any(string.IsNullOrWhiteSpace) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Count)
        {
            throw new ArgumentException($"Random Racing DNA {label} values must be nonblank and unique.");
        }
        return values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }
}
