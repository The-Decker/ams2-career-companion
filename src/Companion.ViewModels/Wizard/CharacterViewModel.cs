using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Core.Character;

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

        Stats = _rules.Stats.TalentStats.Select(s => new StatSlider(s.Id, Label(s.Id), Recompute)).ToList();
        MetaStats = _rules.Stats.MetaStats.Select(m => new StatSlider(m.Id, Label(m.Id), Recompute, m.Default)).ToList();

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
            .Select(g => new PerkCategory(g.Key, g.ToList()))
            .ToList();

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

    [ObservableProperty]
    private Archetype? _selectedArchetype;

    partial void OnSelectedArchetypeChanged(Archetype? value)
    {
        if (value is null)
            return;
        ApplyArchetype(value);
    }

    /// <summary>The creation character-point budget for PERKS (10 by default).</summary>
    public int Budget => _rules.CharacterPoints.CreationBudget;

    /// <summary>Net CP the selected perks cost (refund perks are negative).</summary>
    public int NetCpSpend => Perks.Where(p => p.IsSelected).Sum(p => p.Cost);

    /// <summary>Perk CP left over against the budget (never negative for display).</summary>
    public int RemainingCp => Math.Max(0, Budget - NetCpSpend);

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

    /// <summary>A build is valid when its perks fit the CP budget AND its stats fit the talent cap.</summary>
    public bool IsValid => PerksInBudget && StatsWithinCap;

    /// <summary>The one-line reason the current build is invalid, for the wizard footer; null when valid.</summary>
    public string? Invalidity => IsValid ? null
        : !StatsWithinCap
            ? $"Your stats total {StatTotal:0.00} of {StatCap:0.00} talent — lower one to raise another."
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
        OnPropertyChanged(nameof(PerksInBudget));
        OnPropertyChanged(nameof(StatTotal));
        OnPropertyChanged(nameof(StatsWithinCap));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(Invalidity));
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
            Stats = stats,
            PerkIds = Perks.Where(p => p.IsSelected).Select(p => p.Id).ToList(),
            CpUnspent = RemainingCp,
        };
    }

    private static string Label(string id) => CharacterLabels.Stat(id);
}

/// <summary>One stat slider, clamped to the character-creation band (0.15–0.85 for talent stats,
/// 0–1 for meta). Raises a recompute callback on user edits (not on preset application).</summary>
public sealed partial class StatSlider : ObservableObject
{
    private readonly Action _onChanged;
    private bool _suppress;

    public StatSlider(string id, string label, Action onChanged, double initial = 0.5)
    {
        Id = id;
        Label = label;
        _onChanged = onChanged;
        _value = Math.Clamp(initial, Min, Max);
    }

    public string Id { get; }
    public string Label { get; }
    public double Min => 0.15;
    public double Max => 0.85;

    [ObservableProperty]
    private double _value;

    partial void OnValueChanged(double value)
    {
        double clamped = Math.Clamp(value, Min, Max);
        if (clamped != value)
        {
            _value = clamped; // re-clamp without re-entrancy; the setter already ran
        }
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

/// <summary>A category header plus its perks, for the grouped shelf.</summary>
public sealed record PerkCategory(string Name, IReadOnlyList<PerkOption> Perks);
