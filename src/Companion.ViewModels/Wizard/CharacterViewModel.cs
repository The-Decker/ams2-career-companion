using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Core.Character;
using Companion.Core.Determinism;

namespace Companion.ViewModels.Wizard;

/// <summary>
/// The new-career wizard's character-creation step (docs/dev/character-system.md §5). Three tiers
/// over the same state: pick an ARCHETYPE preset (the one-click default — a pre-spent, in-budget
/// template), then optionally FREE-CUSTOMIZE the seven stat sliders and the perk shelf with a live
/// remaining-CP meter, with the raw numbers available for tinkerers. Produces a
/// <see cref="CharacterProfile"/> at confirm. Pure viewmodel — no I/O, fully unit-testable.
/// </summary>
public sealed partial class CharacterViewModel : ObservableObject
{
    private readonly CharacterRules _rules;

    public CharacterViewModel(CharacterRules rules, string? defaultName = null)
    {
        _rules = rules;
        _name = defaultName?.Trim() ?? "";

        Stats = _rules.Stats.TalentStats.Select(s => new StatSlider(
            s.Id, Label(s.Id), Recompute,
            min: s.CreationRange is { Count: 2 } r ? r[0] : 0.15,
            max: s.CreationRange is { Count: 2 } r2 ? r2[1] : 0.85,
            mapsTo: s.MapsTo, writeBase: s.WriteBase, writeSpan: s.WriteSpan)).ToList();
        // Meta stats have no rating analog, so they range over the full 0–1 the data allows (not the
        // talent 0.15–0.85 band).
        MetaStats = _rules.Stats.MetaStats.Select(m => new StatSlider(
            m.Id, Label(m.Id), Recompute, initial: m.Default,
            min: m.Range is { Count: 2 } r ? r[0] : 0.0,
            max: m.Range is { Count: 2 } r2 ? r2[1] : 1.0)).ToList();

        Perks = _rules.Perks
            .Select(p => new PerkOption(p.Id, p.Name, p.Category, p.Cost, p.Description,
                PerkDescriber.Benefits(p), PerkDescriber.Drawbacks(p)))
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

        // One-Trick Pony's specialism picker: the flavor ratings a player may lock onto (every
        // player-writable rating except raceSkill, which is the auto-taxed pace lever). Defaults to
        // the resolver's fallback so a never-touched picker still resolves consistently.
        EligibleFlavors = OneTrickFlavors
            .Select(f => new FlavorOption(f, CharacterLabels.Rating(f)))
            .ToList();
        _chosenFlavor = EligibleFlavors.FirstOrDefault(f => f.Field == PerkResolver.DefaultChosenFlavor)
            ?? EligibleFlavors[0];

        Archetypes = _rules.Creation.Archetypes.ToList();
        // The first archetype is the default one-click character — a complete, valid build.
        SelectedArchetype = Archetypes.FirstOrDefault();
    }

    /// <summary>The five talent-stat sliders (clamped to each stat's creation range).</summary>
    public IReadOnlyList<StatSlider> Stats { get; }

    /// <summary>The two career meta-stat sliders (marketability, durability).</summary>
    public IReadOnlyList<StatSlider> MetaStats { get; }

    public IReadOnlyList<PerkOption> Perks { get; }

    /// <summary>Perks grouped by category for the shelf (pace, racecraft, physical, …).</summary>
    public IReadOnlyList<PerkCategory> PerkCategories { get; }

    public IReadOnlyList<Archetype> Archetypes { get; }

    /// <summary>The player's driver name — the identity the whole app will use. Pre-filled with the
    /// seat's historical driver as a starting point; the player makes it their own.</summary>
    [ObservableProperty]
    private string _name;

    /// <summary>Youngest / oldest a created driver can be, and the default (a typical rookie).</summary>
    public const int MinAge = 16;
    public const int MaxAge = 45;
    public const int DefaultAge = 23;

    /// <summary>The driver's REAL age in their first season — the character's own age (16–45), which
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

    // ---- One-Trick Pony specialism ----

    private const string OneTrickPerkId = "one_trick";

    /// <summary>The rating fields One-Trick Pony may lock onto — every player-writable flavor rating
    /// except raceSkill (the auto-taxed pace lever stays out of the specialism).</summary>
    private static readonly string[] OneTrickFlavors =
        ["wetSkill", "tyreManagement", "qualifyingSkill", "aggression", "defending",
         "avoidanceOfMistakes", "consistency", "startReactions", "fuelManagement", "stamina"];

    /// <summary>The specialism options shown when One-Trick Pony is picked.</summary>
    public IReadOnlyList<FlavorOption> EligibleFlavors { get; }

    /// <summary>The player's chosen One-Trick specialism (the rating its +0.30 lands on and the only
    /// stat in-career development may raise). Only meaningful when <see cref="IsOneTrickSelected"/>.</summary>
    [ObservableProperty]
    private FlavorOption _chosenFlavor;

    /// <summary>True when the build carries One-Trick Pony, so the specialism picker is shown.</summary>
    public bool IsOneTrickSelected =>
        Perks.Any(p => p.IsSelected && string.Equals(p.Id, OneTrickPerkId, StringComparison.Ordinal));

    /// <summary>The creation character-point budget for PERKS (data-driven; 6 in the shipped rules).</summary>
    public int Budget => _rules.CharacterPoints.CreationBudget;

    /// <summary>The MOST perk points a build may spend — the budget plus the drawback refund headroom
    /// (9 = 6 + 3 in the shipped rules): taking a drawback-heavy perk refunds points you can pour into
    /// one premium upside. Displayed so the meter never reads a nonsensical "8 of 6".</summary>
    public int MaxPerkPoints => _rules.CharacterPoints.MaxNetSpend;

    /// <summary>Net CP the selected perks cost (refund perks are negative).</summary>
    public int NetCpSpend => Perks.Where(p => p.IsSelected).Sum(p => p.Cost);

    /// <summary>Perk CP left over against the budget (never negative for display).</summary>
    public int RemainingCp => Math.Max(0, Budget - NetCpSpend);

    /// <summary>How many perks are chosen right now (an archetype supplies its signature few).</summary>
    public int SelectedPerkCount => Perks.Count(p => p.IsSelected);

    /// <summary>The most perks a creation build may carry, or null for no count limit.</summary>
    public int? MaxPerks => _rules.CharacterPoints.MaxPerks;

    /// <summary>Perks fit the count cap — an archetype plus only a few more. No cap = always true.</summary>
    public bool PerksWithinCount => MaxPerks is not int cap || SelectedPerkCount <= cap;

    /// <summary>Perks fit the audited CP window [minBudgetAfterSpend, budget + maxRefundHeadroom].</summary>
    public bool PerksInBudget =>
        NetCpSpend >= _rules.CharacterPoints.MinBudgetAfterSpend
        && NetCpSpend <= _rules.CharacterPoints.MaxNetSpend;

    /// <summary>The most total talent a driver may carry across the seven stats. Redistribution is
    /// free — being a 0.85 somewhere means being low elsewhere — but the SUM is capped, so no driver
    /// is great at everything. Data-driven (perks.json).</summary>
    public double StatCap => _rules.CharacterPoints.StatSumCap;

    /// <summary>The current total across all seven stats.</summary>
    public double StatTotal => Stats.Concat(MetaStats).Sum(s => s.Value);

    public bool StatsWithinCap => StatTotal <= StatCap + 1e-9;

    /// <summary>A build is valid when its perks fit the count cap AND the CP budget AND its stats
    /// fit the talent cap.</summary>
    public bool IsValid => PerksWithinCount && PerksInBudget && StatsWithinCap;

    /// <summary>The one-line reason the current build is invalid, for the wizard footer; null when valid.</summary>
    public string? Invalidity => IsValid ? null
        : !StatsWithinCap
            ? $"Your stats total {StatTotal:0.00} of {StatCap:0.00} talent — lower one to raise another."
            : !PerksWithinCount
                ? $"You've picked {SelectedPerkCount} perks — a driver carries at most {MaxPerks}. Drop one."
                : NetCpSpend < _rules.CharacterPoints.MinBudgetAfterSpend
                    ? $"This build banks more perk points than allowed — spend at least {_rules.CharacterPoints.MinBudgetAfterSpend}."
                    : $"These perks cost {NetCpSpend} of a {_rules.CharacterPoints.MaxNetSpend} maximum — drop one.";

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

    private void Recompute()
    {
        OnPropertyChanged(nameof(NetCpSpend));
        OnPropertyChanged(nameof(RemainingCp));
        OnPropertyChanged(nameof(SelectedPerkCount));
        OnPropertyChanged(nameof(PerksWithinCount));
        OnPropertyChanged(nameof(PerksInBudget));
        OnPropertyChanged(nameof(StatTotal));
        OnPropertyChanged(nameof(StatsWithinCap));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(Invalidity));
        OnPropertyChanged(nameof(IsOneTrickSelected));
    }

    /// <summary>The authored character this step produces at confirm.</summary>
    public CharacterProfile BuildProfile()
    {
        var stats = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var slider in Stats.Concat(MetaStats))
            stats[slider.Id] = slider.Value;

        return new CharacterProfile
        {
            Name = Name.Trim(),
            Age = Math.Clamp(Age, MinAge, MaxAge),
            Stats = stats,
            PerkIds = Perks.Where(p => p.IsSelected).Select(p => p.Id).ToList(),
            // Record the chosen specialism only when One-Trick Pony is actually taken, so a build
            // without it serialises with no ChosenFlavor (byte-identical to a legacy profile).
            ChosenFlavor = IsOneTrickSelected ? ChosenFlavor.Field : null,
            CpUnspent = RemainingCp,
        };
    }

    // ---- Random balanced build (character-gen) ----

    private int _rerollIndex;

    /// <summary>Rolls a complete, valid, in-budget character: a random archetype base (its perks are
    /// valid by construction) with a freshly randomised stat spread under the talent cap. Every click
    /// advances a deterministic PCG so the sequence is stable, and the RESULT is what gets journaled
    /// at Confirm (in the <c>player.character</c> row), so the career always replays byte-for-byte —
    /// the roll itself is never re-run. Mirrors the archetype-apply path (SetSilently + Recompute).</summary>
    [RelayCommand]
    private void RandomBuild()
    {
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

        SelectedArchetype = null; // the spread is no longer the preset's — clear the selection
        Recompute();
    }

    /// <summary>Distributes the talent budget randomly: each stat starts at its floor, then the
    /// remaining cap mass is sprinkled across the stats (each capped at its ceiling), so the result is
    /// always in-range and under the sum cap — a valid spread with no rejection loop.</summary>
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
    IReadOnlyList<string> benefits, IReadOnlyList<string> drawbacks)
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

    /// <summary>The perk cost shown with a sign ("+2", "0", "−1" for a refund).</summary>
    public string CostLabel => Cost > 0 ? $"+{Cost}" : Cost.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>A category header plus its perks, for the grouped shelf. <see cref="DisplayName"/> is the
/// friendly title ("era" → "Era-flavor"); <see cref="Name"/> is the raw id.</summary>
public sealed record PerkCategory(string Name, string DisplayName, IReadOnlyList<PerkOption> Perks);

/// <summary>One One-Trick Pony specialism choice: the rating <see cref="Field"/> and its friendly
/// <see cref="Label"/> (e.g. "wetSkill" → "wet-weather pace").</summary>
public sealed record FlavorOption(string Field, string Label);
