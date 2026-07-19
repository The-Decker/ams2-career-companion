using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Core.Character;
using Companion.Core.Determinism;

namespace Companion.ViewModels.Wizard;

/// <summary>
/// The new-career wizard's character-creation step (docs/dev/character-system.md §5). Three tiers
/// over the same state: pick an ARCHETYPE preset (the one-click default, a pre-spent, in-budget
/// template), then optionally FREE-CUSTOMIZE the seven stat sliders and the perk shelf with a live
/// remaining-CP meter, with the raw numbers available for tinkerers. Produces a
/// <see cref="CharacterProfile"/> at confirm. Pure viewmodel, no I/O, fully unit-testable.
/// </summary>
public sealed partial class CharacterViewModel : ObservableObject
{
    private readonly CharacterRules _rules;
    private readonly RacingDnaCatalog? _racingDnaCatalog;
    private readonly RacingDnaChoiceContext _racingDnaChoiceContext;
    private ulong? _randomBuildSeed;

    public CharacterViewModel(
        CharacterRules rules,
        string? defaultName = null,
        RacingDnaCatalog? racingDnaCatalog = null,
        RacingDnaChoiceContext? racingDnaChoiceContext = null,
        int progressionVersion = CharacterLevelProgression.EraCappedVersion,
        long? masterSeed = null,
        MasterySkillCatalog? masterySkillCatalog = null,
        IReadOnlyList<CharacterCountryOption>? countryOptions = null)
    {
        _rules = rules;
        _racingDnaCatalog = racingDnaCatalog;
        _racingDnaChoiceContext = NormalizeChoiceContext(racingDnaChoiceContext);
        if (progressionVersion is not (CharacterLevelProgression.EraCappedVersion or
            CharacterLevelProgression.Level300Version))
        {
            throw new NotSupportedException(
                $"Character creation progression version {progressionVersion} is not supported.");
        }
        if (progressionVersion == CharacterLevelProgression.Level300Version && racingDnaCatalog is null)
            throw new ArgumentNullException(
                nameof(racingDnaCatalog),
                "Progression-v2 character creation requires the Racing DNA catalog.");
        IsProgressionV2 = progressionVersion == CharacterLevelProgression.Level300Version;
        CountryOptions = IsProgressionV2
            ? NormalizeCountryOptions(countryOptions ?? CharacterCountryCatalog.Available)
            : [];
        _randomBuildSeed = masterSeed is null ? null : unchecked((ulong)masterSeed.Value);
        _name = defaultName?.Trim() ?? "";

        Stats = _rules.Stats.TalentStats.Select(s =>
        {
            IReadOnlyList<double>? range = IsProgressionV2
                ? _racingDnaCatalog!.CreationBudget.TalentRanges[s.Id]
                : s.CreationRange;
            return new StatSlider(
                s.Id, Label(s.Id), Recompute,
                min: range is { Count: 2 } ? range[0] : 0.15,
                max: range is { Count: 2 } ? range[1] : 0.85,
                mapsTo: s.MapsTo, writeBase: s.WriteBase, writeSpan: s.WriteSpan);
        }).ToList();
        // Meta stats have no rating analog, so they range over the full 0–1 the data allows (not the
        // talent 0.15–0.85 band).
        MetaStats = _rules.Stats.MetaStats.Select(m =>
        {
            IReadOnlyList<double>? range = IsProgressionV2
                ? _racingDnaCatalog!.CreationBudget.MetaRanges[m.Id]
                : m.Range;
            return new StatSlider(
                m.Id, Label(m.Id), Recompute, initial: m.Default,
                min: range is { Count: 2 } ? range[0] : 0.0,
                max: range is { Count: 2 } ? range[1] : 1.0);
        }).ToList();

        Perks = _rules.Perks
            .Select(perk =>
            {
                IReadOnlyList<CharacterEffectLine> effects = PerkDescriber.Effects(perk);
                return new PerkOption(
                    perk.Id, perk.Name, perk.Category, perk.Cost, perk.Description,
                    PerkDescriber.Benefits(effects), PerkDescriber.Drawbacks(effects), effects);
            })
            .ToList();
        // A perk toggled directly (the shelf checkbox binds IsSelected) recomputes the CP meter,
        // just like the command path.
        foreach (var perk in Perks)
            perk.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PerkOption.IsSelected) && !_suppressRecompute)
                    Recompute();
            };
        PerkCategories = Perks
            .GroupBy(p => p.Category, StringComparer.Ordinal)
            .Select(g => new PerkCategory(g.Key, CharacterLabels.Category(g.Key), g.ToList()))
            .ToList();
        MasteryPreviewFamilies = IsProgressionV2 && masterySkillCatalog is not null
            ? BuildMasteryPreview(masterySkillCatalog)
            : [];
        MasteryPreviewAttributeRails = IsProgressionV2 && masterySkillCatalog is not null
            ? BuildAttributePreview(masterySkillCatalog, _rules)
            : [];

        // One-Trick Pony's specialism picker: the flavor ratings a player may lock onto (every
        // player-writable rating except raceSkill, which is the auto-taxed pace lever). Defaults to
        // the resolver's fallback so a never-touched picker still resolves consistently.
        EligibleFlavors = OneTrickFlavors
            .Select(f => new FlavorOption(f, CharacterLabels.Rating(f)))
            .ToList();
        _chosenFlavor = EligibleFlavors.FirstOrDefault(f => f.Field == PerkResolver.DefaultChosenFlavor)
            ?? EligibleFlavors[0];

        Archetypes = _rules.Creation.Archetypes.ToList();
        RacingDnaCards = racingDnaCatalog is null
            ? []
            : racingDnaCatalog.Definitions
                .GroupBy(definition => definition.Id, StringComparer.Ordinal)
                .Select(group => group.MaxBy(definition => definition.Version)!)
                .Select(ToCard)
                .ToArray();

        if (IsProgressionV2)
        {
            // V2 starts from one complete DNA preset. Selecting the card applies its creation
            // template; later slider/trait edits are advanced customization and do not erase DNA.
            SelectedRacingDna = RacingDnaCards.FirstOrDefault();
        }
        else
        {
            // The first archetype remains the default v1 one-click character.
            SelectedArchetype = Archetypes.FirstOrDefault();
        }
    }

    /// <summary>The five talent-stat sliders (clamped to each stat's creation range).</summary>
    public IReadOnlyList<StatSlider> Stats { get; }

    /// <summary>The two career meta-stat sliders (marketability, durability).</summary>
    public IReadOnlyList<StatSlider> MetaStats { get; }

    public IReadOnlyList<PerkOption> Perks { get; }

    /// <summary>Perks grouped by category for the shelf (pace, racecraft, physical, …).</summary>
    public IReadOnlyList<PerkCategory> PerkCategories { get; }

    /// <summary>The immutable legacy traits available at creation. This remains separate from the
    /// progression-v2 mastery catalog so previewing a career skill can never acquire it.</summary>
    public int CreationTraitCount => Perks.Count;

    /// <summary>All progression-v2 mastery skills grouped in authored family order for a read-only
    /// new-career preview. Empty for legacy creation and when no optional catalog was supplied.</summary>
    public IReadOnlyList<MasteryPreviewFamily> MasteryPreviewFamilies { get; }

    public int MasteryPreviewSkillCount =>
        MasteryPreviewFamilies.Sum(family => family.Skills.Count);

    /// <summary>All seven authored v2 attribute rails. These are career-SP purchases shown at
    /// creation for planning only; they never mutate the creation baseline.</summary>
    public IReadOnlyList<MasteryPreviewAttributeRail> MasteryPreviewAttributeRails { get; }

    public int MasteryPreviewAttributeRailCount => MasteryPreviewAttributeRails.Count;

    public int MasteryPreviewAttributeNodeCount =>
        MasteryPreviewAttributeRails.Sum(rail => rail.Nodes.Count);

    public int MasteryPreviewAttributeCost =>
        MasteryPreviewAttributeRails.Sum(rail => rail.TotalCost);

    /// <summary>Display-ready explanation of the team-first expectation model. The App binds this
    /// instead of restating Core coefficients in XAML.</summary>
    public string ExpectedPerformanceBasisSummary => IsProgressionV2
        ? "Your team sets most of the target: 60% comes from the car package and 10% from reliability. " +
          "Your Pace attribute starts the remaining 30% driver share."
        : "Round expectation is 60% car pace, 30% driver race skill and 10% reliability.";

    /// <summary>Display-ready explanation of the versioned performance-history feedback. New v2
    /// characters opt into this model at creation; existing profiles retain their original formula.</summary>
    public string ExpectedPerformanceCalibrationSummary => IsProgressionV2
        ? "As your career builds a race record, consistent performance makes a small adjustment to " +
          "the driver share. The team remains the main factor. Your pace history also helps tune the " +
          "recommended AMS2 opponent skill."
        : "Race history updates OPI and the recommended opponent setting without rewriting this legacy profile's expectation formula.";

    /// <summary>The three authored shares, ready for a telemetry-style Advanced breakdown.</summary>
    public IReadOnlyList<CharacterExpectationComponent> ExpectedPerformanceComponents => IsProgressionV2
        ?
        [
            new("TEAM CAR", 60, "Your team's power, weight and drag package."),
            new("DRIVER FORM", 30, "Your starting Pace, followed by a small adjustment from consistent performance over time."),
            new("RELIABILITY", 10, "How often the team is expected to deliver a classified finish."),
        ]
        :
        [
            new("TEAM CAR", 60, "Power, weight and drag from the selected team and car."),
            new("DRIVER RATING", 30, "The profile's fixed race-skill rating; OPI does not recalibrate this legacy model."),
            new("RELIABILITY", 10, "The team's likelihood of delivering the car to the finish."),
        ];

    /// <summary>Compact, truthful status-ribbon copy for progression v2.</summary>
    public string ProgressionSummary => IsProgressionV2
        ? $"LEVEL 300 | 499 TOTAL SKILL POINTS | {MasteryPreviewSkillCount} SKILLS / {MasteryPreviewFamilies.Count} FAMILIES" +
          $" | {MasteryPreviewAttributeNodeCount} ATTRIBUTE STEPS / {MasteryPreviewAttributeRailCount} PATHS"
        : "LEGACY CHARACTER PROGRESSION";

    public IReadOnlyList<Archetype> Archetypes { get; }

    /// <summary>True only for an explicitly opted-in v2 creation VM. Merely loading the catalog
    /// never changes a legacy wizard's behavior.</summary>
    public bool IsProgressionV2 { get; }

    /// <summary>The latest definition version of each of the 30 immutable identities, in authored
    /// catalog order. Existing <see cref="Archetypes"/> remains intact for v1 bindings.</summary>
    public IReadOnlyList<RacingDnaCardViewModel> RacingDnaCards { get; }

    /// <summary>The immutable, stably ordered countries whose flag art ships with the app. The
    /// three-letter code is persisted; the label and flag key are presentation-only.</summary>
    public IReadOnlyList<CharacterCountryOption> CountryOptions { get; }

    /// <summary>The player's authored nationality. Progression-v2 creation requires an explicit
    /// choice; Random Build never changes it or consumes an RNG draw for it.</summary>
    [ObservableProperty]
    private CharacterCountryOption? _selectedCountry;

    public bool IsCountryValid => !IsProgressionV2 ||
        SelectedCountry is { } selected && CountryOptions.Any(option =>
            string.Equals(option.Code, selected.Code, StringComparison.Ordinal));

    public string CountrySummary => SelectedCountry is { } country
        ? country.Name.ToUpperInvariant()
        : "COUNTRY NOT SELECTED";

    partial void OnSelectedCountryChanged(CharacterCountryOption? value)
    {
        OnPropertyChanged(nameof(IsCountryValid));
        OnPropertyChanged(nameof(CountrySummary));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(Invalidity));
        OnPropertyChanged(nameof(ValidationIssues));
        OnPropertyChanged(nameof(BuildReadinessSummary));
    }

    /// <summary>The player's driver name, the identity the whole app will use. It is created before
    /// seat selection, so its default is deliberately independent of the historical driver.</summary>
    [ObservableProperty]
    private string _name;

    public bool IsDriverNameValid => !IsProgressionV2 || !string.IsNullOrWhiteSpace(Name);

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(IsDriverNameValid));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(Invalidity));
        OnPropertyChanged(nameof(ValidationIssues));
        OnPropertyChanged(nameof(BuildReadinessSummary));
    }

    /// <summary>Youngest / oldest a created driver can be, and the default (a typical rookie).</summary>
    public const int MinAge = 16;
    public const int MaxAge = 45;
    public const int DefaultAge = 23;

    /// <summary>The driver's REAL age in their first season, the character's own age (16–45), which
    /// drives the sim's season-end aging and the contract-offer age risk. A young driver has years of
    /// growth ahead; a veteran is courted more warily. Independent of the historical seat.</summary>
    [ObservableProperty]
    private int _age = DefaultAge;

    [ObservableProperty]
    private Archetype? _selectedArchetype;

    partial void OnSelectedArchetypeChanged(Archetype? value)
    {
        if (value is null)
            return;
        ApplyArchetype(value);
    }

    [ObservableProperty]
    private RacingDnaCardViewModel? _selectedRacingDna;

    private IReadOnlyList<RacingDnaChoiceOption> _racingDnaChoiceOptions = [];

    /// <summary>Static catalog options or the stable ID/display pairs supplied by creation context
    /// for a rival or nationality affinity.</summary>
    public IReadOnlyList<RacingDnaChoiceOption> RacingDnaChoiceOptions => _racingDnaChoiceOptions;

    [ObservableProperty]
    private string? _racingDnaChoiceValue;

    public bool HasRacingDnaChoice => SelectedRacingDna?.ChoiceKind is not null;

    public bool IsRacingDnaChoiceRequired =>
        SelectedRacingDna is { } selected &&
        _racingDnaCatalog!.Get(selected.Id, selected.Version).Choice?.Required == true;

    public string? RacingDnaChoicePrompt =>
        SelectedRacingDna is { } selected
            ? _racingDnaCatalog!.Get(selected.Id, selected.Version).Choice?.Prompt
            : null;

    public bool IsRacingDnaChoiceValid
    {
        get
        {
            if (!IsProgressionV2)
                return true;
            if (SelectedRacingDna is not { } selected)
                return false;

            var rule = _racingDnaCatalog!.Get(selected.Id, selected.Version).Choice;
            if (rule is null)
                return RacingDnaChoiceValue is null;
            if (string.IsNullOrWhiteSpace(RacingDnaChoiceValue))
                return !rule.Required;
            return RacingDnaChoiceOptions.Any(option =>
                string.Equals(option.Value, RacingDnaChoiceValue, StringComparison.Ordinal));
        }
    }

    partial void OnSelectedRacingDnaChanged(RacingDnaCardViewModel? value)
    {
        RacingDnaChoiceValue = null;
        _racingDnaChoiceOptions = value is null ? [] : ChoiceOptions(value);
        OnPropertyChanged(nameof(RacingDnaChoiceOptions));
        if (value is not null)
            ApplyRacingDna(value);
        RecomputeRacingDna();
    }

    partial void OnRacingDnaChoiceValueChanged(string? value) => RecomputeRacingDna();

    /// <summary>Selected permanent identity plus its two authored mastery affinities.</summary>
    public string RacingDnaSummary => SelectedRacingDna is { } dna
        ? $"{dna.Name} v{dna.Version}  ·  {dna.FamilyLine}" + RacingDnaChoiceSummary
        : IsProgressionV2 ? "Choose a Racing DNA identity" : "Legacy archetype";

    private string RacingDnaChoiceSummary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RacingDnaChoiceValue))
                return "";
            string label = RacingDnaChoiceOptions.FirstOrDefault(option =>
                    string.Equals(option.Value, RacingDnaChoiceValue, StringComparison.Ordinal))?.Label
                ?? RacingDnaChoiceValue;
            return $"  ·  {label}";
        }
    }

    // ---- One-Trick Pony specialism ----

    private const string OneTrickPerkId = "one_trick";

    /// <summary>The rating fields One-Trick Pony may lock onto, every player-writable flavor rating
    /// except raceSkill (the auto-taxed pace lever stays out of the specialism).</summary>
    private static readonly IReadOnlyList<string> OneTrickFlavors = PerkResolver.EligibleChosenFlavors;

    /// <summary>The specialism options shown when One-Trick Pony is picked.</summary>
    public IReadOnlyList<FlavorOption> EligibleFlavors { get; }

    /// <summary>The player's chosen One-Trick specialism (the rating its +0.30 lands on and the only
    /// stat in-career development may raise). Only meaningful when <see cref="IsOneTrickSelected"/>.</summary>
    [ObservableProperty]
    private FlavorOption _chosenFlavor;

    /// <summary>True when the build carries One-Trick Pony, so the specialism picker is shown.</summary>
    public bool IsOneTrickSelected =>
        Perks.Any(p => p.IsSelected && string.Equals(p.Id, OneTrickPerkId, StringComparison.Ordinal));

    /// <summary>The creation trait budget. V2 reads its separately versioned catalog budget; v1
    /// keeps the shipped CP rule exactly.</summary>
    public int Budget => IsProgressionV2
        ? _racingDnaCatalog!.CreationBudget.TraitBudget
        : _rules.CharacterPoints.CreationBudget;

    /// <summary>The MOST perk points a build may spend, the budget plus the drawback refund headroom
    /// (9 = 6 + 3 in the shipped rules): taking a drawback-heavy perk refunds points you can pour into
    /// one premium upside. Displayed so the meter never reads a nonsensical "8 of 6".</summary>
    public int MaxPerkPoints => IsProgressionV2
        ? _racingDnaCatalog!.CreationBudget.TraitSpendMax
        : _rules.CharacterPoints.MaxNetSpend;

    /// <summary>Net CP the selected perks cost (refund perks are negative).</summary>
    public int NetCpSpend => Perks.Where(p => p.IsSelected).Sum(p => p.Cost);

    /// <summary>Perk CP left over against the budget (never negative for display).</summary>
    public int RemainingCp => Math.Max(0, Budget - NetCpSpend);

    /// <summary>How many perks are chosen right now (an archetype supplies its signature few).</summary>
    public int SelectedPerkCount => Perks.Count(p => p.IsSelected);

    /// <summary>The most perks a creation build may carry, or null for no count limit.</summary>
    public int? MaxPerks => IsProgressionV2
        ? _racingDnaCatalog!.CreationBudget.MaxTraits
        : _rules.CharacterPoints.MaxPerks;

    /// <summary>Perks fit the count cap, an archetype plus only a few more. No cap = always true.</summary>
    public bool PerksWithinCount => MaxPerks is not int cap || SelectedPerkCount <= cap;

    /// <summary>Perks fit the audited CP window [minBudgetAfterSpend, budget + maxRefundHeadroom].</summary>
    public bool PerksInBudget =>
        NetCpSpend >= (IsProgressionV2
            ? _racingDnaCatalog!.CreationBudget.TraitSpendMin
            : _rules.CharacterPoints.MinBudgetAfterSpend)
        && NetCpSpend <= MaxPerkPoints;

    /// <summary>The most total talent a driver may carry across the seven stats. Redistribution is
    /// free, being a 0.85 somewhere means being low elsewhere, but the SUM is capped, so no driver
    /// is great at everything. Data-driven (perks.json).</summary>
    public double StatCap => IsProgressionV2
        ? _racingDnaCatalog!.CreationBudget.StatSumCap
        : _rules.CharacterPoints.StatSumCap;

    /// <summary>The current total across all seven stats.</summary>
    public double StatTotal => Stats.Concat(MetaStats).Sum(s => s.Value);

    public double TalentStatTotal => Stats.Sum(stat => stat.Value);

    public double MetaStatTotal => MetaStats.Sum(stat => stat.Value);

    public string AttributeBuildSummary =>
        $"{Stats.Count} DRIVING + {MetaStats.Count} CAREER ATTRIBUTES | {StatTotal:0.00} / {StatCap:0.00}";

    public string CreationTraitSummary =>
        $"{SelectedPerkCount} / {MaxPerks?.ToString() ?? "NO CAP"} SELECTED | {CreationTraitCount} AVAILABLE";

    public string CreationBudgetSummary => IsProgressionV2
        ? $"{NetCpSpend} CREATION POINTS USED | {RemainingCp} LEFT | UNUSED POINTS DO NOT CARRY"
        : $"{NetCpSpend} USED | {RemainingCp} REMAINING";

    public bool StatsWithinCap => StatTotal <= StatCap + 1e-9;

    /// <summary>A build is valid when its perks fit the count cap AND the CP budget AND its stats
    /// fit the talent cap.</summary>
    public bool IsValid => IsDriverNameValid && IsCountryValid &&
                           PerksWithinCount && PerksInBudget && StatsWithinCap &&
                           (!IsProgressionV2 ||
                            SelectedRacingDna is not null && IsRacingDnaChoiceValid);

    /// <summary>Every current build blocker in stable display order. The old one-line property remains
    /// as the first issue for binding compatibility.</summary>
    public IReadOnlyList<string> ValidationIssues
    {
        get
        {
            var issues = new List<string>();
            if (!IsDriverNameValid)
                issues.Add("Enter your driver's name.");
            if (!IsCountryValid)
                issues.Add("Choose your driver's country.");
            if (IsProgressionV2 && SelectedRacingDna is null)
                issues.Add("Choose a Racing DNA identity.");
            else if (IsProgressionV2 && !IsRacingDnaChoiceValid)
                issues.Add(IsRacingDnaChoiceRequired
                    ? $"Choose {RacingDnaChoicePrompt ?? "the required Racing DNA context"}."
                    : "The Racing DNA context choice is not valid.");

            if (!StatsWithinCap)
                issues.Add($"Your talent and career attributes total {StatTotal:0.00} of {StatCap:0.00}; lower one to raise another.");
            if (!PerksWithinCount)
                issues.Add($"You've picked {SelectedPerkCount} starting traits; a driver carries at most {MaxPerks}. Drop one.");

            int minimum = IsProgressionV2
                ? _racingDnaCatalog!.CreationBudget.TraitSpendMin
                : _rules.CharacterPoints.MinBudgetAfterSpend;
            if (NetCpSpend < minimum)
                issues.Add(IsProgressionV2
                    ? "This build leaves more creation points unused than allowed."
                    : $"This build banks more perk points than allowed; spend at least {minimum}.");
            if (NetCpSpend > MaxPerkPoints)
                issues.Add(IsProgressionV2
                    ? $"These starting traits cost {NetCpSpend} of a {MaxPerkPoints} maximum; drop one."
                    : $"These perks cost {NetCpSpend} of a {MaxPerkPoints} maximum; drop one.");
            return issues;
        }
    }

    public string? Invalidity => ValidationIssues.FirstOrDefault();

    public string BuildReadinessSummary => IsValid
        ? IsProgressionV2
            ? "BUILD READY | Your DNA, country, attributes and starting traits will be saved with this career."
            : "BUILD READY | Attributes and perks will be locked into the new career."
        : $"BUILD INCOMPLETE | {ValidationIssues.Count} ISSUE{(ValidationIssues.Count == 1 ? "" : "S")}";

    private bool _suppressRecompute;

    [RelayCommand]
    private void TogglePerk(PerkOption? perk)
    {
        if (perk is null)
            return;
        perk.IsSelected = !perk.IsSelected; // the perk's PropertyChanged recomputes the meter
    }

    /// <summary>Applies an archetype preset: its stat spread and its exact perk set. Selecting a
    /// preset is a complete, valid character; the player may then customize from there.</summary>
    private void ApplyArchetype(Archetype archetype)
    {
        foreach (var slider in Stats)
            if (archetype.StartStats.TryGetValue(slider.Id, out double value))
                slider.SetSilently(value);
        foreach (var slider in MetaStats)
            if (archetype.StartMeta.TryGetValue(slider.Id, out double value))
                slider.SetSilently(value);

        // Apply the whole perk set, then recompute once (each perk's PropertyChanged is suppressed).
        _suppressRecompute = true;
        var chosen = new HashSet<string>(archetype.PerkIds, StringComparer.Ordinal);
        foreach (var perk in Perks)
            perk.IsSelected = chosen.Contains(perk.Id);
        _suppressRecompute = false;

        Recompute();
    }

    private void ApplyRacingDna(RacingDnaCardViewModel card)
    {
        var definition = _racingDnaCatalog!.Get(card.Id, card.Version);
        foreach (var slider in Stats)
            if (definition.StartingStats.TryGetValue(slider.Id, out double value))
                slider.SetSilently(value);
        foreach (var slider in MetaStats)
            if (definition.StartingMeta.TryGetValue(slider.Id, out double value))
                slider.SetSilently(value);

        _suppressRecompute = true;
        var chosen = definition.StartingTraitIds.ToHashSet(StringComparer.Ordinal);
        foreach (var perk in Perks)
            perk.IsSelected = chosen.Contains(perk.Id);
        _suppressRecompute = false;

        // V1 selection remains a live compatibility binding, but a DNA preset is the active v2
        // identity. Advanced customization may now edit the applied values without clearing it.
        SelectedArchetype = null;
        Recompute();
    }

    private void Recompute()
    {
        OnPropertyChanged(nameof(NetCpSpend));
        OnPropertyChanged(nameof(RemainingCp));
        OnPropertyChanged(nameof(SelectedPerkCount));
        OnPropertyChanged(nameof(PerksWithinCount));
        OnPropertyChanged(nameof(PerksInBudget));
        OnPropertyChanged(nameof(StatTotal));
        OnPropertyChanged(nameof(TalentStatTotal));
        OnPropertyChanged(nameof(MetaStatTotal));
        OnPropertyChanged(nameof(AttributeBuildSummary));
        OnPropertyChanged(nameof(CreationTraitSummary));
        OnPropertyChanged(nameof(CreationBudgetSummary));
        OnPropertyChanged(nameof(StatsWithinCap));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(Invalidity));
        OnPropertyChanged(nameof(ValidationIssues));
        OnPropertyChanged(nameof(BuildReadinessSummary));
        OnPropertyChanged(nameof(IsOneTrickSelected));
    }

    private void RecomputeRacingDna()
    {
        OnPropertyChanged(nameof(HasRacingDnaChoice));
        OnPropertyChanged(nameof(IsRacingDnaChoiceRequired));
        OnPropertyChanged(nameof(RacingDnaChoicePrompt));
        OnPropertyChanged(nameof(RacingDnaChoiceOptions));
        OnPropertyChanged(nameof(IsRacingDnaChoiceValid));
        OnPropertyChanged(nameof(RacingDnaSummary));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(Invalidity));
        OnPropertyChanged(nameof(ValidationIssues));
        OnPropertyChanged(nameof(BuildReadinessSummary));
    }

    private IReadOnlyList<RacingDnaChoiceOption> ChoiceOptions(RacingDnaCardViewModel card)
    {
        var rule = _racingDnaCatalog!.Get(card.Id, card.Version).Choice;
        if (rule is null)
            return [];
        if (rule.Options.Count > 0)
            return rule.Options.Select(value => new RacingDnaChoiceOption(value, ChoiceLabel(value))).ToArray();
        return rule.Kind switch
        {
            RacingDnaChoiceKind.RivalDriverId => _racingDnaChoiceContext.RivalDrivers,
            RacingDnaChoiceKind.NationalityAffinity => _racingDnaChoiceContext.Nationalities,
            _ => [],
        };
    }

    private RacingDnaCardViewModel ToCard(RacingDnaDefinition definition) => new()
    {
        Id = definition.Id,
        Version = definition.Version,
        Name = definition.Name,
        Description = definition.Description,
        PrimaryFamily = definition.PrimaryFamily,
        PrimaryFamilyLabel = CharacterLabels.Category(definition.PrimaryFamily),
        SecondaryFamily = definition.SecondaryFamily,
        SecondaryFamilyLabel = CharacterLabels.Category(definition.SecondaryFamily),
        StartingTraitIds = definition.StartingTraitIds.ToArray(),
        StartingTraitNames = definition.StartingTraitIds
            .Select(id => _rules.PerkById(id).Name)
            .ToArray(),
        PersistentEffects = definition.PersistentEffects,
        TradeoffEffects = definition.TradeoffEffects,
        ChoiceKind = definition.Choice?.Kind,
        ChoicePrompt = definition.Choice?.Prompt,
        ChoiceRequired = definition.Choice?.Required == true,
    };

    private static RacingDnaChoiceContext NormalizeChoiceContext(RacingDnaChoiceContext? context) => new(
        NormalizeChoiceOptions(context?.RivalDrivers, "rival driver"),
        NormalizeChoiceOptions(context?.Nationalities, "nationality"));

    private static IReadOnlyList<RacingDnaChoiceOption> NormalizeChoiceOptions(
        IReadOnlyList<RacingDnaChoiceOption>? options,
        string label)
    {
        if (options is null)
            return [];
        if (options.Any(option =>
                string.IsNullOrWhiteSpace(option.Value) || string.IsNullOrWhiteSpace(option.Label)) ||
            options.Select(option => option.Value).Distinct(StringComparer.Ordinal).Count() != options.Count)
        {
            throw new ArgumentException($"Racing DNA {label} choices must have unique stable values and labels.");
        }
        return options
            .OrderBy(option => option.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ChoiceLabel(string value) => value switch
    {
        "highSpeed" => "High-speed",
        _ => char.ToUpperInvariant(value[0]) + value[1..],
    };

    private static IReadOnlyList<MasteryPreviewFamily> BuildMasteryPreview(
        MasterySkillCatalog catalog)
    {
        var names = catalog.Skills.ToDictionary(
            skill => skill.Id,
            skill => skill.Name,
            StringComparer.Ordinal);

        return catalog.FamilyOrder.Select(family => new MasteryPreviewFamily
        {
            Id = family,
            Name = CharacterLabels.Category(family),
            Skills = catalog.Skills
                .Where(skill => string.Equals(skill.Family, family, StringComparison.Ordinal))
                .Select(skill => new MasteryPreviewSkill
                {
                    Id = skill.Id,
                    Name = skill.Name,
                    Description = skill.Description,
                    Family = skill.Family,
                    Tier = skill.Tier,
                    Order = skill.Order,
                    IconKey = skill.IconKey,
                    Cost = skill.Cost,
                    UnlockLevel = skill.UnlockLevel,
                    ExclusiveGroup = skill.ExclusiveGroup,
                    RequiresIds = skill.Requires.ToArray(),
                    RequiresLabels = skill.Requires
                        .Select(id => names.GetValueOrDefault(id, id))
                        .ToArray(),
                    Benefits = skill.Benefits.ToArray(),
                    Drawbacks = skill.Drawbacks.ToArray(),
                    Effects = skill.Effects.Select(MasterySkillGraph.DescribeEffect).ToArray(),
                })
                .ToArray(),
        }).ToArray();
    }

    private static IReadOnlyList<MasteryPreviewAttributeRail> BuildAttributePreview(
        MasterySkillCatalog catalog,
        CharacterRules rules)
    {
        var names = catalog.AttributeNodes.ToDictionary(
            node => node.Id,
            node => node.Name,
            StringComparer.Ordinal);

        return catalog.AttributeRails.Select(rail =>
        {
            CharacterEffectClass classification = rules.Stats.TalentStats.Any(stat =>
                string.Equals(stat.Id, rail.Stat, StringComparison.Ordinal))
                ? CharacterEffectClass.Expectation
                : CharacterEffectClass.Career;
            MasteryPreviewAttributeNode[] nodes = catalog.AttributeNodes
                .Where(node => string.Equals(node.RailId, rail.Id, StringComparison.Ordinal))
                .OrderBy(node => node.Order)
                .ThenBy(node => node.Id, StringComparer.Ordinal)
                .Select(node =>
                {
                    return new MasteryPreviewAttributeNode
                    {
                        Id = node.Id,
                        Name = node.Name,
                        Tier = node.Tier,
                        Order = node.Order,
                        IconKey = node.IconKey,
                        Cost = node.Cost,
                        UnlockLevel = node.UnlockLevel,
                        StepValue = node.StepValue,
                        CapValue = node.CapValue,
                        RequiresIds = node.Requires.ToArray(),
                        RequiresLabels = node.Requires
                            .Select(id => names.GetValueOrDefault(id, id))
                            .ToArray(),
                        Effects =
                        [
                            MasterySkillGraph.DescribeAttributeEffect(rail, node, classification),
                        ],
                    };
                })
                .ToArray();

            return new MasteryPreviewAttributeRail
            {
                Id = rail.Id,
                Name = rail.Name,
                StatId = rail.Stat,
                StatLabel = CharacterLabels.Rating(rail.Stat),
                Family = rail.Family,
                FamilyLabel = CharacterLabels.Category(rail.Family),
                IconKey = rail.IconKey,
                StepValue = rail.StepValue,
                CapValue = rail.CapValue,
                CostPerStep = rail.CostPerStep,
                StepCount = rail.StepCount,
                TotalCost = nodes.Sum(node => node.Cost),
                Nodes = nodes,
            };
        }).ToArray();
    }

    /// <summary>The authored character this step produces at confirm.</summary>
    public CharacterProfile BuildProfile()
    {
        var stats = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var slider in Stats.Concat(MetaStats))
            stats[slider.Id] = slider.Value;

        var perkIds = Perks.Where(p => p.IsSelected).Select(p => p.Id).ToList();
        return new CharacterProfile
        {
            Name = Name.Trim(),
            Age = Math.Clamp(Age, MinAge, MaxAge),
            Stats = stats,
            PerkIds = perkIds,
            CreationPerkIds = perkIds,
            ProgressionVersion = 1,
            // Record the chosen specialism only when One-Trick Pony is actually taken, so a build
            // without it serialises with no ChosenFlavor (byte-identical to a legacy profile).
            ChosenFlavor = IsOneTrickSelected ? ChosenFlavor.Field : null,
            CpUnspent = RemainingCp,
        };
    }

    /// <summary>Builds the complete lossless progression-v2 creation snapshot. This is deliberately
    /// separate from <see cref="BuildProfile"/> so legacy wizard callers cannot opt into v2 by
    /// accident; the owning wizard must pair it atomically with an explicit experience mode.</summary>
    public CharacterProfile BuildVersionTwoProfile()
    {
        if (!IsProgressionV2 || _racingDnaCatalog is null)
            throw new InvalidOperationException("This character editor was not created for progression v2.");
        if (!IsValid)
            throw new InvalidOperationException(Invalidity ?? "The progression-v2 character is invalid.");
        var dna = SelectedRacingDna
            ?? throw new InvalidOperationException("Choose a Racing DNA identity.");

        var talent = Stats.ToDictionary(slider => slider.Id, slider => slider.Value, StringComparer.Ordinal);
        var meta = MetaStats.ToDictionary(slider => slider.Id, slider => slider.Value, StringComparer.Ordinal);
        var combined = talent.Concat(meta)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var traitIds = Perks.Where(perk => perk.IsSelected).Select(perk => perk.Id).ToArray();
        string? chosenFlavor = IsOneTrickSelected ? ChosenFlavor.Field : null;
        string? choice = HasRacingDnaChoice ? RacingDnaChoiceValue?.Trim() : null;

        var profile = new CharacterProfile
        {
            Name = Name.Trim(),
            CountryCode = SelectedCountry!.Code,
            Age = Math.Clamp(Age, MinAge, MaxAge),
            Stats = combined,
            PerkIds = traitIds,
            CreationPerkIds = traitIds,
            ChosenFlavor = chosenFlavor,
            ProgressionVersion = CharacterLevelProgression.Level300Version,
            MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
            ExpectationModelVersion = CharacterProfile.CurrentExpectationModelVersion,
            RacingDnaId = dna.Id,
            RacingDnaVersion = dna.Version,
            RacingDnaChoice = choice,
            CreationBaseline = new CharacterCreationBaseline
            {
                Stats = talent,
                Meta = meta,
                TraitIds = traitIds,
                ChosenFlavor = chosenFlavor,
            },
        };

        _racingDnaCatalog.ValidateCreation(profile);
        return profile;
    }

    // ---- Random balanced build (character-gen) ----

    private int _rerollIndex;

    /// <summary>Rolls a complete, valid, in-budget character: a random archetype base (its perks are
    /// valid by construction) with a freshly randomised stat spread under the talent cap. Every click
    /// advances a deterministic PCG so the sequence is stable, and the RESULT is what gets journaled
    /// at Confirm (in the <c>player.character</c> row), so the career always replays byte-for-byte —
    /// the roll itself is never re-run. Mirrors the archetype-apply path (SetSilently + Recompute).</summary>
    [RelayCommand(CanExecute = nameof(CanRandomBuild))]
    private void RandomBuild()
    {
        if (IsProgressionV2)
        {
            if (!CanRandomBuild())
                return;

            var context = new RacingDnaRandomContext
            {
                EligibleRivalDriverIds = _racingDnaChoiceContext.RivalDrivers
                    .Select(option => option.Value)
                    .ToArray(),
                NationalityAffinities = _racingDnaChoiceContext.Nationalities
                    .Select(option => option.Value)
                    .ToArray(),
            };
            var profile = RacingDnaRandomBuild.Create(
                _racingDnaCatalog!, _randomBuildSeed!.Value, _rerollIndex,
                context, Name, Age);
            _rerollIndex++;
            ApplyRandomVersionTwoProfile(profile);
            return;
        }
        if (Archetypes.Count == 0)
            return;

        // Seeded off a fixed base + the re-roll counter: successive clicks give different builds, and
        // because the chosen build is journaled, replay never depends on this stream.
        var rng = new Pcg32(0x9E3779B97F4A7C15UL ^ (ulong)_rerollIndex, 0xDA3E39CB94B95BDBUL);
        _rerollIndex++;

        var archetype = Archetypes[(int)(rng.NextUInt32() % (uint)Archetypes.Count)];

        _suppressRecompute = true;
        var chosen = new HashSet<string>(archetype.PerkIds, StringComparer.Ordinal);
        foreach (var perk in Perks)
            perk.IsSelected = chosen.Contains(perk.Id);
        RandomiseStats(rng);
        // A random specialism for a rolled One-Trick build.
        if (chosen.Contains(OneTrickPerkId) && EligibleFlavors.Count > 0)
            ChosenFlavor = EligibleFlavors[(int)(rng.NextUInt32() % (uint)EligibleFlavors.Count)];
        _suppressRecompute = false;

        SelectedArchetype = null; // the spread is no longer the preset's, clear the selection
        Recompute();
    }

    /// <summary>Distributes the talent budget randomly: each stat starts at its floor, then the
    /// remaining cap mass is sprinkled across the stats (each capped at its ceiling), so the result is
    /// always in-range and under the sum cap, a valid spread with no rejection loop.</summary>
    private bool CanRandomBuild() => IsProgressionV2
        ? _randomBuildSeed.HasValue &&
          _racingDnaCatalog is not null
        : Archetypes.Count > 0;

    internal void SetRandomBuildSeed(long masterSeed)
    {
        ulong seed = unchecked((ulong)masterSeed);
        if (_randomBuildSeed == seed)
            return;
        _randomBuildSeed = seed;
        _rerollIndex = 0;
        RandomBuildCommand.NotifyCanExecuteChanged();
    }

    internal void ClearRandomBuildSeed()
    {
        if (_randomBuildSeed is null)
            return;
        _randomBuildSeed = null;
        _rerollIndex = 0;
        RandomBuildCommand.NotifyCanExecuteChanged();
    }

    private void ApplyRandomVersionTwoProfile(CharacterProfile profile)
    {
        var card = RacingDnaCards.Single(candidate =>
            string.Equals(candidate.Id, profile.RacingDnaId, StringComparison.Ordinal) &&
            candidate.Version == profile.RacingDnaVersion);
        SelectedRacingDna = card;
        Name = profile.Name;
        Age = profile.Age ?? DefaultAge;
        foreach (var slider in Stats.Concat(MetaStats))
            slider.SetSilently(profile.Stat(slider.Id));

        _suppressRecompute = true;
        var chosen = profile.PerkIds.ToHashSet(StringComparer.Ordinal);
        foreach (var perk in Perks)
            perk.IsSelected = chosen.Contains(perk.Id);
        _suppressRecompute = false;

        if (profile.ChosenFlavor is { } flavor)
            ChosenFlavor = EligibleFlavors.Single(option => option.Field == flavor);
        RacingDnaChoiceValue = profile.RacingDnaChoice;
        SelectedArchetype = null;
        Recompute();
        RecomputeRacingDna();
    }

    private void RandomiseStats(Pcg32 rng)
    {
        var sliders = Stats.Concat(MetaStats).ToList();
        double floorSum = sliders.Sum(s => s.Min);
        double budget = Math.Max(0, StatCap - floorSum);
        foreach (var s in sliders)
            s.SetSilently(s.Min);

        // Hand out the mass in small increments to random stats that still have headroom.
        const double grain = 0.05;
        int steps = (int)(budget / grain);
        for (int i = 0; i < steps; i++)
        {
            var open = sliders.Where(s => s.Value + grain <= s.Max + 1e-9).ToList();
            if (open.Count == 0)
                break;
            var pick = open[(int)(rng.NextUInt32() % (uint)open.Count)];
            pick.SetSilently(pick.Value + grain);
        }
    }

    private static string Label(string id) => CharacterLabels.Stat(id);

    private static IReadOnlyList<CharacterCountryOption> NormalizeCountryOptions(
        IReadOnlyList<CharacterCountryOption> options)
    {
        if (options.Count == 0 || options.Any(option =>
                option.Code.Length != 3 || option.Code.Any(ch => ch is < 'A' or > 'Z') ||
                string.IsNullOrWhiteSpace(option.Name) || string.IsNullOrWhiteSpace(option.FlagKey)) ||
            options.Select(option => option.Code).Distinct(StringComparer.Ordinal).Count() != options.Count ||
            options.Select(option => option.Name).Distinct(StringComparer.Ordinal).Count() != options.Count ||
            options.Select(option => option.FlagKey).Distinct(StringComparer.Ordinal).Count() != options.Count)
        {
            throw new ArgumentException(
                "Character country choices must have unique uppercase three-letter codes, names and flag keys.",
                nameof(options));
        }

        return options
            .OrderBy(option => option.Name, StringComparer.Ordinal)
            .ThenBy(option => option.Code, StringComparer.Ordinal)
            .ToArray();
    }
}

/// <summary>One stat slider, clamped to its authored creation band (0.15–0.85 for talent stats,
/// 0–1 for meta). A talent slider also carries its stat→rating mapping so the "advanced" numbers can
/// show what it writes live. Raises a recompute callback on user edits (not on preset application).</summary>
public sealed partial class StatSlider : ObservableObject
{
    private readonly Action _onChanged;
    private readonly IReadOnlyList<string> _mapsTo;
    private readonly double _writeBase;
    private readonly double _writeSpan;
    private bool _suppress;

    public StatSlider(
        string id, string label, Action onChanged, double initial = 0.5,
        double min = 0.15, double max = 0.85,
        IReadOnlyList<string>? mapsTo = null, double writeBase = 0.35, double writeSpan = 0.55)
    {
        Id = id;
        Label = label;
        _onChanged = onChanged;
        Min = min;
        Max = max;
        _mapsTo = mapsTo ?? [];
        _writeBase = writeBase;
        _writeSpan = writeSpan;
        _value = Math.Clamp(initial, Min, Max);
    }

    public string Id { get; }
    public string Label { get; }

    /// <summary>Plain-language creation help. This describes the real player-facing consumer and
    /// explicitly avoids presenting AI-only ratings as hidden player-car performance.</summary>
    public string CreationGuidance => Id switch
    {
        "pace" => "Sets the starting driver share of your result target and helps calibrate recommended AMS2 opponent skill. Higher Pace means tougher expectations, not a faster car.",
        "oneLap" => "Describes your qualifying identity and written qualifying rating. It does not increase the human-driven car's speed.",
        "craft" => "Describes composure and mistake-avoidance for driver identity and career evaluation. It does not steer the car.",
        "racecraft" => "Describes overtaking and defending identity used by career recognition and stories. It does not change player-car performance.",
        "adaptability" => "Describes wet-weather and tyre-management identity for condition-based career systems. It does not change player-car performance.",
        "marketability" => "Affects reputation, contract interest, salary and sponsor value. It does not affect lap time.",
        "durability" => "Affects aging, retirement risk, injury-related career systems and long-term team value. It does not affect lap time.",
        _ => "Sets this driver's starting profile value. Only effects marked CAR can change player-car weight, power or drag.",
    };

    public double Min { get; }
    public double Max { get; }

    [ObservableProperty]
    private double _value;

    /// <summary>The rating field(s) this stat writes and the value it writes them to, for the
    /// "advanced" disclosure (e.g. "wet-weather pace 0.80 · tyre management 0.80"). Empty for a meta
    /// stat, which has no rating analog. Updates live as the slider moves.</summary>
    public string WrittenPreview => _mapsTo.Count == 0
        ? ""
        : string.Join("  ·  ", _mapsTo.Select(f =>
            $"{Companion.Core.Character.CharacterLabels.Rating(f)} {Math.Clamp(_writeBase + _writeSpan * Value, 0, 1):0.00}"));

    partial void OnValueChanged(double value)
    {
        double clamped = Math.Clamp(value, Min, Max);
        if (clamped != value)
        {
            _value = clamped; // re-clamp without re-entrancy; the setter already ran
        }
        OnPropertyChanged(nameof(WrittenPreview));
        if (!_suppress)
            _onChanged();
    }

    /// <summary>Sets the value from a preset without firing the recompute (the caller recomputes once).</summary>
    public void SetSilently(double value)
    {
        _suppress = true;
        Value = Math.Clamp(value, Min, Max);
        _suppress = false;
    }
}

/// <summary>One selectable perk on the shelf.</summary>
public sealed partial class PerkOption(
    string id, string name, string category, int cost, string description,
    IReadOnlyList<string> benefits, IReadOnlyList<string> drawbacks,
    IReadOnlyList<CharacterEffectLine> effects)
    : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Category { get; } = category;
    public int Cost { get; } = cost;
    public string Description { get; } = description;

    /// <summary>The good things this perk does, in plain language.</summary>
    public IReadOnlyList<string> Benefits { get; } = benefits;

    /// <summary>The costs this perk carries, in plain language.</summary>
    public IReadOnlyList<string> Drawbacks { get; } = drawbacks;

    /// <summary>The same mechanics with explicit EXPECTATION / CAREER / CAR boundary labels.</summary>
    public IReadOnlyList<CharacterEffectLine> Effects { get; } = effects;

    /// <summary>The perk cost shown with a sign ("+2", "0", "−1" for a refund).</summary>
    public string CostLabel => Cost > 0 ? $"+{Cost}" : Cost.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Unambiguous creation-only currency label for the GUI.</summary>
    public string CreationPointLabel => Cost switch
    {
        > 0 => $"COST {Cost}",
        0 => "NO COST",
        _ => $"REFUND {-Cost}",
    };

    public string CreationPointHelpText => Cost switch
    {
        > 0 => $"Uses {Cost} Creation Point{(Cost == 1 ? "" : "s")} from this starting build.",
        0 => "Uses no Creation Points.",
        _ => $"Returns {-Cost} Creation Point{(Cost == -1 ? "" : "s")} to this starting build.",
    };

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>A category header plus its perks, for the grouped shelf. <see cref="DisplayName"/> is the
/// friendly title ("era" → "Era-flavor"); <see cref="Name"/> is the raw id.</summary>
public sealed record PerkCategory(string Name, string DisplayName, IReadOnlyList<PerkOption> Perks);

/// <summary>One read-only progression-v2 mastery lane shown during character creation. These
/// previews are not creation traits and expose no acquisition command.</summary>
public sealed record MasteryPreviewFamily
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<MasteryPreviewSkill> Skills { get; init; }
}

/// <summary>Display-only authored mastery data for the new-career preview. Ownership remains empty
/// until the in-career atomic skill-plan workflow commits an acquisition.</summary>
public sealed record MasteryPreviewSkill
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Family { get; init; }
    public int Tier { get; init; }
    public int Order { get; init; }
    public required string IconKey { get; init; }
    public int Cost { get; init; }
    public int UnlockLevel { get; init; }
    public string? ExclusiveGroup { get; init; }
    public required IReadOnlyList<string> RequiresIds { get; init; }
    public required IReadOnlyList<string> RequiresLabels { get; init; }
    public required IReadOnlyList<string> Benefits { get; init; }
    public required IReadOnlyList<string> Drawbacks { get; init; }
    public required IReadOnlyList<CharacterEffectLine> Effects { get; init; }
}

/// <summary>One of the seven career-SP attribute rails shown read-only at creation.</summary>
public sealed record MasteryPreviewAttributeRail
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string StatId { get; init; }
    public required string StatLabel { get; init; }
    public required string Family { get; init; }
    public required string FamilyLabel { get; init; }
    public required string IconKey { get; init; }
    public double StepValue { get; init; }
    public double CapValue { get; init; }
    public int CostPerStep { get; init; }
    public int StepCount { get; init; }
    public int TotalCost { get; init; }
    public required IReadOnlyList<MasteryPreviewAttributeNode> Nodes { get; init; }
}

/// <summary>One authored step in a creation-time attribute-rail preview.</summary>
public sealed record MasteryPreviewAttributeNode
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int Tier { get; init; }
    public int Order { get; init; }
    public required string IconKey { get; init; }
    public int Cost { get; init; }
    public int UnlockLevel { get; init; }
    public double StepValue { get; init; }
    public double CapValue { get; init; }
    public required IReadOnlyList<string> RequiresIds { get; init; }
    public required IReadOnlyList<string> RequiresLabels { get; init; }
    public required IReadOnlyList<CharacterEffectLine> Effects { get; init; }
}

/// <summary>One authored share of the team-first expected-performance calculation, projected for
/// the creator's Advanced telemetry panel.</summary>
public sealed record CharacterExpectationComponent(
    string Label,
    int Percent,
    string Description);

/// <summary>One One-Trick Pony specialism choice: the rating <see cref="Field"/> and its friendly
/// <see cref="Label"/> (e.g. "wetSkill" → "wet-weather pace").</summary>
public sealed record FlavorOption(string Field, string Label);

/// <summary>One stable authored choice value and its display label. Values, never labels, are
/// persisted in <see cref="CharacterProfile.RacingDnaChoice"/>.</summary>
public sealed record RacingDnaChoiceOption(string Value, string Label);

/// <summary>Mode/pack-owned dynamic DNA choices. The character VM never invents a rival ID or
/// nationality; callers provide the exact eligible values after deterministic sorting.</summary>
public sealed record RacingDnaChoiceContext(
    IReadOnlyList<RacingDnaChoiceOption> RivalDrivers,
    IReadOnlyList<RacingDnaChoiceOption> Nationalities);

/// <summary>Binding projection for one Racing DNA card. Existing v1 Archetype bindings remain
/// available alongside this additive 30-card contract.</summary>
public sealed record RacingDnaCardViewModel
{
    public required string Id { get; init; }
    public int Version { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string PrimaryFamily { get; init; }
    public required string PrimaryFamilyLabel { get; init; }
    public required string SecondaryFamily { get; init; }
    public required string SecondaryFamilyLabel { get; init; }
    public string FamilyLine => $"{PrimaryFamilyLabel} / {SecondaryFamilyLabel}";
    public required IReadOnlyList<string> StartingTraitIds { get; init; }
    public required IReadOnlyList<string> StartingTraitNames { get; init; }
    public required IReadOnlyList<RacingDnaEffect> PersistentEffects { get; init; }
    public required IReadOnlyList<RacingDnaEffect> TradeoffEffects { get; init; }
    public IReadOnlyList<string> PersistentEffectSummaries =>
        PersistentEffects.Select(effect => effect.Summary).ToArray();
    public IReadOnlyList<string> TradeoffEffectSummaries =>
        TradeoffEffects.Select(effect => effect.Summary).ToArray();
    public RacingDnaChoiceKind? ChoiceKind { get; init; }
    public string? ChoicePrompt { get; init; }
    public bool ChoiceRequired { get; init; }
}
